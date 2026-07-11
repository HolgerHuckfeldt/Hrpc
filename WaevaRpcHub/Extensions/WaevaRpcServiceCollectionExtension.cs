using WaevaRpcCommon;
using WaevaRpcCommon.MessagePack;
using WaevaRpcCore.Attributes;
using WaevaRpcCore.Services;
using MessagePack;
using Microsoft.AspNetCore.RateLimiting;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.RateLimiting;

namespace WaevaRpcHub.Extensions
{
    public static class WaevaRpcServiceCollectionExtension
    {
        /// <summary>
        /// Registriert WaevaRpc mit Konfigurations-Callback — das ist der empfohlene
        /// Einstiegspunkt für eine v1.0-Server-Setup. WebSocket ist der primäre
        /// (Default-)Kanal; SignalR ist über <see cref="WaevaRpcOptions.UseSignalR"/>
        /// optional zuschaltbar. Die Pipeline muss anschließend
        /// <c>app.UseWebSockets(); app.UseWaevaRpcWebSocket(); app.UseWaevaRpc();</c>
        /// (und bei Bedarf <c>app.MapWaevaRpcEndpoints()</c>) aufrufen.
        ///
        /// Usage:
        /// <code>
        /// builder.Services.AddWaevaRpc();                       // Defaults (WS primär)
        /// // oder
        /// builder.Services.AddWaevaRpc(o =&gt;
        /// {
        ///     o.EnableDetailedErrors = builder.Environment.IsDevelopment();
        ///     o.RateLimitPermitLimit = 50;                  // nur in Prod
        ///     o.UseSignalR = true;                          // optionaler 2. Kanal
        /// });
        /// </code>
        /// </summary>
        public static IServiceCollection AddWaevaRpc(this IServiceCollection services,
            Action<WaevaRpcOptions>? configure = null)
        {
            var options = new WaevaRpcOptions();
            configure?.Invoke(options);
            return services.AddWaevaRpc(options);
        }

        public static IServiceCollection AddWaevaRpc(this IServiceCollection services,
            WaevaRpcOptions options)
        {
            // Options als Singleton in DI ablegen, damit die Pipeline-Extensions
            // (UseWaevaRpcTransports/MapWaevaRpc) UseSignalR etc. ohne Parameter lesen können.
            services.AddSingleton(options);

            if (options.UseSignalR)
            {
                // Add SignalR as the transport-layer.
                var fastHub = services.AddSignalR(
                    o =>
                    {
                        o.EnableDetailedErrors = options.EnableDetailedErrors;
                        o.MaximumReceiveMessageSize = options.MaximumReceiveMessageSize;
                        o.StreamBufferCapacity = options.StreamBufferCapacity;
                        o.MaximumParallelInvocationsPerClient = options.MaximumParallelInvocationsPerClient;
                    });

                if (options.UseMessagePack)
                {
                    // Custom Resolver: JsonElement (WaevaRpcResponse.Data seit dem Single-Pass-
                    // Fix) wird als native MessagePack-Tokens serialisiert, nicht als
                    // escapeter JSON-String — keine Double-Wrapping-Tax auf dem SignalR-Kanal.
                    fastHub.AddMessagePackProtocol(o =>
                        o.SerializerOptions = MessagePackSerializerOptions.Standard
                            .WithResolver(JsonElementResolver.Instance));
                }
            }

            // Minimal-API (REST) JSON-Options host-weit konfigurieren: camelCase +
            // relaxed Encoder. Wirkt auf ALLE Minimal-API-Endpoints des Hosts (WaevaRpc ist
            // das Framework, das den Host bereitstellt) — Data (JsonElement) wird damit
            // roh in einem Pass serialisiert, ohne `"`-Escape-Tax.
            // WaevaRpcResponseJsonConverter (Write-only): DataBytes via WriteRawValue roh in
            // den Wire → kein JsonDocument-Baum auf dem Server (Single-Pass-Optimierung).
            services.ConfigureHttpJsonOptions(o =>
            {
                o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                o.SerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
                o.SerializerOptions.Converters.Add(new WaevaRpcResponseJsonConverter());
            });

            // Rate Limiting – always register to ensure UseRateLimiter works.
            services.AddRateLimiter(rateLimiterOptions =>
            {
                if (options.RateLimitPermitLimit > 0)
                {
                    rateLimiterOptions.AddFixedWindowLimiter("waeva", opt =>
                    {
                        opt.PermitLimit = options.RateLimitPermitLimit;
                        opt.Window = TimeSpan.FromSeconds(options.RateLimitWindowSeconds);
                        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                        opt.QueueLimit = 0;
                    });
                    rateLimiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                }
            });

            // Add the WaevaRpcService
            // Singleton for performance-reasons.
            // The WaevaRpcService will create a scope for the controllers.
            services.AddSingleton<IWaevaRpcCore>(sp =>
            {
                var invoker = new WaevaRpcCore.Services.WaevaRpcInvoker(
                    sp.GetRequiredService<IServiceScopeFactory>(),
                    sp.GetService<Microsoft.Extensions.Logging.ILogger<WaevaRpcCore.Services.WaevaRpcInvoker>>()
                        ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<WaevaRpcCore.Services.WaevaRpcInvoker>.Instance,
                    sp.GetService<IEnumerable<WaevaRpcCore.Services.IWaevaRpcInterceptor>>());

                // Detailed Errors aktivieren, wenn explizit gewünscht oder in Development.
                var env = sp.GetService<Microsoft.Extensions.Hosting.IHostEnvironment>();
                invoker.EnableDetailedErrors = options.EnableDetailedErrors || (env?.IsDevelopment() ?? false);
                return invoker;
            });

            // Register built-in interceptors
            services.AddSingleton<WaevaRpcCore.Services.IWaevaRpcInterceptor, WaevaRpcCore.Services.WaevaRpcLoggingInterceptor>();

            // Register Fast-Controller as Scoped.
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (Type type in assembly.GetTypes())
                {
                    if (type.GetCustomAttributes(typeof(WaevaRpcControllerAttribute), true).Length > 0)
                    {
                        services.AddScoped(type);
                    }
                }
            }

            return services;
        }

        public static IApplicationBuilder UseWaevaRpc(
            this IApplicationBuilder app)
        {
            app.ApplicationServices.CreateScope();

            var waevaRpcService = app.ApplicationServices.GetService<WaevaRpcCore.Services.IWaevaRpcCore>();
            if (waevaRpcService == null)
            {
                return app;
            }

            // If a WaevaRpcControllerBuilder was registered (fluent API), use it
            var builder = app.ApplicationServices.GetService<WaevaRpcControllerBuilder>();
            if (builder != null)
            {
                builder.Apply(app.ApplicationServices, waevaRpcService);
                return app;
            }

            // Fallback: register all auto-discovered [WaevaRpcController] types
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (Type type in assembly.GetTypes())
                {
                    if (type.GetCustomAttributes(typeof(WaevaRpcControllerAttribute), true).Length > 0)
                    {
                        waevaRpcService.Register(type);
                    }
                }
            }

            return app;
        }
    }
}
