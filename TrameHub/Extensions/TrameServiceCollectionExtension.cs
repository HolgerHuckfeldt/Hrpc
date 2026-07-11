using TrameCommon;
using TrameCommon.MessagePack;
using TrameCore.Attributes;
using TrameCore.Services;
using MessagePack;
using Microsoft.AspNetCore.RateLimiting;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.RateLimiting;

namespace TrameHub.Extensions
{
    public static class TrameServiceCollectionExtension
    {
        /// <summary>
        /// Registriert Trame mit Konfigurations-Callback — das ist der empfohlene
        /// Einstiegspunkt für eine v1.0-Server-Setup. WebSocket ist der primäre
        /// (Default-)Kanal; SignalR ist über <see cref="TrameOptions.UseSignalR"/>
        /// optional zuschaltbar. Die Pipeline muss anschließend
        /// <c>app.UseWebSockets(); app.UseTrameWebSocket(); app.UseTrame();</c>
        /// (und bei Bedarf <c>app.MapTrameEndpoints()</c>) aufrufen.
        ///
        /// Usage:
        /// <code>
        /// builder.Services.AddTrame();                       // Defaults (WS primär)
        /// // oder
        /// builder.Services.AddTrame(o =&gt;
        /// {
        ///     o.EnableDetailedErrors = builder.Environment.IsDevelopment();
        ///     o.RateLimitPermitLimit = 50;                  // nur in Prod
        ///     o.UseSignalR = true;                          // optionaler 2. Kanal
        /// });
        /// </code>
        /// </summary>
        public static IServiceCollection AddTrame(this IServiceCollection services,
            Action<TrameOptions>? configure = null)
        {
            var options = new TrameOptions();
            configure?.Invoke(options);
            return services.AddTrame(options);
        }

        public static IServiceCollection AddTrame(this IServiceCollection services,
            TrameOptions options)
        {
            // Options als Singleton in DI ablegen, damit die Pipeline-Extensions
            // (UseTrameTransports/MapTrame) UseSignalR etc. ohne Parameter lesen können.
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
                    // Custom Resolver: JsonElement (TrameResponse.Data seit dem Single-Pass-
                    // Fix) wird als native MessagePack-Tokens serialisiert, nicht als
                    // escapeter JSON-String — keine Double-Wrapping-Tax auf dem SignalR-Kanal.
                    fastHub.AddMessagePackProtocol(o =>
                        o.SerializerOptions = MessagePackSerializerOptions.Standard
                            .WithResolver(JsonElementResolver.Instance));
                }
            }

            // Minimal-API (REST) JSON-Options host-weit konfigurieren: camelCase +
            // relaxed Encoder. Wirkt auf ALLE Minimal-API-Endpoints des Hosts (Trame ist
            // das Framework, das den Host bereitstellt) — Data (JsonElement) wird damit
            // roh in einem Pass serialisiert, ohne `"`-Escape-Tax.
            // TrameResponseJsonConverter (Write-only): DataBytes via WriteRawValue roh in
            // den Wire → kein JsonDocument-Baum auf dem Server (Single-Pass-Optimierung).
            services.ConfigureHttpJsonOptions(o =>
            {
                o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                o.SerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
                o.SerializerOptions.Converters.Add(new TrameResponseJsonConverter());
            });

            // Rate Limiting – always register to ensure UseRateLimiter works.
            services.AddRateLimiter(rateLimiterOptions =>
            {
                if (options.RateLimitPermitLimit > 0)
                {
                    rateLimiterOptions.AddFixedWindowLimiter("trame", opt =>
                    {
                        opt.PermitLimit = options.RateLimitPermitLimit;
                        opt.Window = TimeSpan.FromSeconds(options.RateLimitWindowSeconds);
                        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                        opt.QueueLimit = 0;
                    });
                    rateLimiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                }
            });

            // Add the TrameService
            // Singleton for performance-reasons.
            // The TrameService will create a scope for the controllers.
            services.AddSingleton<ITrameCore>(sp =>
            {
                var invoker = new TrameCore.Services.TrameInvoker(
                    sp.GetRequiredService<IServiceScopeFactory>(),
                    sp.GetService<Microsoft.Extensions.Logging.ILogger<TrameCore.Services.TrameInvoker>>()
                        ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<TrameCore.Services.TrameInvoker>.Instance,
                    sp.GetService<IEnumerable<TrameCore.Services.ITrameInterceptor>>());

                // Detailed Errors aktivieren, wenn explizit gewünscht oder in Development.
                var env = sp.GetService<Microsoft.Extensions.Hosting.IHostEnvironment>();
                invoker.EnableDetailedErrors = options.EnableDetailedErrors || (env?.IsDevelopment() ?? false);
                // Kardinalitäts-Caps durchreichen (Default 1000/10000, 0 = unbegrenzt).
                invoker.MaxParameterArrayLength = options.MaxParameterArrayLength;
                invoker.MaxResultElementCount = options.MaxResultElementCount;
                return invoker;
            });

            // Register built-in interceptors
            services.AddSingleton<TrameCore.Services.ITrameInterceptor, TrameCore.Services.TrameLoggingInterceptor>();

            // Register Fast-Controller as Scoped.
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (Type type in assembly.GetTypes())
                {
                    if (type.GetCustomAttributes(typeof(TrameControllerAttribute), true).Length > 0)
                    {
                        services.AddScoped(type);
                    }
                }
            }

            return services;
        }

        public static IApplicationBuilder UseTrame(
            this IApplicationBuilder app)
        {
            app.ApplicationServices.CreateScope();

            var trameService = app.ApplicationServices.GetService<TrameCore.Services.ITrameCore>();
            if (trameService == null)
            {
                return app;
            }

            // If a TrameControllerBuilder was registered (fluent API), use it
            var builder = app.ApplicationServices.GetService<TrameControllerBuilder>();
            if (builder != null)
            {
                builder.Apply(app.ApplicationServices, trameService);
                return app;
            }

            // Fallback: register all auto-discovered [TrameController] types
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (Type type in assembly.GetTypes())
                {
                    if (type.GetCustomAttributes(typeof(TrameControllerAttribute), true).Length > 0)
                    {
                        trameService.Register(type);
                    }
                }
            }

            return app;
        }
    }
}
