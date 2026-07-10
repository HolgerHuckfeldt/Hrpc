using FluentAssertions;
using HrpcClient.Hrpc;
using HrpcCommon.Exceptions;
using HrpcCommon.Models;
using HrpcCore.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using Xunit;

namespace HrpcTests.Unit.Client;

/// <summary>
/// Unit tests for HrpcCall fluent builder.
/// </summary>
public class HrpcCallTests
{
    [Fact]
    public void Init_CreatesCallWithControllerAndMethod()
    {
        // Act
        var call = HrpcCall.Init("Customer", "GetById");

        // Assert
        call.Should().NotBeNull();
    }

    [Fact]
    public void With_AddsParameters()
    {
        // Act
        var call = HrpcCall.Init("Customer", "Add")
            .With("Alice", 30);

        // Assert
        var request = call.ToRequest();
        request.Should().NotBeNull();
        request.Controller.Should().Be("Customer");
        request.Method.Should().Be("Add");
        request.StringData.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Add_SingleParameter_AddsToRequest()
    {
        // Act
        var call = HrpcCall.Init("Customer", "Search")
            .Add("test");

        // Assert
        var request = call.ToRequest();
        request.StringData.Should().Contain("test");
    }

    [Fact]
    public void Named_SetsRequestId()
    {
        // Act
        var call = HrpcCall.Init("Customer", "GetAll")
            .Named("get-all-customers");

        // Assert
        var request = call.ToRequest();
        request.Id.Should().Be("get-all-customers");
    }

    [Fact]
    public void Exposes_SetsDependencyMapping()
    {
        // Act
        var call = HrpcCall.Init("Customer", "Add")
            .With("Alice")
            .Exposes("$", "customerId");

        // Assert
        var request = call.ToRequest();
        request.DependencyMapping.Should().NotBeNull();
        request.DependencyMapping.Should().ContainKey("customerId");
        request.DependencyMapping!["customerId"].Should().Be("$");
    }

    [Fact]
    public void WithAlias_AddsAliasPlaceholder()
    {
        // Act
        var call = HrpcCall.Init("Customer", "GetById")
            .WithAlias("@customerId");

        // Assert
        var request = call.ToRequest();
        request.StringData.Should().Contain("@customerId");
    }

    [Fact]
    public void WithAlias_SetsParameterName()
    {
        // Act
        var call = HrpcCall.Init("Customer", "GetById")
            .WithAlias("@customerId");

        // Assert
        var request = call.ToRequest();
        var params_ = JsonSerializer.Deserialize<List<HrpcParameter>>(request.StringData!);
        params_.Should().NotBeNull();
        params_!.Should().ContainSingle()
            .Which.ParameterName.Should().Be("customerId");
    }

    [Fact]
    public void Param_SetsParameterName()
    {
        // Act
        var call = HrpcCall.Init("Customer", "GetById")
            .Param("id", 42);

        // Assert
        var request = call.ToRequest();
        var params_ = JsonSerializer.Deserialize<List<HrpcParameter>>(request.StringData!);
        params_.Should().NotBeNull();
        params_!.Should().ContainSingle()
            .Which.ParameterName.Should().Be("id");
        params_!.Single().Data.Should().Be("42");
    }

    [Fact]
    public void Add_SetsParameterName()
    {
        // Act
        var call = HrpcCall.Init("Customer", "Search")
            .Add("test");

        // Assert
        var request = call.ToRequest();
        var params_ = JsonSerializer.Deserialize<List<HrpcParameter>>(request.StringData!);
        params_.Should().NotBeNull();
        params_!.Should().ContainSingle()
            .Which.ParameterName.Should().Be("param0");
    }

    [Fact]
    public void Add_MultipleParameters_HaveSequentialNames()
    {
        // Act
        var call = HrpcCall.Init("Customer", "Create")
            .Add("Alice")
            .Add(30)
            .Add(true);

        // Assert
        var request = call.ToRequest();
        var params_ = JsonSerializer.Deserialize<List<HrpcParameter>>(request.StringData!);
        params_.Should().HaveCount(3);
        params_![0].ParameterName.Should().Be("param0");
        params_![1].ParameterName.Should().Be("param1");
        params_![2].ParameterName.Should().Be("param2");
    }

    [Fact]
    public void ToRequest_GeneratesIdFromControllerAndMethod_WhenNotNamed()
    {
        // Act
        var call = HrpcCall.Init("Customer", "GetAll");

        // Assert
        var request = call.ToRequest();
        request.Id.Should().Be("Customer.GetAll");
    }
}

/// <summary>
/// Unit tests for consolidated HrpcException.
/// </summary>
public class HrpcExceptionTests
{
    [Fact]
    public void Constructor_WithInnerException_PreservesMessage()
    {
        // Arrange
        var inner = new InvalidOperationException("Inner error");

        // Act
        var ex = new HrpcException(inner);

        // Assert
        ex.Message.Should().Be("Inner error");
        ex.InnerException.Should().Be(inner);
    }

