using WaevaRpcCore.Attributes;
using WaevaRpcCore.Services;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using System.Threading.RateLimiting;

namespace WaevaRpcHub.Extensions;

/// <summary>
/// Builder for registering WaevaRpc controllers with minimal boilerplate.
/// Supports convention-based, explicit, and lambda-based registration.
/// </summary>
public class WaevaRpcControllerBuilder
{
    private readonly IServiceCollection _services;
    private readonly List<Action<IWaevaRpcCore>> _registrations = new();
    private readonly List<(Type type, ServiceLifetime lifetime)> _serviceRegistrations = new();

    internal WaevaRpcControllerBuilder(IServiceCollection services)
    {
        _services = services;
    }

    /// <summary>
    /// Registers all types with [WaevaRpcController] from the specified assemblies.
    /// </summary>
    public WaevaRpcControllerBuilder FromAssemblies(params Assembly[] assemblies)
    {
        foreach (var assembly in assemblies.Length > 0 ? assemblies : AppDomain.CurrentDomain.GetAssemblies())
        {
            foreach (var type in assembly.GetTypes())
            {
                if (type.GetCustomAttributes(typeof(WaevaRpcControllerAttribute), true).Length > 0)
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
    /// The type must have [WaevaRpcController] attribute.
    /// </summary>
    public WaevaRpcControllerBuilder Add<T>() where T : class
    {
        _serviceRegistrations.Add((typeof(T), ServiceLifetime.Scoped));
        _registrations.Add(core => core.Register<T>());
        return this;
    }

    /// <summary>
    /// Registers a controller type with a specific service lifetime.
    /// </summary>
    public WaevaRpcControllerBuilder Add<T>(ServiceLifetime lifetime) where T : class
    {
        _serviceRegistrations.Add((typeof(T), lifetime));
        _registrations.Add(core => core.Register<T>());
        return this;
    }

    /// <summary>
    /// Registers a controller as a singleton (e.g. stateless service).
    /// </summary>
    public WaevaRpcControllerBuilder AddSingleton<T>() where T : class
    {
        _serviceRegistrations.Add((typeof(T), ServiceLifetime.Singleton));
        _registrations.Add(core => core.Register<T>());
        return this;
    }

    /// <summary>
    /// Registers a controller with a factory method.
    /// The type must have [WaevaRpcController] attribute.
    /// </summary>
    public WaevaRpcControllerBuilder Add<T>(Func<IServiceProvider, T> factory) where T : class
    {
        _services.AddScoped(factory);
        _registrations.Add(core => core.Register<T>());
        return this;
    }

    internal void Apply(IServiceProvider serviceProvider, IWaevaRpcCore core)
    {
        // Service registrations were already added to IServiceCollection in AddWaevaRpc()
        // Just register controllers with the core
        foreach (var registration in _registrations)
        {
            registration(core);
        }
    }
}

/// <summary>
/// Extension methods for registering WaevaRpc controllers with a fluent builder API.
/// </summary>
public static class WaevaRpcRegistrationExtensions
{
    /// <summary>
    /// Registers WaevaRpc controllers using a fluent builder.
    /// Reduces boilerplate compared to [WaevaRpcController] attribute scanning.
    ///
    /// Usage:
    /// <code>
    /// builder.Services.AddWaevaRpc(options, controllers => controllers
    ///     .Add&lt;CustomerHandler&gt;()
    ///     .AddSingleton&lt;ConfigHandler&gt;()
    ///     .FromAssemblies(typeof(Program).Assembly));
    /// </code>
    /// </summary>
    public static IServiceCollection AddWaevaRpc(
        this IServiceCollection services,
        WaevaRpcOptions options,
        Action<WaevaRpcControllerBuilder> configureControllers)
    {
        // Call the original AddWaevaRpc (without auto-scan)
        AddWaevaRpcCore(services, options);

        var builder = new WaevaRpcControllerBuilder(services);
        configureControllers(builder);

        // We'll apply registrations in UseWaevaRpc (when the core is available)
        services.AddSingleton(builder);

        return services;
    }

    private static void AddWaevaRpcCore(IServiceCollection services, WaevaRpcOptions options)
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
                rateLimiterOptions.AddFixedWindowLimiter("waeva", opt =>
                {
                    opt.PermitLimit = options.RateLimitPermitLimit;
                    opt.Window = TimeSpan.FromSeconds(options.RateLimitWindowSeconds);
                    opt.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
                    opt.QueueLimit = 0;
                });
                rateLimiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            }
        });

        services.AddSingleton<IWaevaRpcCore, WaevaRpcCore.Services.WaevaRpcInvoker>();
        services.AddSingleton<WaevaRpcCore.Services.IWaevaRpcInterceptor, WaevaRpcCore.Services.WaevaRpcLoggingInterceptor>();
    }

    /// <summary>
    /// Registers WaevaRpc with auto-discovery from the calling assembly only (faster than scanning all assemblies).
    /// </summary>
    public static IServiceCollection AddWaevaRpcFromCurrentAssembly(
        this IServiceCollection services,
        WaevaRpcOptions options)
    {
        var assembly = Assembly.GetCallingAssembly();
        return services.AddWaevaRpc(options, controllers => controllers.FromAssemblies(assembly));
    }
}