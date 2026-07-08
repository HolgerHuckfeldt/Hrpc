using HrpcCore.Attributes;
using HrpcCore.Services;
using MessagePack;
using Microsoft.AspNetCore.RateLimiting;
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
                    fastHub.AddMessagePackProtocol();
            }

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
            services.AddSingleton<IHrpcCore, HrpcCore.Services.HrpcInvoker>();

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
