using Microsoft.AspNetCore.Builder;

namespace HrpcWebSocket;

/// <summary>
/// Extension-Methoden, um den schlanken Hrpc-WebSocket-Transport einfach einzubinden.
/// </summary>
public static class HrpcWebSocketExtensions
{
    /// <summary>
    /// Fügt die Hrpc-WebSocket-Middleware der Pipeline hinzu.
    /// Verwenden Sie vorher app.UseWebSockets().
    /// </summary>
    public static IApplicationBuilder UseHrpcWebSocket(this IApplicationBuilder app, string path = "/hrpcws")
    {
        return app.Map(path, application =>
        {
            application.UseMiddleware<HrpcWebSocketMiddleware>();
        });
    }
}
