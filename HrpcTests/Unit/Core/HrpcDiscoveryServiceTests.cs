using FluentAssertions;
using HrpcCommon.Models;
using HrpcCore.Services;
using HrpcTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using Xunit;

namespace HrpcTests.Unit.Core;

/// <summary>
/// Unit tests for HrpcDiscoveryService metadata generation.
/// </summary>
public class HrpcDiscoveryServiceTests
{
    private readonly HrpcInvoker _invoker;

    public HrpcDiscoveryServiceTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTransient<TestInvokerController>();
        var sp = services.BuildServiceProvider();
        _invoker = new HrpcInvoker(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<ILogger<HrpcInvoker>>());
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
        controller.Methods.Should().HaveCount(16); // All methods decorated with [HrpcMethod]
        controller.Methods.Select(m => m.MethodName).Should().Contain(new[]
        {
            "Echo", "Add", "EchoAsync", "AddAsync", "VoidMethod",
            "WithCancellation", "ComplexReturn", "NoParams", "Secured", "SecuredWithRole",
            "StreamNumbers", "StreamNumbersTask", "UploadBlob", "DownloadBlob", "UploadAndProcess", "DownloadStream"
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
}
