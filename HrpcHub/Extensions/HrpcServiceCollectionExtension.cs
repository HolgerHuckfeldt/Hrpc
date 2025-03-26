using HrpcCore.Attributes;
using HrpcCore.Services;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;

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
                // Maybe POST or Grps in future.
                var fastHub = services.AddSignalR(
                    o =>
                    {
                        o.EnableDetailedErrors = options.EnableDetailedErrors;
                        o.MaximumReceiveMessageSize = options.MaximumReceiveMessageSize;
                        o.StreamBufferCapacity = options.StreamBufferCapacity;
                        o.MaximumParallelInvocationsPerClient = options.MaximumParallelInvocationsPerClient;
                    });

                if (options.UseMessagePack)
                    fastHub.AddMessagePackProtocol(
                    //protocolOptions => protocolOptions.SerializerOptions = MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray)
                    );
            }


            // Add the HrpcService
            // Singleton for performance-reasons.
            // The HrpcService will create a scope for the controllers.
            services.AddSingleton<IHrpcCore, HrpcCore.Services.HrpcInvoker>();
            //services.AddSingleton<HrpcCore.Services.HrpcDiscoveryService>();

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
