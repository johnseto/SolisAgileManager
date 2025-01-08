using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using SolisManager.Client.Pages;
using SolisManager.Shared;
using SolisManager.Shared.Models;

namespace SolisManager.APIWrappers;

public static class EndpointMapper
{
    public static IEndpointRouteBuilder ConfigureAPIEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGroup("inverter")
            .MapAgilePricesApi()
            .MapGetConfigAPI()
            .MapToolsAPI()
            .MapSaveConfigAPI();
        
        return app;
    }

    private static RouteGroupBuilder MapAgilePricesApi(this RouteGroupBuilder group)
    {
        group.MapGet("agileprices",
            async ([FromServices] IInverterService service) =>
            {
                var prices = await service.GetAgilePriceSlots();
                return TypedResults.Ok(prices);
            });

        group.MapGet("history",
            async ([FromServices] IInverterService service) =>
            {
                var history = await service.GetHistory();
                return TypedResults.Ok(history);
            });

        return group;
    }

    private static RouteGroupBuilder MapGetConfigAPI(this RouteGroupBuilder group)
    {
        group.MapGet("getconfig",
           async  ([FromServices] IInverterService service) =>
            {
                var config = await service.GetConfig();
                return TypedResults.Ok(config);
            });

        return group;
    }

    private static RouteGroupBuilder MapToolsAPI(this RouteGroupBuilder group)
    {
        group.MapGet("chargebattery",
            async  ([FromServices] IInverterService service) =>
            {
                await service.ChargeBattery();
                return TypedResults.Ok();
            });

        group.MapGet("dischargebattery",
            async  ([FromServices] IInverterService service) =>
            {
                await service.DischargeBattery();
                return TypedResults.Ok();
            });

        group.MapGet("dumpandchargebattery",
            async  ([FromServices] IInverterService service) =>
            {
                await service.DumpAndChargeBattery();
                return TypedResults.Ok();
            });

        group.MapGet("clearoverrides",
            async  ([FromServices] IInverterService service) =>
            {
                await service.ClearOverrides();
                return TypedResults.Ok();
            });
        return group;
    }

    private static RouteGroupBuilder MapSaveConfigAPI(this RouteGroupBuilder group)
    {
        group.MapPost("saveconfig",
            async (string configJson, 
                [FromServices] IInverterService inverterService) =>
            {
                var configToSave = JsonSerializer.Deserialize<SolisManagerConfig>(configJson);
                await inverterService.SaveConfig(configToSave);
                return TypedResults.Ok();
            });

        return group;
    }
}