using HrpcCore.Services;

namespace HrpcRest
{
    public static class HrpcEndpointExtensions
    {
        /// <summary>
        /// Fügt die hrpc-Endpunkte zur Anwendung hinzu.
        /// </summary>
        /// <param name="endpoints">Der IEndpointRouteBuilder der Anwendung.</param>
        /// <param name="prefix">Das URL-Präfix für die hrpc-Endpunkte.</param>
        /// <returns>Der IEndpointRouteBuilder für weitere Konfigurationen.</returns>
        public static IEndpointRouteBuilder MapHrpcEndpoints(this IEndpointRouteBuilder endpoints, string prefix = "/api/hrpc")
        {
            // Erstellt eine Gruppe für alle deine Routen. Das ist sauber und vermeidet Konflikte.
            var group = endpoints.MapGroup(prefix);

            group.MapPost("/json", async (
                HrpcRequest request, IHrpcCore hrpcService, HttpContext httpContext, CancellationToken ct) =>
            {
                try
                {
                    var result = await hrpcService.InvokeDi(request, httpContext, ct);
                    return Results.Ok(result);
                }
                catch (OperationCanceledException)
                {
                    return Results.Json(new { title = "Request cancelled.", status = 499 },
                        statusCode: 499);
                }
            });

            group.MapPost("/json/multi", async (
                HrpcMultiRequest? request, IHrpcCore hrpcService, HttpContext httpContext, CancellationToken ct) =>
            {
                if (request?.Requests == null) return Results.BadRequest("Request is empty.");
                try
                {
                    var result = await hrpcService.InvokeDi(request.Requests, httpContext, request.Mode, ct);
                    return Results.Ok(result);
                }
                catch (OperationCanceledException)
                {
                    return Results.Json(new { title = "Request cancelled.", status = 499 },
                        statusCode: 499);
                }
            });

            group.MapGet("/discovery", (IHrpcCore hrpcService) =>
            {
                var allControllers = hrpcService.GetDiscoveryInfo();
                return Results.Ok(allControllers);
            });
           
            return group;
        }
    }
}
