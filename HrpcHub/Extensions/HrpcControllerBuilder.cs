using HrpcCore.Attributes;
using HrpcCore.Services;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using System.Threading.RateLimiting;

namespace HrpcHub.Extensions;

/// <summary>
/// Builder for registering Hrpc controllers with minimal boilerplate.
/// Supports convention-based, explicit, and lambda-based registration.
/// </summary>
public class HrpcControllerBuilder
{
    private readonly IServiceCollection _services;
    private readonly List<Action<IHrpcCore>> _registrations = new();
    private readonly List<(Type type, ServiceLifetime lifetime)> _serviceRegistrations = new();

    internal HrpcControllerBuilder(IServiceCollection services)
    {
        _services = services;
    }

    /// <summary>
    /// Registers all types with [HrpcController] from the specified assemblies.
    /// </summary>
    public HrpcControllerBuilder FromAssemblies(params Assembly[] assemblies)
    {
        foreach (var assembly in assemblies.Length > 0 ? assemblies : AppDomain.CurrentDomain.GetAssemblies())
        {
            foreach (var type in assembly.GetTypes())
            {
                if (type.GetCustomAttributes(typeof(HrpcControllerAttribute), true).Length > 0)
                {
                    _serviceRegistrations.Add((type, ServiceLifetime.Scoped));
                    _registrations.Add(core => core.Register(type));
                }
            }
        }
        return this;
    }

    /// <summary>
    /// Explicitly registers a controller type.
    /// The type must have [HrpcController] attribute.
    /// </summary>
    public HrpcControllerBuilder Add<T>() where T : class
    {
        _serviceRegistrations.Add((typeof(T), ServiceLifetime.Scoped));
        _registrations.Add(core => core.Register<T>());
        return this;
    }

    /// <summary>
    /// Registers a controller type with a specific service lifetime.
    /// </summary>
    public HrpcControllerBuilder Add<T>(ServiceLifetime lifetime) where T : class
    {
        _serviceRegistrations.Add((typeof(T), lifetime));
        _registrations.Add(core => core.Register<T>());
        return this;
    }

    /// <summary>
    /// Registers a controller as a singleton (e.g. stateless service).
    /// </summary>
    public HrpcControllerBuilder AddSingleton<T>() where T : class
    {
        _serviceRegistrations.Add((typeof(T), ServiceLifetime.Singleton));
        _registrations.Add(core => core.Register<T>());
        return this;
    }

    /// <summary>
    /// Registers a controller with a factory method.
    /// The type must have [HrpcController] attribute.
    /// </summary>
    public HrpcControllerBuilder Add<T>(Func<IServiceProvider, T> factory) where T : class
    {
        _services.AddScoped(factory);
        _registrations.Add(core => core.Register<T>());
        return this;
    }

    internal void Apply(IServiceProvider serviceProvider, IHrpcCore core)
    {
        // Service registrations were already added to IServiceCollection in AddHrpc()
        // Just register controllers with the core
        foreach (var registration in _registrations)
        {
            registration(core);
        }
    }
}

/// <summary>
/// Extension methods for registering Hrpc controllers with a fluent builder API.
/// </summary>
public static class HrpcRegistrationExtensions
{
    /// <summary>
    /// Registers Hrpc controllers using a fluent builder.
    /// Reduces boilerplate compared to [HrpcController] attribute scanning.
    ///
    /// Usage:
    /// <code>
    /// builder.Services.AddHrpc(options, controllers => controllers
    ///     .Add&lt;CustomerHandler&gt;()
    ///     .AddSingleton&lt;ConfigHandler&gt;()
    ///     .FromAssemblies(typeof(Program).Assembly));
    /// </code>
    /// </summary>
    public static IServiceCollection AddHrpc(
        this IServiceCollection services,
        HrpcOptions options,
        Action<HrpcControllerBuilder> configureControllers)
    {
        // Call the original AddHrpc (without auto-scan)
        AddHrpcCore(services, options);

        var builder = new HrpcControllerBuilder(services);
        configureControllers(builder);

        // We'll apply registrations in UseHrpc (when the core is available)
        services.AddSingleton(builder);

        return services;
    }

    private static void AddHrpcCore(IServiceCollection services, HrpcOptions options)
    {
        if (options.UseSignalR)
        {
            var fastHub = services.AddSignalR(o =>
            {
                o.EnableDetailedErrors = options.EnableDetailedErrors;
                o.MaximumReceiveMessageSize = options.MaximumReceiveMessageSize;
                o.StreamBufferCapacity = options.StreamBufferCapacity;
                o.MaximumParallelInvocationsPerClient = options.MaximumParallelInvocationsPerClient;
            });

            if (options.UseMessagePack)
                fastHub.AddMessagePackProtocol();
        }

        services.AddRateLimiter(rateLimiterOptions =>
        {
            if (options.RateLimitPermitLimit > 0)
            {
                rateLimiterOptions.AddFixedWindowLimiter("hrpc", opt =>
                {
                    opt.PermitLimit = options.RateLimitPermitLimit;
                    opt.Window = TimeSpan.FromSeconds(options.RateLimitWindowSeconds);
                    opt.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
                    opt.QueueLimit = 0;
                });
                rateLimiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            }
        });

        services.AddSingleton<IHrpcCore, HrpcCore.Services.HrpcInvoker>();
        services.AddSingleton<HrpcCore.Services.IHrpcInterceptor, HrpcCore.Services.HrpcLoggingInterceptor>();
    }

    /// <summary>
    /// Registers Hrpc with auto-discovery from the calling assembly only (faster than scanning all assemblies).
    /// </summary>
    public static IServiceCollection AddHrpcFromCurrentAssembly(
        this IServiceCollection services,
        HrpcOptions options)
    {
        var assembly = Assembly.GetCallingAssembly();
        return services.AddHrpc(options, controllers => controllers.FromAssemblies(assembly));
    }
}