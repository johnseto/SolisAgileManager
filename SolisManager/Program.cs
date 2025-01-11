using System.Diagnostics;
using System.Reflection.Metadata;
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

    public static string ConfigFolder => configFolder;
    
    private static string configFolder = "config";
    
    public static async Task Main(string[] args)
    {
        if (args.Length > 0)
        {
            var folder = args[0];
            if (!string.IsNullOrEmpty(folder))
            {
                if (Directory.Exists(folder))
                {
                    Console.WriteLine($"Config folder set to {folder}.");
                    configFolder = folder;
                }
                else if (SafeCreateFolder(ConfigFolder))
                {
                    configFolder = folder;
                    Console.WriteLine($"Created config folder: {ConfigFolder}.");
                }
                else
                {
                    Console.WriteLine($"Config folder {folder} did not exist and unable to create it. Exiting...");
                    return;
                }
            }

            if( string.IsNullOrEmpty(folder))
            {
                Console.WriteLine($"Using default folder \"{configFolder}\".");
            }
        }

        var builder = WebApplication.CreateBuilder(args);

        builder.Host.UseSerilog((hostContext, services, configuration) =>
        {
            InitLogConfiguration(configuration, ConfigFolder);
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

        var logger = app.Services.GetRequiredService<ILogger<Program>>();
        
        logger.LogInformation("Application started. Logs being written to {C}", ConfigFolder);
        
        // First, load the config
        var config = app.Services.GetRequiredService<SolisManagerConfig>();
        if (!config.ReadFromFile(ConfigFolder))
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

        // Get the solcast data every six hours. Run it on the 
        // 13th minute, because that reduces load (half of the world
        // runs their solcast ingestion on the hour).
        // Don't run at first startup. It means you won't get 
        // data for a while, but that's probably okay.
        app.Services.UseScheduler(s => s
            .Schedule<SolcastScheduler>()
            .Cron("13 */6 * * *"));
        
        await app.RunAsync();
    }
    
    private const string template = "[{Timestamp:HH:mm:ss.fff}-{ThreadID}-{Level:u3}] {Message:lj}{NewLine}{Exception}";
    private static readonly LoggingLevelSwitch logLevel = new();

    private static bool SafeCreateFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            return true;
        }
        catch
        {
            return false;
        }
    }

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