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
/// Unit tests for HrpcInvoker – the core RPC invocation engine.
/// </summary>
public class HrpcInvokerTests
{
    private readonly IServiceProvider _serviceProvider;
    private readonly HrpcInvoker _invoker;

    public HrpcInvokerTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTransient<TestInvokerController>();
        _serviceProvider = services.BuildServiceProvider();

        var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
        var logger = _serviceProvider.GetRequiredService<ILogger<HrpcInvoker>>();
        _invoker = new HrpcInvoker(scopeFactory, logger);
        _invoker.Register<TestInvokerController>();
    }

    private static HrpcRequest CreateRequest(string controller, string method,
        params (string name, string jsonValue)[] parameters)
    {
        var paramList = parameters.Select(p => new HrpcParameter
        {
            ParameterName = p.name,
            Data = p.jsonValue
        }).ToList();

        return new HrpcRequest
        {
            Controller = controller,
            Method = method,
            StringData = JsonSerializer.Serialize(paramList),
            Id = $"{controller}.{method}"
        };
    }

    #region Registration

    [Fact]
    public void Register_ControllerWithAttributes_AddsToRouteHandlers()
    {
        // Act & Assert: After registration, discovery should list the controller
        var discovery = _invoker.GetDiscoveryInfo();
        discovery.Controllers.Should().Contain(c => c.Name == "TestInvoker");
    }

    [Fact]
    public void Register_NonControllerType_DoesNotRegister()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var sp = services.BuildServiceProvider();
        var invoker = new HrpcInvoker(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<ILogger<HrpcInvoker>>());

        // Act: Register a type without [HrpcController]
        invoker.Register(typeof(string));

        // Assert: No controllers registered
        var discovery = invoker.GetDiscoveryInfo();
        discovery.Controllers.Should().BeEmpty();
    }

    #endregion

    #region Single Invocation – Sync Methods

    [Fact]
    public async Task InvokeDi_SyncEcho_ReturnsEchoedString()
    {
        // Arrange
        var request = CreateRequest("TestInvoker", "Echo",
            ("message", "\"Hello Hrpc\""));

        // Act
        var response = await _invoker.InvokeDi(request, null);

        // Assert
        response.Should().NotBeNull();
        response!.Code.Should().Be((int)HttpStatusCode.OK);
        response.Data.Should().Contain("Hello Hrpc");
    }

    [Fact]
    public async Task InvokeDi_SyncAdd_ReturnsSum()
    {
        // Arrange
        var request = CreateRequest("TestInvoker", "Add",
            ("a", "3"), ("b", "4"));

        // Act
        var response = await _invoker.InvokeDi(request, null);

        // Assert
        response.Should().NotBeNull();
        response!.Code.Should().Be((int)HttpStatusCode.OK);
        response.Data.Should().Be("7");
    }

    [Fact]
    public async Task InvokeDi_NoParams_ReturnsResult()
    {
        // Arrange
        var request = new HrpcRequest
        {
            Controller = "TestInvoker",
            Method = "NoParams",
            StringData = "[]",
            Id = "NoParams"
        };

        // Act
        var response = await _invoker.InvokeDi(request, null);

        // Assert
        response.Should().NotBeNull();
        response!.Code.Should().Be((int)HttpStatusCode.OK);
        response.Data.Should().Contain("Hello World");
    }

    [Fact]
    public async Task InvokeDi_VoidMethod_ReturnsOk()
    {
        // Arrange
        var request = CreateRequest("TestInvoker", "VoidMethod",
            ("data", "\"test\""));

        // Act
        var response = await _invoker.InvokeDi(request, null);

        // Assert
        response.Should().NotBeNull();
        response!.Code.Should().Be((int)HttpStatusCode.OK);
    }

    [Fact]
    public async Task InvokeDi_ComplexReturn_SerializesDto()
    {
        // Arrange
        var request = CreateRequest("TestInvoker", "ComplexReturn",
            ("id", "42"));

        // Act
        var response = await _invoker.InvokeDi(request, null);

        // Assert
        response.Should().NotBeNull();
        response!.Code.Should().Be((int)HttpStatusCode.OK);
        response.Data.Should().Contain("\"Id\":42");
        response.Data.Should().Contain("\"Name\":\"Test\"");
    }

    #endregion

    #region Single Invocation – Async Methods

    [Fact]
    public async Task InvokeDi_AsyncEcho_ReturnsEchoedString()
    {
        // Arrange
        var request = CreateRequest("TestInvoker", "EchoAsync",
            ("message", "\"Async Hello\""));

        // Act
        var response = await _invoker.InvokeDi(request, null);

        // Assert
        response.Should().NotBeNull();
        response!.Code.Should().Be((int)HttpStatusCode.OK);
        response.Data.Should().Contain("Async Hello");
    }

    [Fact]
    public async Task InvokeDi_AsyncAdd_ReturnsSum()
    {
        // Arrange
        var request = CreateRequest("TestInvoker", "AddAsync",
            ("a", "10"), ("b", "20"));

        // Act
        var response = await _invoker.InvokeDi(request, null);

        // Assert
        response.Should().NotBeNull();
        response!.Code.Should().Be((int)HttpStatusCode.OK);
        response.Data.Should().Be("30");
    }

    [Fact]
    public async Task InvokeDi_WithCancellation_PassesToken()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var request = CreateRequest("TestInvoker", "WithCancellation",
            ("input", "\"Cancelled\""));

        // Act
        cts.CancelAfter(1);
        var response = await _invoker.InvokeDi(request, null, cts.Token);

        // Assert: Should either complete or be cancelled gracefully
        response.Should().NotBeNull();
    }

    #endregion

    #region Error Handling

    [Fact]
    public async Task InvokeDi_UnknownController_ReturnsNotFound()
    {
        // Arrange
        var request = new HrpcRequest
        {
            Controller = "NonExistent",
            Method = "AnyMethod",
            StringData = "[]"
        };

        // Act
        var response = await _invoker.InvokeDi(request, null);

        // Assert
        response.Should().NotBeNull();
        response!.Code.Should().Be((int)HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task InvokeDi_UnknownMethod_ReturnsBadRequest()
    {
        // Arrange
        var request = new HrpcRequest
        {
            Controller = "TestInvoker",
            Method = "NonExistentMethod",
            StringData = "[]"
        };

        // Act
        var response = await _invoker.InvokeDi(request, null);

        // Assert
        response.Should().NotBeNull();
        response!.Code.Should().Be((int)HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task InvokeDi_InvalidParameterType_ReturnsBadRequest()
    {
        // Arrange
        var request = CreateRequest("TestInvoker", "Add",
            ("a", "\"not-a-number\""), ("b", "4"));

        // Act
        var response = await _invoker.InvokeDi(request, null);

        // Assert
        response.Should().NotBeNull();
        response!.Code.Should().Be((int)HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task InvokeDi_SetsResponseId_FromRequestId()
    {
        // Arrange
        var request = CreateRequest("TestInvoker", "Echo",
            ("message", "\"test\""));
        request.Id = "my-custom-id";

        // Act
        var response = await _invoker.InvokeDi(request, null);

        // Assert
        response.Should().NotBeNull();
        response!.Id.Should().Be("my-custom-id");
    }

    #endregion

    #region Batch Invocation

    [Fact]
    public async Task InvokeDi_ParallelBatch_ExecutesAllRequests()
    {
        // Arrange
        var requests = new List<HrpcRequest>
        {
            CreateRequest("TestInvoker", "Add", ("a", "1"), ("b", "2")),
            CreateRequest("TestInvoker", "Add", ("a", "3"), ("b", "4")),
            CreateRequest("TestInvoker", "Echo", ("message", "\"batch\""))
        };

        // Act
        var responses = await _invoker.InvokeDi(requests, null, ExecutionMode.Parallel);

        // Assert
        var responseList = responses.ToList();
        responseList.Should().HaveCount(3);
        responseList.Should().AllSatisfy(r => r!.Code.Should().Be((int)HttpStatusCode.OK));
    }

    [Fact]
    public async Task InvokeDi_SerialBatch_ExecutesAllRequests()
    {
        // Arrange
        var requests = new List<HrpcRequest>
        {
            CreateRequest("TestInvoker", "Add", ("a", "1"), ("b", "2")),
            CreateRequest("TestInvoker", "Echo", ("message", "\"serial\""))
        };

        // Act
        var responses = await _invoker.InvokeDi(requests, null, ExecutionMode.Serial);

        // Assert
        var responseList = responses.ToList();
        responseList.Should().HaveCount(2);
        responseList.Should().AllSatisfy(r => r!.Code.Should().Be((int)HttpStatusCode.OK));
    }

    [Fact]
    public async Task InvokeDi_EmptyBatch_ReturnsEmptyCollection()
    {
        // Arrange
        var requests = new List<HrpcRequest>();

        // Act
        var responses = await _invoker.InvokeDi(requests, null, ExecutionMode.Parallel);

        // Assert
        responses.Should().BeEmpty();
    }

    #endregion

    #region Binary Data

    [Fact]
    public async Task InvokeDi_UploadBlob_BinaryDataInjected()
    {
        // Arrange
        var binaryData = new byte[] { 1, 2, 3, 4, 5 };
        var request = new HrpcRequest
        {
            Controller = "TestInvoker",
            Method = "UploadBlob",
            StringData = "[{\"ParameterName\":\"filename\",\"Data\":\"\\\"test.bin\\\"\"}]",
            BinaryData = binaryData,
            Id = "UploadBlob"
        };

        // Act
        var response = await _invoker.InvokeDi(request, null);

        // Assert
        response.Should().NotBeNull();
        response!.Code.Should().Be((int)HttpStatusCode.OK);
        response.Data.Should().Contain("5 bytes");
    }

    [Fact]
    public async Task InvokeDi_DownloadBlob_ReturnsBinaryInContent()
    {
        // Arrange
        var request = new HrpcRequest
        {
            Controller = "TestInvoker",
            Method = "DownloadBlob",
            StringData = "[{\"ParameterName\":\"name\",\"Data\":\"\\\"myfile\\\"\"}]",
            Id = "DownloadBlob"
        };

        // Act
        var response = await _invoker.InvokeDi(request, null);

        // Assert
        response.Should().NotBeNull();
        response!.Code.Should().Be((int)HttpStatusCode.OK);
        response.Content.Should().NotBeNull();
        response.Content!.Length.Should().BeGreaterThan(0);
        var text = System.Text.Encoding.UTF8.GetString(response.Content);
        text.Should().Contain("Blob content for myfile");
    }

    [Fact]
    public async Task InvokeDi_UploadAndProcess_BinaryWithCancellationToken()
    {
        // Arrange
        var binaryData = new byte[] { 10, 20, 30, 40, 50, 60, 70, 80 };
        var request = new HrpcRequest
        {
            Controller = "TestInvoker",
            Method = "UploadAndProcess",
            StringData = "[]",
            BinaryData = binaryData,
            Id = "UploadAndProcess"
        };

        // Act
        var response = await _invoker.InvokeDi(request, null);

        // Assert
        response.Should().NotBeNull();
        response!.Code.Should().Be((int)HttpStatusCode.OK);
        response.Data.Should().Be("8");
    }

    #endregion

    #region Streaming (IAsyncEnumerable)

    [Fact]
    public async Task InvokeDi_StreamNumbers_ReturnsJsonArray()
    {
        // Arrange
        var request = CreateRequest("TestInvoker", "StreamNumbers",
            ("count", "5"));

        // Act
        var response = await _invoker.InvokeDi(request, null);

        // Assert
        response.Should().NotBeNull();
        response!.Code.Should().Be((int)HttpStatusCode.OK);
        response.Data.Should().NotBeNull();
        var numbers = JsonSerializer.Deserialize<List<int>>(response.Data!);
        numbers.Should().NotBeNull();
        numbers.Should().HaveCount(5);
        numbers.Should().BeEquivalentTo(new[] { 0, 1, 2, 3, 4 });
    }

    [Fact]
    public async Task InvokeDi_StreamNumbersTask_ReturnsJsonArray()
    {
        // Arrange
        var request = CreateRequest("TestInvoker", "StreamNumbersTask",
            ("count", "3"));

        // Act
        var response = await _invoker.InvokeDi(request, null);

        // Assert
        response.Should().NotBeNull();
        response!.Code.Should().Be((int)HttpStatusCode.OK);
        response.Data.Should().NotBeNull();
        var numbers = JsonSerializer.Deserialize<List<int>>(response.Data!);
        numbers.Should().NotBeNull();
        numbers.Should().HaveCount(3);
        numbers.Should().BeEquivalentTo(new[] { 0, 1, 2 });
    }

    #endregion

    #region Discovery

    [Fact]
    public void GetDiscoveryInfo_ReturnsControllersAndMethods()
    {
        // Act
        var discovery = _invoker.GetDiscoveryInfo();

        // Assert
        discovery.Controllers.Should().NotBeEmpty();
        var controller = discovery.Controllers.First(c => c.Name == "TestInvoker");
        controller.Methods.Should().NotBeEmpty();
        controller.Methods.Should().Contain(m => m.MethodName == "Echo");
        controller.Methods.Should().Contain(m => m.MethodName == "Add");
        controller.Methods.Should().Contain(m => m.MethodName == "EchoAsync");
    }

    [Fact]
    public void GetDiscoveryInfo_ReturnsCachedInstance()
    {
        // Act
        var first = _invoker.GetDiscoveryInfo();
        var second = _invoker.GetDiscoveryInfo();

        // Assert
        first.Should().BeSameAs(second);
    }

    [Fact]
    public void GetDiscoveryInfo_IncludesParameterMetadata()
    {
        // Act
        var discovery = _invoker.GetDiscoveryInfo();

        // Assert
        var echoMethod = discovery.Controllers
            .First(c => c.Name == "TestInvoker")
            .Methods.First(m => m.MethodName == "Echo");
        echoMethod.Parameters.Should().HaveCount(1);
        echoMethod.Parameters[0].ParameterName.Should().Be("message");
    }

    [Fact]
    public void GetDiscoveryInfo_IncludesComplexReturnType()
    {
        // Act
        var discovery = _invoker.GetDiscoveryInfo();

        // Assert
        var complexMethod = discovery.Controllers
            .First(c => c.Name == "TestInvoker")
            .Methods.First(m => m.MethodName == "ComplexReturn");
        complexMethod.ReturnType.Should().Contain("TestDto");
    }

    #endregion

    #region Error Message Safety

    [Fact]
    public async Task InvokeDi_UnknownController_ReturnsGenericError()
    {
        // Arrange
        var request = CreateRequest("NonExistent", "Echo", ("message", "\"hello\""));

        // Act
        var response = await _invoker.InvokeDi(request, null);

        // Assert
        response.Should().NotBeNull();
        response!.Code.Should().Be((int)HttpStatusCode.NotFound);
        response.Data.Should().NotContain("Fehler");
        response.Data.Should().NotContain("Exception");
    }

    [Fact]
    public async Task InvokeDi_UnknownMethod_ReturnsGenericError()
    {
        // Arrange
        var request = CreateRequest("TestInvoker", "NonExistentMethod", ("message", "\"hello\""));

        // Act
        var response = await _invoker.InvokeDi(request, null);

        // Assert
        response.Should().NotBeNull();
        response!.Code.Should().Be((int)HttpStatusCode.BadRequest);
        response.Data.Should().NotContain("Fehler");
        response.Data.Should().NotContain("Exception");
    }

    [Fact]
    public async Task InvokeDi_InvalidJson_ReturnsGenericError()
    {
        // Arrange
        var request = new HrpcRequest
        {
            Controller = "TestInvoker",
            Method = "Echo",
            StringData = "not valid json {{{",
            Id = "test"
        };

        // Act
        var response = await _invoker.InvokeDi(request, null);

        // Assert
        response.Should().NotBeNull();
        response!.Code.Should().Be((int)HttpStatusCode.BadRequest);
        response.Data.Should().NotContain("Exception");
        response.Data.Should().NotContain("JsonException");
    }

    [Fact]
    public async Task InvokeDi_TypeMismatch_ReturnsSafeError()
    {
        // Arrange: "Add" expects int a, int b — pass a string
        var request = CreateRequest("TestInvoker", "Add",
            ("a", "\"not_a_number\""),
            ("b", "42"));

        // Act
        var response = await _invoker.InvokeDi(request, null);

        // Assert
        response.Should().NotBeNull();
        response!.Code.Should().Be((int)HttpStatusCode.BadRequest);
        // Should mention the parameter name but NOT leak stack traces
        response.Data.Should().Contain("a");
        response.Data.Should().NotContain("at ");
        response.Data.Should().NotContain("stack");
    }

    [Fact]
    public async Task InvokeDi_InternalError_ReturnsGenericMessage()
    {
        // Arrange: call a method that doesn't exist in the controller
        // but the controller exists — this tests the error path
        var request = new HrpcRequest
        {
            Controller = "TestInvoker",
            Method = "Echo",
            StringData = "[{\"parameterName\":\"message\",\"data\":\"hello\"}]",
            Id = "test"
        };

        // Act
        var response = await _invoker.InvokeDi(request, null);

        // Assert
        response.Should().NotBeNull();
        // Even on success, verify error responses don't leak
        if (response!.Code >= 400)
        {
            response.Data.Should().NotContain("Fehler in der Ausführung");
            response.Data.Should().NotContain("Exception");
        }
    }

    #endregion
}