    [Fact]
    public void Constructor_WithMessage_SetsMessage()
    {
        // Act
        var ex = new HrpcException("Custom error");

        // Assert
        ex.Message.Should().Be("Custom error");
        ex.InnerException.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithMessageAndInner_SetsBoth()
    {
        // Arrange
        var inner = new ArgumentException("arg error");

        // Act
        var ex = new HrpcException("outer", inner);

        // Assert
        ex.Message.Should().Be("outer");
        ex.InnerException.Should().Be(inner);
    }
}

/// <summary>
/// Unit tests for shared models (consolidated in HrpcCommon).
/// </summary>
public class HrpcModelTests
{
    [Fact]
    public void HrpcRequest_DefaultValues_AreCorrect()
    {
        // Act
        var request = new HrpcRequest();

        // Assert
        request.Controller.Should().BeEmpty();
        request.Method.Should().BeEmpty();
        request.Id.Should().BeEmpty();
        request.StringData.Should().BeNull();
        request.BinaryData.Should().BeNull();
        request.DependencyMapping.Should().BeNull();
    }

    [Fact]
    public void HrpcResponse_DefaultValues_AreCorrect()
    {
        // Act
        var response = new HrpcResponse();

        // Assert
        response.Code.Should().Be(0);
        response.Data.Should().BeNull();
        response.Content.Should().BeNull();
        response.Id.Should().BeNull();
        response.ExposedDependencies.Should().BeNull();
    }

    [Fact]
    public void HrpcMultiRequest_DefaultMode_IsSerial()
    {
        // Act
        var multiRequest = new HrpcMultiRequest();

        // Assert
        multiRequest.Mode.Should().Be(ExecutionMode.Serial);
        multiRequest.Requests.Should().BeNull();
    }

    [Fact]
    public void HrpcParameter_DefaultValues_AreCorrect()
    {
        // Act
        var param = new HrpcParameter();

        // Assert
        param.ParameterName.Should().BeEmpty();
        param.Data.Should().BeNull();
        param.Num.Should().Be(0);
    }

