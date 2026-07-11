using WaevaRpcHub.Extensions;
using WaevaRpcDeveloperUi;
using WaevaRpcRest;
using WaevaRpcWebSocket;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace WaevaRpcServer;

/// <summary>
/// Vereinheitlichte Pipeline-Extensions für das WaevaRpc-Server-Setup.
/// Ergänzt <c>AddWaevaRpc</c> (Services-Stage) um die Transport-Middleware und
/// die Endpoint-Mappings in jeweils einem Aufruf — das v1.0-Server-Setup
/// reduziert sich auf drei Zeilen:
/// <code>
/// builder.Services.AddWaevaRpc(o =&gt; { /* … */ });
/// app.UseWaevaRpcTransports();   // WebSocket (primär) + Controller-Registrierung
/// app.MapWaevaRpc();             // REST + Developer-UI + optional SignalR-Hub
/// </code>
/// </summary>
public static class WaevaRpcPipelineExtensions
{
    /// <summary>
    /// Aktiviert die WaevaRpc-Transport-Middleware in einem Aufruf:
    /// <c>UseWebSockets</c> + WebSocket-Transport (primärer Kanal) +
    /// Controller-Registrierung via <c>UseWaevaRpc</c>.
    /// SignalR ist kein Middleware-Transport — sein Hub wird über
    /// <see cref="MapWaevaRpc"/> gemappt, sobald <see cref="WaevaRpcOptions.UseSignalR"/>
    /// aktiv ist.
    /// </summary>
    /// <param name="app">Die Application-Builder-Pipeline.</param>
    /// <param name="webSocketPath">Pfad des WebSocket-Transports (Default <c>/waevaws</c>).</param>
    public static IApplicationBuilder UseWaevaRpcTransports(this IApplicationBuilder app,
        string webSocketPath = "/waevaws")
    {
        app.UseWebSockets();
        app.UseWaevaRpcWebSocket(webSocketPath);
        app.UseWaevaRpc();
        return app;
    }

    /// <summary>
    /// Mappt alle WaevaRpc-Endpunkte in einem Aufruf: REST (<paramref name="restPrefix"/>)
    /// + Developer-UI (<paramref name="developerUiPath"/>) + optional den SignalR-Hub
    /// unter <paramref name="hubPath"/>, falls <see cref="WaevaRpcOptions.UseSignalR"/>
    /// in der Services-Konfiguration aktiviert wurde (die Options werden aus DI
    /// gelesen, wenn <c>AddWaevaRpc</c> sie registriert hat).
    /// </summary>
    public static IEndpointRouteBuilder MapWaevaRpc(this IEndpointRouteBuilder endpoints,
        string restPrefix = "/api/waeva",
        string developerUiPath = "/developer",
        string hubPath = "/Waeva")
    {
        var options = endpoints.ServiceProvider.GetService<WaevaRpcOptions>();

        // Rate Limiting nur durchreichen, wenn der Host die "waeva"-Policy registriert hat.
        endpoints.MapWaevaRpcEndpoints(restPrefix, enableRateLimiting: options?.RateLimitPermitLimit > 0);
        endpoints.MapWaevaRpcDeveloperUi(developerUiPath);

        if (options?.UseSignalR == true)
        {
            // Vollqualifiziert, um den Namen vom gleichnamigen Namespace zu trennen.
            endpoints.MapHub<WaevaRpcHub.Hub.WaevaRpcHub>(hubPath);
        }

        return endpoints;
    }
}