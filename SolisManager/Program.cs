using System.Diagnostics;
using SolisManager.APIWrappers;
using SolisManager.Components;
using Coravel;
using Coravel.Invocable;
using MudBlazor.Services;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using SolisManager.Services;
using SolisManager.Shared;
using SolisManager.Shared.Models;

namespace SolisManager;

public class Program
{
    private const int solisManagerPort = 5169;
    
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Host.UseSerilog((hostContext, services, configuration) =>
        {
            InitLogConfiguration(configuration, ".");
        });

        // Add services to the container.
        builder.Services.AddRazorComponents()
            .AddInteractiveWebAssemblyComponents();

        builder.Services.AddSingleton<SolisManagerConfig>();
        builder.Services.AddSingleton<InverterManager>();
        builder.Services.AddSingleton<IInverterService>(x => x.GetRequiredService<InverterManager>());
        builder.Services.AddSingleton<IInverterRefreshService>(x => x.GetRequiredService<InverterManager>());

        builder.Services.AddSingleton<BatteryScheduler>();
        builder.Services.AddSingleton<RatesScheduler>();
        builder.Services.AddSingleton<SolcastScheduler>();

        builder.Services.AddSingleton<SolisAPI>();
        builder.Services.AddSingleton<SolcastAPI>();
        builder.Services.AddSingleton<OctopusAPI>();

        builder.Services.AddScheduler();
        builder.Services.AddMudServices();
        
        if (!Debugger.IsAttached)
        {
            // Use Kestrel options to set the port. Using .Urls.Add breaks WASM debugging.
            // This line also breaks wasm debugging in Rider.
            // See https://github.com/dotnet/aspnetcore/issues/43703
            builder.WebHost.UseKestrel(serverOptions => { serverOptions.ListenAnyIP(solisManagerPort); });
        }

        var app = builder.Build();
        
        // First, load the config
        var config = app.Services.GetRequiredService<SolisManagerConfig>();
        if (!config.ReadFromFile())
        {
            config.OctopusProduct = "AGILE-24-10-01";
            config.OctopusProductCode = "E-1R-AGILE-24-10-01-A";
            config.SlotsForFullBatteryCharge = 6;
            config.AlwaysChargeBelowPrice = 10;
        }

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.UseWebAssemblyDebugging();
        }
        else
        {
            app.UseExceptionHandler("/Error");
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }

        app.UseHttpsRedirection();

        app.UseRouting();
        app.UseAntiforgery();

        app.MapStaticAssets();
        app.MapRazorComponents<App>()
            .AddInteractiveWebAssemblyRenderMode()
            .AddAdditionalAssemblies(typeof(Client._Imports).Assembly);

        app.ConfigureAPIEndpoints();

        // Refresh and apply the octopus rates every 30 mins
        app.Services.UseScheduler(s => s
            .Schedule<RatesScheduler>()
            .Cron("0,30 * * * *")
            .RunOnceAtStart());
        
        // Update the battery every 5 minutes. Skip the 0 / 30
        // minute slots, because it gets updated when we refresh
        // rates anyway. Don't need to run at startup, for the 
        // same reason.
        app.Services.UseScheduler(s => s
            .Schedule<BatteryScheduler>()
            .Cron("5,10,15,20,25,35,40,45,50,55 * * * *"));

        // Get the solcast data every four hours
        app.Services.UseScheduler(s => s
            .Schedule<SolcastScheduler>()
            .Cron("0 */4 * * *")
            .RunOnceAtStart());

        await app.RunAsync();
    }
    
    private const string template = "[{Timestamp:HH:mm:ss.fff}-{ThreadID}-{Level:u3}] {Message:lj}{NewLine}{Exception}";
    private static readonly LoggingLevelSwitch logLevel = new();

    /// <summary>
    ///     Initialise logging and add the thread enricher.
    /// </summary>
    /// <returns></returns>
    public static void InitLogConfiguration(LoggerConfiguration config, string logFolder)
    {
        try
        {
            if ( !Directory.Exists(logFolder) )
            {
                Console.WriteLine($"Creating log folder {logFolder}");
                Directory.CreateDirectory(logFolder);
            }
            
            logLevel.MinimumLevel = LogEventLevel.Information;
            var logFilePattern = Path.Combine(logFolder, "SolisManager-.log");

            config.WriteTo.Console(outputTemplate: template,
                    levelSwitch: logLevel)
                  .WriteTo.File(logFilePattern,
                    outputTemplate: template,
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: 104857600,
                    retainedFileCountLimit: 10,
                    levelSwitch: logLevel)
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning);
        }
        catch ( Exception ex )
        {
            Console.WriteLine($"Unable to initialise logs: {ex}");
        }
    }
}