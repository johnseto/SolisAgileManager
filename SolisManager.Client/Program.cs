using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using SolisManager.Client.Services;
using SolisManager.Shared;

namespace SolisManager.Client;

class Program
{
    static async Task Main(string[] args)
    {
        var builder = WebAssemblyHostBuilder.CreateDefault(args);

        builder.Services.AddScoped( x => new HttpClient
        {
            BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
        });

        builder.Services.AddScoped<IInverterService, ClientInverterService>();
        builder.Services.AddMudServices();
        
        await builder.Build().RunAsync();
    }
}