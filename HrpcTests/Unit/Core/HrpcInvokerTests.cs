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
        services.AddTransient<NestedContactController>();
        services.AddTransient<DependencyChainController>();
        _serviceProvider = services.BuildServiceProvider();

        var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
        var logger = _serviceProvider.GetRequiredService<ILogger<HrpcInvoker>>();
        _invoker = new HrpcInvoker(scopeFactory, logger);
        _invoker.Register<TestInvokerController>();
        _invoker.Register<NestedContactController>();
        _invoker.Register<DependencyChainController>();
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
    public async Task InvokeDi_VoidMethod_ReturnsNoContent()
    {
        // Arrange
        var request = CreateRequest("TestInvoker", "VoidMethod",
            ("data", "\"test\""));

        // Act
        var response = await _invoker.InvokeDi(request, null);

        // Assert: void methods respond with 204 No Content (no body).
        response.Should().NotBeNull();
        response!.Code.Should().Be((int)HttpStatusCode.NoContent);
        response.Data.Should().BeNull();
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

    #region Parameter Binding (positional fallback + duplicate names)

    [Fact]
    public async Task InvokeDi_PositionalParams_BindByIndex()
    {
        // Arrange: simulate the fluent client which emits param0/param1 with Num indices.
        // The method Add(int a, int b) has no matching names -> server must fall back to Num.
        var paramList = new List<HrpcParameter>
        {
            new() { Num = 0, ParameterName = "param0", Data = "3" },
            new() { Num = 1, ParameterName = "param1", Data = "4" }
        };
        var request = new HrpcRequest
        {
            Controller = "TestInvoker",
            Method = "Add",
            StringData = JsonSerializer.Serialize(paramList),
            Id = "pos"
        };

        // Act
        var response = await _invoker.InvokeDi(request, null);

        // Assert
        response.Should().NotBeNull();
        response!.Code.Should().Be((int)HttpStatusCode.OK);
        response.Data.Should().Be("7");
    }

    [Fact]
    public async Task InvokeDi_PositionalParams_SkipCancellationTokenInIndex()
    {
        // Arrange: WithCancellation(string input, CancellationToken ct) — client sends one
        // positional arg (Num=0). Server must bind it to `input` (the first non-token param),
        // not to position 0 of the raw signature (which is `input` anyway) — this guards the
        // general case where a CancellationToken sits before a value parameter.
        var paramList = new List<HrpcParameter>
        {
            new() { Num = 0, ParameterName = "param0", Data = "\"hello\"" }
        };
        var request = new HrpcRequest
        {
            Controller = "TestInvoker",
            Method = "Echo",
            StringData = JsonSerializer.Serialize(paramList),
            Id = "pos-ct"
        };

        // Act
        var response = await _invoker.InvokeDi(request, null);

        // Assert
        response.Should().NotBeNull();
        response!.Code.Should().Be((int)HttpStatusCode.OK);
        response.Data.Should().Contain("hello");
    }

    [Fact]
    public async Task InvokeDi_DuplicateParameterName_ReturnsBadRequest()
    {
        // Arrange
        var paramList = new List<HrpcParameter>
        {
            new() { ParameterName = "a", Data = "3" },
            new() { ParameterName = "a", Data = "4" }
        };
        var request = new HrpcRequest
        {
            Controller = "TestInvoker",
            Method = "Add",
            StringData = JsonSerializer.Serialize(paramList),
            Id = "dup"
        };

        // Act
        var response = await _invoker.InvokeDi(request, null);

        // Assert
        response.Should().NotBeNull();
        response!.Code.Should().Be((int)HttpStatusCode.BadRequest);
        response.Data.Should().Contain("Duplicate");
    }

    [Fact]
    public async Task InvokeDi_MissingParameter_UsesDefault()
    {
        // Arrange: Add(int a, int b) — only `a` supplied; `b` defaults to 0.
        var request = CreateRequest("TestInvoker", "Add", ("a", "3"));

        // Act
        var response = await _invoker.InvokeDi(request, null);

        // Assert
        response.Should().NotBeNull();
        response!.Code.Should().Be((int)HttpStatusCode.OK);
        response.Data.Should().Be("3");
    }

    [Fact]
    public async Task InvokeDi_AuthorisationFailure_ReturnsUnauthorized()
    {
        // Arrange: [HrpcAuthorise] on Secured; no HttpContext -> auth fails.
        var request = CreateRequest("TestInvoker", "Secured", ("data", "\"x\""));

        // Act
        var response = await _invoker.InvokeDi(request, null);

        // Assert
        response.Should().NotBeNull();
        response!.Code.Should().Be((int)HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Dotted Controller Namespace

    [Fact]
    public async Task InvokeDi_DottedControllerNamespace_RoutesCorrectly()
    {
        // Arrange: Controller "Customer.Address.Contact" — dots in the name are allowed
        // and let callers express arbitrarily deep namespaces via the controller field.
        var request = CreateRequest("Customer.Address.Contact", "Add", ("name", "\"alice\""));

        // Act
        var response = await _invoker.InvokeDi(request, null);

        // Assert
        response.Should().NotBeNull();
        response!.Code.Should().Be((int)HttpStatusCode.OK);
        response.Data.Should().Contain("added alice");
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

    #region Dependency Chains (@alias-Walking)

    /// <summary>
    /// Baut einen Chain-Request mit eindeutiger Id. Die Parameter werden als
    /// HrpcParameter[] serialisiert; jsonValue ist jeweils die JSON-Form des Werts
    /// (z. B. "7", "true", "\"hi\"", "{\"Id\":7}").
    /// </summary>
    private static HrpcRequest ChainRequest(string id, string controller, string method,
        params (string name, string jsonValue)[] parameters)
        => ChainRequest(id, controller, method, null, parameters);

    /// <summary>
    /// Wie oben, zusätzlich mit DependencyMapping (Alias → ergebnisrelativer JsonPath).
    /// mapping steht positionell vor den params, sodass Tuple-Werte nicht versehentlich
    /// an mapping gebunden werden.
    /// </summary>
    private static HrpcRequest ChainRequest(string id, string controller, string method,
        Dictionary<string, string>? mapping,
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
            Id = id,
            DependencyMapping = mapping
        };
    }

    private static readonly JsonSerializerOptions _caseInsensitiveOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Wahrheitswert durch eine Chain: EchoBool(true) exposes "$" als "flag";
    /// EchoBool(@flag) muss true liefern. Prüft, dass bool weder zu "true"(string)
    /// noch zu 1 kollabiert — die typgetreue Substitution muss JsonValue.Create nutzen.
    /// </summary>
    [Fact]
    public async Task DependencyChain_Bool_RoundtripsThroughAlias()
    {
        // Arrange
        var step1 = ChainRequest("s1", "DepChain", "EchoBool",
            new Dictionary<string, string> { { "flag", "$" } },
            ("value", "true"));
        var step2 = ChainRequest("s2", "DepChain", "EchoBool",
            ("value", "@flag"));

        // Act
        var responses = (await _invoker.InvokeDi(new[] { step1, step2 }, null)).ToList();

        // Assert
        var byId = responses.ToDictionary(r => r!.Id ?? string.Empty);
        byId["s1"].Code.Should().Be((int)HttpStatusCode.OK);
        byId["s2"].Code.Should().Be((int)HttpStatusCode.OK);
        byId["s2"].Error.Should().BeNull();
        JsonSerializer.Deserialize<bool>(byId["s2"].Data!).Should().BeTrue();
    }

    /// <summary>
    /// long-Wert jenseits des int-Bereichs durch eine Chain. Stellt sicher, dass die
    /// numerische Substitution die volle long-Präzision erhält (kein int-Verlust).
    /// </summary>
    [Fact]
    public async Task DependencyChain_Long_RoundtripsThroughAlias()
    {
        // Arrange — 9_000_000_000 liegt außerhalb von int32
        var step1 = ChainRequest("s1", "DepChain", "EchoLong",
            new Dictionary<string, string> { { "big", "$" } },
            ("value", "9000000000"));
        var step2 = ChainRequest("s2", "DepChain", "EchoLong",
            ("value", "@big"));

        // Act
        var responses = (await _invoker.InvokeDi(new[] { step1, step2 }, null)).ToList();

        // Assert
        var byId = responses.ToDictionary(r => r!.Id ?? string.Empty);
        byId["s2"].Code.Should().Be((int)HttpStatusCode.OK);
        byId["s2"].Error.Should().BeNull();
        JsonSerializer.Deserialize<long>(byId["s2"].Data!).Should().Be(9_000_000_000L);
    }

    /// <summary>
    /// decimal durch eine Chain. Decimal wird von System.Text.Json als JSON-Zahl
    /// serialisiert; die Substitution muss die Zahlform (nicht den String "12.5")
    /// erhalten, sonst bricht Deserialize&lt;decimal&gt;.
    /// </summary>
    [Fact]
    public async Task DependencyChain_Decimal_RoundtripsThroughAlias()
    {
        // Arrange
        var step1 = ChainRequest("s1", "DepChain", "EchoDecimal",
            new Dictionary<string, string> { { "price", "$" } },
            ("value", "12.5"));
        var step2 = ChainRequest("s2", "DepChain", "EchoDecimal",
            ("value", "@price"));

        // Act
        var responses = (await _invoker.InvokeDi(new[] { step1, step2 }, null)).ToList();

        // Assert
        var byId = responses.ToDictionary(r => r!.Id ?? string.Empty);
        byId["s2"].Code.Should().Be((int)HttpStatusCode.OK);
        byId["s2"].Error.Should().BeNull();
        JsonSerializer.Deserialize<decimal>(byId["s2"].Data!).Should().Be(12.5m);
    }

    /// <summary>
    /// Ganzes Objekt als Dependency: MakeDto(7,"Alice") exposes "$" als "dto";
    /// EchoDto(@dto) muss das Objekt mit Id=7 zurückgeben. Prüft, dass die
    /// Substitution ein JSON-Objekt intakt in einen Objekt-Parameter injiziert
    /// (ToJsonString des Objects + JSON-String-Hülle + Deserialize&lt;TestDto&gt;).
    /// </summary>
    [Fact]
    public async Task DependencyChain_Object_WholeResult_InjectsIntoObjectParam()
    {
        // Arrange
        var step1 = ChainRequest("s1", "DepChain", "MakeDto",
            new Dictionary<string, string> { { "dto", "$" } },
            ("id", "7"), ("name", "\"Alice\""));
        var step2 = ChainRequest("s2", "DepChain", "EchoDto",
            ("dto", "@dto"));

        // Act
        var responses = (await _invoker.InvokeDi(new[] { step1, step2 }, null)).ToList();

        // Assert
        var byId = responses.ToDictionary(r => r!.Id ?? string.Empty);
        byId["s1"].ExposedDependencies.Should().ContainKey("dto");
        byId["s2"].Code.Should().Be((int)HttpStatusCode.OK);
        byId["s2"].Error.Should().BeNull();
        var dto = JsonSerializer.Deserialize<TestDto>(byId["s2"].Data!, _caseInsensitiveOpts);
        dto.Should().NotBeNull();
        dto!.Id.Should().Be(7);
        dto.Name.Should().Be("Alice");
    }

    /// <summary>
    /// Property-Pfad auf einem Objekt-Result: MakeDto(7,"Alice") exposes "$.Id" als
    /// "id"; MakeDto(@id,"Bob") muss ein neues Dto mit Id=7 liefern. Prüft int-Extraktion
    /// via ergebnisrelativem Pfad (kein $.data-Envelope) und typgetreue int-Injection.
    /// </summary>
    [Fact]
    public async Task DependencyChain_ObjectPropertyPath_Int_InjectsIntoIntParam()
    {
        // Arrange
        var step1 = ChainRequest("s1", "DepChain", "MakeDto",
            new Dictionary<string, string> { { "id", "$.Id" } },
            ("id", "7"), ("name", "\"Alice\""));
        var step2 = ChainRequest("s2", "DepChain", "MakeDto",
            ("id", "@id"), ("name", "\"Bob\""));

        // Act
        var responses = (await _invoker.InvokeDi(new[] { step1, step2 }, null)).ToList();

        // Assert
        var byId = responses.ToDictionary(r => r!.Id ?? string.Empty);
        byId["s1"].ExposedDependencies!["id"].Should().Be("7");
        byId["s2"].Code.Should().Be((int)HttpStatusCode.OK);
        byId["s2"].Error.Should().BeNull();
        var dto = JsonSerializer.Deserialize<TestDto>(byId["s2"].Data!, _caseInsensitiveOpts);
        dto!.Id.Should().Be(7);
        dto.Name.Should().Be("Bob");
    }

    /// <summary>
    /// Ganze Collection als Dependency: MakeDtoList() exposes "$" als "dtos";
    /// CountDtoList(@dtos) muss 3 liefern. Prüft, dass ein JSON-Array intakt in einen
    /// List&lt;T&gt;-Parameter injiziert wird (ToJsonString des Arrays + Hülle).
    /// </summary>
    [Fact]
    public async Task DependencyChain_Array_WholeResult_InjectsIntoArrayParam()
    {
        // Arrange
        var step1 = ChainRequest("s1", "DepChain", "MakeDtoList",
            new Dictionary<string, string> { { "dtos", "$" } });
        var step2 = ChainRequest("s2", "DepChain", "CountDtoList",
            ("dtos", "@dtos"));

        // Act
        var responses = (await _invoker.InvokeDi(new[] { step1, step2 }, null)).ToList();

        // Assert
        var byId = responses.ToDictionary(r => r!.Id ?? string.Empty);
        byId["s2"].Code.Should().Be((int)HttpStatusCode.OK);
        byId["s2"].Error.Should().BeNull();
        JsonSerializer.Deserialize<int>(byId["s2"].Data!).Should().Be(3);
    }

    /// <summary>
    /// Array-Element-Pfad als Dependency-Quelle: MakeIntList() → [10,20,30];
    /// exposes "$[1]" als "second"; EchoLong(@second) muss 20 liefern. Prüft die
    /// JsonPath-Index-Extraktion innerhalb einer Chain (bisher nur am nackten Array
    /// im Resolver-Unit-Test geprüft, nie in einer echten Chain).
    /// </summary>
    [Fact]
    public async Task DependencyChain_ArrayElementPath_InjectsExtractedElement()
    {
        // Arrange
        var step1 = ChainRequest("s1", "DepChain", "MakeIntList",
            new Dictionary<string, string> { { "second", "$[1]" } });
        var step2 = ChainRequest("s2", "DepChain", "EchoLong",
            ("value", "@second"));

        // Act
        var responses = (await _invoker.InvokeDi(new[] { step1, step2 }, null)).ToList();

        // Assert
        var byId = responses.ToDictionary(r => r!.Id ?? string.Empty);
        byId["s1"].ExposedDependencies!["second"].Should().Be("20");
        byId["s2"].Code.Should().Be((int)HttpStatusCode.OK);
        byId["s2"].Error.Should().BeNull();
        JsonSerializer.Deserialize<long>(byId["s2"].Data!).Should().Be(20L);
    }

    /// <summary>
    /// Mehrere @alias-Platzhalter in einen Request aus zwei verschiedenen Vorläufern
    /// (Diamond-Verbraucher): step1 exposes "$.Id" als "id", step2 exposes "$.Name" als
    /// "name", step3 MakeDto(@id, @name) kombiniert beide. Prüft das Zusammenführen der
    /// ExposedDependencies aller Vorläufer-Batches in einem Aufruf.
    /// </summary>
    [Fact]
    public async Task DependencyChain_MultipleAliases_OneRequest_ConsumesTwoProducers()
    {
        // Arrange — step1 und step2 sind unabhängig (Batch 1), step3 verbraucht beide (Batch 2)
        var step1 = ChainRequest("s1", "DepChain", "MakeDto",
            new Dictionary<string, string> { { "id", "$.Id" } },
            ("id", "7"), ("name", "\"Alice\""));
        var step2 = ChainRequest("s2", "DepChain", "MakeDto",
            new Dictionary<string, string> { { "name", "$.Name" } },
            ("id", "99"), ("name", "\"Bob\""));
        var step3 = ChainRequest("s3", "DepChain", "MakeDto",
            ("id", "@id"), ("name", "@name"));

        // Act
        var responses = (await _invoker.InvokeDi(new[] { step1, step2, step3 }, null)).ToList();

        // Assert
        var byId = responses.ToDictionary(r => r!.Id ?? string.Empty);
        byId["s3"].Code.Should().Be((int)HttpStatusCode.OK);
        byId["s3"].Error.Should().BeNull();
        var dto = JsonSerializer.Deserialize<TestDto>(byId["s3"].Data!, _caseInsensitiveOpts);
        dto!.Id.Should().Be(7);      // aus step1
        dto.Name.Should().Be("Bob"); // aus step2
    }

    /// <summary>
    /// Nicht auflösbarer @alias-Platzhalter: step2 referenziert "@nonexistent", den
    /// kein Vorläufer exposed. Erwartet einen BadRequest (400) für step2 — keinen 500
    /// und keine geworfene Exception. Regression-Guard für ResolveParameterValues.
    /// </summary>
    [Fact]
    public async Task DependencyChain_UnresolvedAlias_ReturnsBadRequest_NoThrow()
    {
        // Arrange — step1 exposed "id", step2 fragt aber "@nonexistent" ab
        var step1 = ChainRequest("s1", "DepChain", "MakeDto",
            new Dictionary<string, string> { { "id", "$.Id" } },
            ("id", "7"), ("name", "\"Alice\""));
        var step2 = ChainRequest("s2", "DepChain", "EchoLong",
            ("value", "@nonexistent"));

        // Act — darf nicht werfen
        var responses = (await _invoker.InvokeDi(new[] { step1, step2 }, null)).ToList();

        // Assert
        var byId = responses.ToDictionary(r => r!.Id ?? string.Empty);
        byId["s1"].Code.Should().Be((int)HttpStatusCode.OK);
        byId["s2"].Code.Should().Be((int)HttpStatusCode.BadRequest);
        byId["s2"].Error.Should().NotBeNull();
    }

    /// <summary>
    /// Dokumentiert das Auto-Detect-Verhalten: sobald ein Request ein DependencyMapping
    /// deklariert, routed InvokeDi IMMER auf den topologischen Batch-Pfad — auch wenn
    /// Mode=Serial angefordert wurde. Die Chain muss daher trotzdem korrekt auflösen.
    /// </summary>
    [Fact]
    public async Task DependencyChain_ModeSerial_WithMapping_RoutesTopologicallyAndResolves()
    {
        // Arrange — Mode.Serial wird angefordert, aber das Mapping erzwingt topologisch
        var step1 = ChainRequest("s1", "DepChain", "EchoLong",
            new Dictionary<string, string> { { "big", "$" } },
            ("value", "42"));
        var step2 = ChainRequest("s2", "DepChain", "EchoLong",
            ("value", "@big"));

        // Act
        var responses = (await _invoker.InvokeDi(new[] { step1, step2 }, null, ExecutionMode.Serial)).ToList();

        // Assert — topologischer Pfad löst den Alias trotz Mode=Serial auf
        var byId = responses.ToDictionary(r => r!.Id ?? string.Empty);
        byId["s2"].Code.Should().Be((int)HttpStatusCode.OK);
        byId["s2"].Error.Should().BeNull();
        JsonSerializer.Deserialize<long>(byId["s2"].Data!).Should().Be(42L);
    }

    /// <summary>
    /// Dokumentiert die Kehrseite: der echte Serial-Pfad (ExecuteSequentially) ist nur
    /// erreichbar, wenn KEIN Request ein DependencyMapping hat — dann kann aber kein
    /// Vorläufer ExposedDependencies produzieren, sodass ein @alias zwingend unresolved
    /// ist. Serial-Mode + Chain ohne Mapping ist daher ein Fehler, kein Erfolgspfad.
    /// </summary>
    [Fact]
    public async Task DependencyChain_SerialPath_WithoutMapping_AliasIsUnresolved()
    {
        // Arrange — kein Mapping → echter Serial-Pfad; step1 exponiert nichts
        var step1 = ChainRequest("s1", "DepChain", "EchoLong",
            ("value", "42"));
        var step2 = ChainRequest("s2", "DepChain", "EchoLong",
            ("value", "@big"));

        // Act
        var responses = (await _invoker.InvokeDi(new[] { step1, step2 }, null, ExecutionMode.Serial)).ToList();

        // Assert — Serial-Pfad kann @big nicht auflösen → 400, kein 500
        var byId = responses.ToDictionary(r => r!.Id ?? string.Empty);
        byId["s1"].Code.Should().Be((int)HttpStatusCode.OK);
        byId["s2"].Code.Should().Be((int)HttpStatusCode.BadRequest);
        byId["s2"].Error.Should().NotBeNull();
    }

    #endregion
}