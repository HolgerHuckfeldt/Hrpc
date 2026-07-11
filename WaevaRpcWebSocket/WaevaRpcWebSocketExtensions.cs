using Microsoft.AspNetCore.Builder;

namespace WaevaRpcWebSocket;

/// <summary>
/// Extension-Methoden, um den schlanken WaevaRpc-WebSocket-Transport einfach einzubinden.
/// </summary>
public static class WaevaRpcWebSocketExtensions
{
    /// <summary>
    /// Fügt die WaevaRpc-WebSocket-Middleware der Pipeline hinzu.
    /// Verwenden Sie vorher app.UseWebSockets().
    /// </summary>
    public static IApplicationBuilder UseWaevaRpcWebSocket(this IApplicationBuilder app, string path = "/waevaws")
    {
        return app.Map(path, application =>
        {
            application.UseMiddleware<WaevaRpcWebSocketMiddleware>();
        });
    }
}