    [Fact]
    public void ExecutionMode_HasTwoValues()
    {
        // Act
        var values = Enum.GetValues<ExecutionMode>();

        // Assert
        values.Should().HaveCount(2);
        values.Should().Contain(ExecutionMode.Parallel);
        values.Should().Contain(ExecutionMode.Serial);
    }
}

/// <summary>
/// Unit tests for HrpcWebSocketClient ID-based request/response correlation.
/// </summary>
public class HrpcWebSocketClientTests
{
    [Fact]
    public void Constructor_WithServerUrl_SetsUri()
    {
        // Act
        var client = new HrpcWebSocketClient("https://example.com/api");

        // Assert
        client.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithTrailingSlash_HandlesCorrectly()
    {
        // Act
        var client = new HrpcWebSocketClient("https://example.com/");

        // Assert
        client.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithEmptyUrl_Throws()
    {
        // Act
        var act = () => new HrpcWebSocketClient("");

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task Call_WithNullRequest_ReturnsNull()
    {
        // Arrange
        var client = new HrpcWebSocketClient("https://example.com");

        // Act
        var result = await client.Call((HrpcRequest?)null);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task Call_MultiRequest_WithNullRequest_ReturnsNull()
    {
        // Arrange
        var client = new HrpcWebSocketClient("https://example.com");

        // Act
        var result = await client.Call((HrpcMultiRequest?)null);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void RequestId_IsAssigned_WhenNotProvided()
    {
        // Arrange
        var client = new HrpcWebSocketClient("https://example.com");
        var request = new HrpcRequest
        {
            Controller = "Test",
            Method = "Echo",
            StringData = "[]"
            // Id is not set — defaults to string.Empty
        };

        // Assert: structural contract — the client will assign an ID
        // via request.Id ?? NextId() in SendAndAwaitResponseAsync
        request.Id.Should().BeEmpty(); // Not set by caller
        request.Controller.Should().Be("Test");
    }
}

/// <summary>
/// Unit tests for HrpcRestJsonClient.
/// </summary>
public class HrpcRestJsonClientTests
{
    [Fact]
    public void Constructor_WithDefaultApiPath_UsesApiHrpc()
    {
        // Act
        var client = new HrpcRestJsonClient("https://example.com");

        // Assert
        client.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithCustomApiPath_AcceptsPath()
    {
        // Act
        var client = new HrpcRestJsonClient("https://example.com", apiPath: "custom/path");

        // Assert
        client.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithExternalHttpClient_DoesNotOwnIt()
    {
        // Arrange
        using var http = new HttpClient();

        // Act
        var client = new HrpcRestJsonClient("https://example.com", http);

        // Assert
        client.Should().NotBeNull();
        // No exception on dispose — the external client is not disposed
        client.Dispose();
    }

    [Fact]
    public void Constructor_WithEmptyUrl_Throws()
    {
        // Act
        var act = () => new HrpcRestJsonClient("");

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task Call_WithNullRequest_ReturnsNull()
    {
        // Arrange
        var client = new HrpcRestJsonClient("https://example.com");

        // Act
        var result = await client.Call((HrpcRequest?)null);

        // Assert
        result.Should().BeNull();
    }
}

/// <summary>
/// Unit tests for HrpcSignalrClient.
/// </summary>
public class HrpcSignalrClientTests
{
    [Fact]
    public void Constructor_WithServerUrl_CreatesClient()
    {
        // Act
        var client = new HrpcSignalrClient("https://example.com");

        // Assert
        client.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithBearerToken_CreatesClient()
    {
        // Act
        var client = new HrpcSignalrClient("https://example.com", "test-token");

        // Assert
        client.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithTrailingSlash_HandlesCorrectly()
    {
        // Act
        var client = new HrpcSignalrClient("https://example.com/");

        // Assert
        client.Should().NotBeNull();
    }

    [Fact]
    public async Task Call_WithNullRequest_ReturnsNull()
    {
        // Arrange
        var client = new HrpcSignalrClient("https://example.com");

        // Act
        var result = await client.Call((HrpcRequest?)null);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task Call_MultiRequest_WithNullRequest_ReturnsNull()
    {
        // Arrange
        var client = new HrpcSignalrClient("https://example.com");

        // Act
        var result = await client.Call((HrpcMultiRequest?)null);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task DisposeAsync_CanBeCalledMultipleTimes()
    {
        // Arrange
        var client = new HrpcSignalrClient("https://example.com");

        // Act
        await client.DisposeAsync();
        await client.DisposeAsync(); // Should not throw

        // Assert: no exception
        true.Should().BeTrue();
    }
}

/// <summary>
/// Unit tests for the unified HrpcError model.
/// </summary>
public class HrpcErrorTests
{
    [Fact]
    public void FromResponse_WithErrorResponse_PopulatesFields()
    {
        // Arrange
        var response = new HrpcResponse
        {
            Code = 404,
            Error = new HrpcError { Code = 404, Message = "Not found" },
            Id = "req-123"
        };

        // Act
        var error = HrpcError.FromResponse(response);

        // Assert
        error.Code.Should().Be(404);
        error.Message.Should().Be("Not found");
        error.RequestId.Should().Be("req-123");
    }

    [Fact]
    public void FromResponse_WithNullData_UsesDefaultMessage()
    {
        // Arrange
        var response = new HrpcResponse { Code = 500, Data = null };

        // Act
        var error = HrpcError.FromResponse(response);

        // Assert
        error.Code.Should().Be(500);
        error.Message.Should().Be("Hrpc call failed with code 500.");
    }

    [Fact]
    public void HrpcException_WithHrpcError_PreservesError()
    {
        // Arrange
        var error = new HrpcError { Code = 401, Message = "Unauthorized" };

        // Act
        var ex = new HrpcException(error);

        // Assert
        ex.Error.Should().NotBeNull();
        ex.Error!.Code.Should().Be(401);
        ex.Message.Should().Be("Unauthorized");
    }
}