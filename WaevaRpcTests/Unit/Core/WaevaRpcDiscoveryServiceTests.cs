using FluentAssertions;
using WaevaRpcCommon.Models;
using WaevaRpcCore.Services;
using WaevaRpcTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using Xunit;

namespace WaevaRpcTests.Unit.Core;

/// <summary>
/// Unit tests for WaevaRpcDiscoveryService metadata generation.
/// </summary>
public class WaevaRpcDiscoveryServiceTests
{
    private readonly WaevaRpcInvoker _invoker;

    public WaevaRpcDiscoveryServiceTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTransient<TestInvokerController>();
        var sp = services.BuildServiceProvider();
        _invoker = new WaevaRpcInvoker(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<ILogger<WaevaRpcInvoker>>());
        _invoker.Register<TestInvokerController>();
    }

    [Fact]
    public void GetDiscoveryInfo_ReturnsAllControllers()
    {
        // Act
        var discovery = _invoker.GetDiscoveryInfo();

        // Assert
        discovery.Controllers.Should().HaveCount(1);
        discovery.Controllers[0].Name.Should().Be("TestInvoker");
    }

    [Fact]
    public void GetDiscoveryInfo_ReturnsAllMethods()
    {
        // Act
        var discovery = _invoker.GetDiscoveryInfo();
        var controller = discovery.Controllers[0];

        // Assert
        controller.Methods.Should().HaveCount(18); // All methods decorated with [WaevaRpcMethod]
        controller.Methods.Select(m => m.MethodName).Should().Contain(new[]
        {
            "Echo", "Add", "EchoAsync", "AddAsync", "VoidMethod",
            "WithCancellation", "ComplexReturn", "NoParams", "Secured", "SecuredWithRole",
            "StreamNumbers", "StreamNumbersTask", "UploadBlob", "DownloadBlob", "UploadAndProcess", "DownloadStream",
            "GetOr404", "ValidationProblem"
        });
    }

    [Fact]
    public void GetDiscoveryInfo_ExcludesCancellationTokenFromParameters()
    {
        // Act
        var discovery = _invoker.GetDiscoveryInfo();
        var method = discovery.Controllers[0]
            .Methods.First(m => m.MethodName == "WithCancellation");

        // Assert
        method.Parameters.Should().HaveCount(1);
        method.Parameters[0].ParameterName.Should().Be("input");
    }

    [Fact]
    public void GetDiscoveryInfo_FriendlyTypeNamesForPrimitives()
    {
        // Act
        var discovery = _invoker.GetDiscoveryInfo();
        var addMethod = discovery.Controllers[0]
            .Methods.First(m => m.MethodName == "Add");

        // Assert
        addMethod.Parameters.Should().HaveCount(2);
        addMethod.Parameters[0].ParameterType.Should().Be("int");
        addMethod.Parameters[1].ParameterType.Should().Be("int");
        addMethod.ReturnType.Should().Be("int");
    }

    [Fact]
    public void GetDiscoveryInfo_StringReturnType_ForEchoMethod()
    {
        // Act
        var discovery = _invoker.GetDiscoveryInfo();
        var echoMethod = discovery.Controllers[0]
            .Methods.First(m => m.MethodName == "Echo");

        // Assert
        echoMethod.ReturnType.Should().Be("string");
    }

    [Fact]
    public void GetDiscoveryInfo_VoidReturnType_ForVoidMethod()
    {
        // Act
        var discovery = _invoker.GetDiscoveryInfo();
        var voidMethod = discovery.Controllers[0]
            .Methods.First(m => m.MethodName == "VoidMethod");

        // Assert
        voidMethod.ReturnType.Should().Be("Void");
    }

    [Fact]
    public void GetDiscoveryInfo_RegistersDataContractTypes()
    {
        // Act
        var discovery = _invoker.GetDiscoveryInfo();

        // Assert
        discovery.Types.Should().NotBeEmpty();
        discovery.Types.Should().ContainKey(typeof(TestDto).FullName!);
    }

    [Fact]
    public void GetDiscoveryInfo_TypeMeta_HasProperties()
    {
        // Act
        var discovery = _invoker.GetDiscoveryInfo();
        var typeMeta = discovery.Types[typeof(TestDto).FullName!];

        // Assert
        typeMeta.Properties.Should().HaveCount(2);
        typeMeta.Properties.Should().Contain(p => p.PropertyName == "Id");
        typeMeta.Properties.Should().Contain(p => p.PropertyName == "Name");
    }

    // --- Signatur-Inferenz (Weg C) -------------------------------------------------

    private static WaevaRpcInvoker CreateInvoker<T>() where T : class
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTransient<T>();
        var sp = services.BuildServiceProvider();
        var invoker = new WaevaRpcInvoker(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<ILogger<WaevaRpcInvoker>>());
        invoker.Register<T>();
        return invoker;
    }

    /// <summary>Regel 4: unmarkierter Typ aus der Controller-Assembly wird per Inference expandiert.</summary>
    [Fact]
    public void GetDiscoveryInfo_Inference_ExpandsUnmarkedOwnAssemblyType()
    {
        var invoker = CreateInvoker<DiscoveryInferenceController>();
        var discovery = invoker.GetDiscoveryInfo();

        // UnmarkedDto (Test-Assembly, kein Attribute) muss via Inference expandiert werden.
        discovery.Types.Should().ContainKey(typeof(UnmarkedDto).FullName!);
        var typeMeta = discovery.Types[typeof(UnmarkedDto).FullName!];
        typeMeta.Properties.Should().HaveCount(2);
        typeMeta.Properties.Should().Contain(p => p.PropertyName == "Id");
        typeMeta.Properties.Should().Contain(p => p.PropertyName == "Name");

        // …und am Methoden-Rückgabewert verlinkt sein.
        var method = discovery.Controllers[0].Methods.First(m => m.MethodName == "ReturnUnmarked");
        method.ReturnTypeDefinition.Should().NotBeNull();
    }

    /// <summary>Regel 5: Framework-Envelope aus fremder Assembly bleibt opaque (keine Property-Expansion).</summary>
    [Fact]
    public void GetDiscoveryInfo_Inference_KeepsFrameworkEnvelopeOpaque()
    {
        var invoker = CreateInvoker<DiscoveryInferenceController>();
        var discovery = invoker.GetDiscoveryInfo();

        // WaevaRpcResponse liegt in WaevaRpcCommon.dll (fremde Assembly) → nicht expandieren.
        discovery.Types.Should().NotContainKey(typeof(WaevaRpcResponse).FullName!);
        var method = discovery.Controllers[0].Methods.First(m => m.MethodName == "ReturnFrameworkType");
        method.ReturnTypeDefinition.Should().BeNull();
        method.ReturnType.Should().Be("WaevaRpcResponse");
    }

    /// <summary>Regel 2: Own-Assembly-Typ mit [WaevaRpcDataContract(Exclude = true)] bleibt force-opaque.</summary>
    [Fact]
    public void GetDiscoveryInfo_Inference_ExcludeKeepsOwnAssemblyTypeOpaque()
    {
        var invoker = CreateInvoker<DiscoveryInferenceController>();
        var discovery = invoker.GetDiscoveryInfo();

        // ExcludedDto ist in der Test-Assembly, aber Exclude-Override → nicht expandieren.
        discovery.Types.Should().NotContainKey(typeof(ExcludedDto).FullName!);
        var method = discovery.Controllers[0].Methods.First(m => m.MethodName == "TakeExcluded");
        var param = method.Parameters.First(p => p.ParameterName == "d");
        param.TypeDefinition.Should().BeNull();
    }
}
