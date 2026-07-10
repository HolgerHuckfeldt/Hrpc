using HrpcCommon;
using HrpcCommon.MessagePack;
using HrpcCore.Attributes;
using HrpcCore.Services;
using MessagePack;
using Microsoft.AspNetCore.RateLimiting;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.RateLimiting;

namespace HrpcHub.Extensions
{
    public static class HrpcServiceCollectionExtension
    {
        public static IServiceCollection AddHrpc(this IServiceCollection services,
            HrpcOptions options)
        {

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
                    // Custom Resolver: JsonElement (HrpcResponse.Data seit dem Single-Pass-
                    // Fix) wird als native MessagePack-Tokens serialisiert, nicht als
                    // escapeter JSON-String — keine Double-Wrapping-Tax auf dem SignalR-Kanal.
                    fastHub.AddMessagePackProtocol(o =>
                        o.SerializerOptions = MessagePackSerializerOptions.Standard
                            .WithResolver(JsonElementResolver.Instance));
                }
            }

            // Minimal-API (REST) JSON-Options host-weit konfigurieren: camelCase +
            // relaxed Encoder. Wirkt auf ALLE Minimal-API-Endpoints des Hosts (Hrpc ist
            // das Framework, das den Host bereitstellt) — Data (JsonElement) wird damit
            // roh in einem Pass serialisiert, ohne `"`-Escape-Tax.
            // HrpcResponseJsonConverter (Write-only): DataBytes via WriteRawValue roh in
            // den Wire → kein JsonDocument-Baum auf dem Server (Single-Pass-Optimierung).
            services.ConfigureHttpJsonOptions(o =>
            {
                o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                o.SerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
                o.SerializerOptions.Converters.Add(new HrpcResponseJsonConverter());
            });

            // Rate Limiting – always register to ensure UseRateLimiter works.
            services.AddRateLimiter(rateLimiterOptions =>
            {
                if (options.RateLimitPermitLimit > 0)
                {
                    rateLimiterOptions.AddFixedWindowLimiter("hrpc", opt =>
                    {
                        opt.PermitLimit = options.RateLimitPermitLimit;
                        opt.Window = TimeSpan.FromSeconds(options.RateLimitWindowSeconds);
                        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                        opt.QueueLimit = 0;
                    });
                    rateLimiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                }
            });

            // Add the HrpcService
            // Singleton for performance-reasons.
            // The HrpcService will create a scope for the controllers.
            services.AddSingleton<IHrpcCore>(sp =>
            {
                var invoker = new HrpcCore.Services.HrpcInvoker(
                    sp.GetRequiredService<IServiceScopeFactory>(),
                    sp.GetService<Microsoft.Extensions.Logging.ILogger<HrpcCore.Services.HrpcInvoker>>()
                        ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<HrpcCore.Services.HrpcInvoker>.Instance,
                    sp.GetService<IEnumerable<HrpcCore.Services.IHrpcInterceptor>>());

                // Detailed Errors aktivieren, wenn explizit gewünscht oder in Development.
                var env = sp.GetService<Microsoft.Extensions.Hosting.IHostEnvironment>();
                invoker.EnableDetailedErrors = options.EnableDetailedErrors || (env?.IsDevelopment() ?? false);
                return invoker;
            });

            // Register built-in interceptors
            services.AddSingleton<HrpcCore.Services.IHrpcInterceptor, HrpcCore.Services.HrpcLoggingInterceptor>();

            // Register Fast-Controller as Scoped.
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (Type type in assembly.GetTypes())
                {
                    if (type.GetCustomAttributes(typeof(HrpcControllerAttribute), true).Length > 0)
                    {
                        services.AddScoped(type);
                    }
                }
            }

            return services;
        }

        public static IApplicationBuilder UseHrpc(
            this IApplicationBuilder app)
        {
            app.ApplicationServices.CreateScope();

            var hrpcService = app.ApplicationServices.GetService<HrpcCore.Services.IHrpcCore>();
            if (hrpcService == null)
            {
                return app;
            }

            // If a HrpcControllerBuilder was registered (fluent API), use it
            var builder = app.ApplicationServices.GetService<HrpcControllerBuilder>();
            if (builder != null)
            {
                builder.Apply(app.ApplicationServices, hrpcService);
                return app;
            }

            // Fallback: register all auto-discovered [HrpcController] types
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (Type type in assembly.GetTypes())
                {
                    if (type.GetCustomAttributes(typeof(HrpcControllerAttribute), true).Length > 0)
                    {
                        hrpcService.Register(type);
                    }
                }
            }

            return app;
        }
    }
}
