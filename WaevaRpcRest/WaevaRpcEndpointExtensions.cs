using WaevaRpcCore.Services;
using Microsoft.AspNetCore.Mvc;

namespace WaevaRpcRest
{
    public static class WaevaRpcEndpointExtensions
    {
        /// <summary>
        /// Fügt die WaevaRpc-REST-Endpunkte (Minimal-API) zur Anwendung hinzu.
        /// Das ist die kanonische REST-Implementierung für v1.0 — früher gab es
        /// zusätzlich einen MVC-Controller mit denselben Routen, der bei
        /// <c>AddControllers()</c> zu AmbiguousMatchException führte; er ist
        /// zugunsten dieses Pfads entfallen.
        /// </summary>
        /// <param name="endpoints">Der IEndpointRouteBuilder der Anwendung.</param>
        /// <param name="prefix">Das URL-Präfix für die WaevaRpc-Endpunkte.</param>
        /// <param name="enableRateLimiting">
        /// Wenn <c>true</c>, wird die Policy <c>waevaRpc</c> auf alle Endpunkte
        /// angewandt (<c>RequireRateLimiting</c>). Nur aktivieren, wenn der Host
        /// die Policy registriert hat (AddWaevaRpc mit RateLimitPermitLimit &gt; 0).
        /// </param>
        /// <returns>Der IEndpointRouteBuilder für weitere Konfigurationen.</returns>
        public static IEndpointRouteBuilder MapWaevaRpcEndpoints(this IEndpointRouteBuilder endpoints,
            string prefix = "/api/waeva", bool enableRateLimiting = false)
        {
            // Erstellt eine Gruppe für alle Routen — sauber und konfliktfrei.
            var group = endpoints.MapGroup(prefix);

            // 1 MB max Request-Body — entspricht dem früheren MVC-RequestSizeLimit.
            group.WithMetadata(new RequestSizeLimitAttribute(1_048_576));

            // Per-Endpoint Rate Limiting nur auf ausdrücklichen Wunsch, damit Hosts
            // ohne registrierte "waeva"-Policy keinen Runtime-Fehler produzieren.
            if (enableRateLimiting)
            {
                group.RequireRateLimiting("waeva");
            }

            group.MapPost("/json", async (
                WaevaRpcRequest request, IWaevaRpcCore waevaRpcService, HttpContext httpContext, CancellationToken ct) =>
            {
                try
                {
                    var result = await waevaRpcService.InvokeDi(request, httpContext, ct);
                    return Results.Ok(result);
                }
                catch (OperationCanceledException)
                {
                    return Results.Json(new { title = "Request cancelled.", status = 499 },
                        statusCode: 499);
                }
            });

            group.MapPost("/json/multi", async (
                WaevaRpcMultiRequest? request, IWaevaRpcCore waevaRpcService, HttpContext httpContext, CancellationToken ct) =>
            {
                if (request?.Requests is null || request.Requests.Count == 0)
                    return Results.BadRequest("Request is empty.");
                try
                {
                    var result = await waevaRpcService.InvokeDi(request.Requests, httpContext, request.Mode, ct);
                    return Results.Ok(result);
                }
                catch (OperationCanceledException)
                {
                    return Results.Json(new { title = "Request cancelled.", status = 499 },
                        statusCode: 499);
                }
            });

            group.MapGet("/discovery", (IWaevaRpcCore waevaRpcService) =>
            {
                var allControllers = waevaRpcService.GetDiscoveryInfo();
                return Results.Ok(allControllers);
            });

            return group;
        }
    }
}
