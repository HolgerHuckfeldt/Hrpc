using HrpcHub.Extensions;
using HrpcDeveloperUi;
using HrpcRest;
using HrpcWebSocket;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HrpcServer;

/// <summary>
/// Vereinheitlichte Pipeline-Extensions für das Hrpc-Server-Setup.
/// Ergänzt <c>AddHrpc</c> (Services-Stage) um die Transport-Middleware und
/// die Endpoint-Mappings in jeweils einem Aufruf — das v1.0-Server-Setup
/// reduziert sich auf drei Zeilen:
/// <code>
/// builder.Services.AddHrpc(o =&gt; { /* … */ });
/// app.UseHrpcTransports();   // WebSocket (primär) + Controller-Registrierung
/// app.MapHrpc();             // REST + Developer-UI + optional SignalR-Hub
/// </code>
/// </summary>
public static class HrpcPipelineExtensions
{
    /// <summary>
    /// Aktiviert die Hrpc-Transport-Middleware in einem Aufruf:
    /// <c>UseWebSockets</c> + WebSocket-Transport (primärer Kanal) +
    /// Controller-Registrierung via <c>UseHrpc</c>.
    /// SignalR ist kein Middleware-Transport — sein Hub wird über
    /// <see cref="MapHrpc"/> gemappt, sobald <see cref="HrpcOptions.UseSignalR"/>
    /// aktiv ist.
    /// </summary>
    /// <param name="app">Die Application-Builder-Pipeline.</param>
    /// <param name="webSocketPath">Pfad des WebSocket-Transports (Default <c>/hrpcws</c>).</param>
    public static IApplicationBuilder UseHrpcTransports(this IApplicationBuilder app,
        string webSocketPath = "/hrpcws")
    {
        app.UseWebSockets();
        app.UseHrpcWebSocket(webSocketPath);
        app.UseHrpc();
        return app;
    }

    /// <summary>
    /// Mappt alle Hrpc-Endpunkte in einem Aufruf: REST (<paramref name="restPrefix"/>)
    /// + Developer-UI (<paramref name="developerUiPath"/>) + optional den SignalR-Hub
    /// unter <paramref name="hubPath"/>, falls <see cref="HrpcOptions.UseSignalR"/>
    /// in der Services-Konfiguration aktiviert wurde (die Options werden aus DI
    /// gelesen, wenn <c>AddHrpc</c> sie registriert hat).
    /// </summary>
    public static IEndpointRouteBuilder MapHrpc(this IEndpointRouteBuilder endpoints,
        string restPrefix = "/api/hrpc",
        string developerUiPath = "/developer",
        string hubPath = "/Hrpc")
    {
        var options = endpoints.ServiceProvider.GetService<HrpcOptions>();

        // Rate Limiting nur durchreichen, wenn der Host die "hrpc"-Policy registriert hat.
        endpoints.MapHrpcEndpoints(restPrefix, enableRateLimiting: options?.RateLimitPermitLimit > 0);
        endpoints.MapHrpcDeveloperUi(developerUiPath);

        if (options?.UseSignalR == true)
        {
            // Vollqualifiziert, um den Namen vom gleichnamigen Namespace zu trennen.
            endpoints.MapHub<HrpcHub.Hub.HrpcHub>(hubPath);
        }

        return endpoints;
    }
}