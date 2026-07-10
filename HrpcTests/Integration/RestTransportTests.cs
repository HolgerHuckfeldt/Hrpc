using FluentAssertions;
using HrpcClient.Hrpc;
using HrpcCommon.Models;
using HrpcCore.Services;
using HrpcTests.Fixtures;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace HrpcTests.Integration;

/// <summary>
/// Integration tests for the REST transport (POST /api/hrpc/json, /api/hrpc/json/multi, GET /api/hrpc/discovery).
/// Uses WebApplicationFactory to spin up the sample app in-memory.
/// </summary>
public class RestTransportTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public RestTransportTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Ensure the test controller is registered
                services.AddTransient<TestInvokerController>();
                services.AddTransient<DependencyChainController>();
                var sp = services.BuildServiceProvider();
                var invoker = sp.GetRequiredService<IHrpcCore>();
                invoker.Register<TestInvokerController>();
                invoker.Register<DependencyChainController>();
            });
        });
        _client = _factory.CreateClient();
    }

    private static StringContent CreateJsonContent(object payload)
        => new(JsonSerializer.Serialize(payload, _jsonOpts), Encoding.UTF8, "application/json");

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

    [Fact]
    public async Task Discovery_Returns200_AndContainsControllers()
    {
        // Act
        var response = await _client.GetAsync("/api/hrpc/discovery");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task SingleCall_Echo_Returns200()
    {
        // Arrange
        var request = CreateRequest("TestService", "GetAdresse",
            ("id", "1"), ("greet", "\"Hello\""));

        // Act
        var response = await _client.PostAsync("/api/hrpc/json", CreateJsonContent(request));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SingleCall_CustomerGetAll_Returns200()
    {
        // Arrange
        var request = CreateRequest("Customer", "GetAllCustomers");

        // Act
        var response = await _client.PostAsync("/api/hrpc/json", CreateJsonContent(request));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task MultiCall_Parallel_Returns200()
    {
        // Arrange
        var multiRequest = new HrpcMultiRequest
        {
            Mode = ExecutionMode.Parallel,
            Requests = new List<HrpcRequest>
            {
                CreateRequest("Customer", "GetAllCustomers"),
                CreateRequest("Customer", "GetAllCustomers")
            }
        };

        // Act
        var response = await _client.PostAsync("/api/hrpc/json/multi", CreateJsonContent(multiRequest));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task MultiCall_Serial_Returns200()
    {
        // Arrange
        var multiRequest = new HrpcMultiRequest
        {
            Mode = ExecutionMode.Serial,
            Requests = new List<HrpcRequest>
            {
                CreateRequest("Customer", "GetAllCustomers"),
                CreateRequest("Customer", "GetAllCustomers")
            }
        };

        // Act
        var response = await _client.PostAsync("/api/hrpc/json/multi", CreateJsonContent(multiRequest));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task MultiCall_EmptyRequests_Returns400()
    {
        // Arrange
        var multiRequest = new HrpcMultiRequest
        {
            Mode = ExecutionMode.Parallel,
            Requests = new List<HrpcRequest>()
        };

        // Act
        var response = await _client.PostAsync("/api/hrpc/json/multi", CreateJsonContent(multiRequest));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SingleCall_FluentPositional_BindsByIndex()
    {
        // Arrange: the fluent builder emits param0/param1; the server must bind them
        // positionally and return the greet value echoed from GetAdresse.
        var request = HrpcCall.Init("TestService", "GetAdresse")
            .With(1, "Hi")
            .ToRequest();

        // Act
        var response = await _client.PostAsync("/api/hrpc/json", CreateJsonContent(request));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var hrpcResp = JsonSerializer.Deserialize<HrpcResponse>(content, _jsonOpts);
        hrpcResp!.Code.Should().Be(200);
        hrpcResp.Data.Value.GetRawText().Should().Contain("\"greet\":\"Hi\"");
        hrpcResp.Data.Value.GetRawText().Should().Contain("\"id\":1");
    }

    [Fact]
    public async Task SingleCall_FluentNamed_BindsByName()
    {
        // Arrange: named parameters bind by explicit name, independent of order.
        var request = HrpcCall.Init("TestService", "GetAdresse")
            .Param("greet", "Hola")
            .Param("id", 2)
            .ToRequest();

        // Act
        var response = await _client.PostAsync("/api/hrpc/json", CreateJsonContent(request));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var hrpcResp = JsonSerializer.Deserialize<HrpcResponse>(content, _jsonOpts);
        hrpcResp!.Code.Should().Be(200);
        hrpcResp.Data.Value.GetRawText().Should().Contain("\"greet\":\"Hola\"");
        hrpcResp.Data.Value.GetRawText().Should().Contain("\"id\":2");
    }

    /// <summary>
    /// End-to-end numeric dependency chain via REST /json/multi:
    /// AddCustomer(name) -> int id, exposes the whole result ("$") as "newId";
    /// GetCustomerById(@newId) -> Customer. Da step1 ein DependencyMapping deklariert,
    /// routed der Server automatisch über den topologischen Batch-Pfad
    /// (ExecuteInDependencyBatches), der @alias-Platzhalter gegen die Responses
    /// VORHERIGER Batches auflösen muss. Regression-Test für zwei früher kaputte
    /// Server-Bugs: (1) der topologische Pfad löste @alias gar nicht auf, (2) die
    /// Substitution stringifizierte Werte, sodass numerische Parameter brachen.
    /// </summary>
    [Fact]
    public async Task DependencyChain_NumericAlias_AutoDetect_ResolvesAndReturnsCustomer()
    {
        // Arrange
        var step1 = new HrpcRequest
        {
            Controller = "Customer",
            Method = "AddCustomer",
            StringData = JsonSerializer.Serialize(new List<HrpcParameter>
            {
                new() { ParameterName = "name", Data = "\"DepChainIT\"" }
            }),
            Id = "chain-step1",
            DependencyMapping = new Dictionary<string, string> { { "newId", "$" } }
        };
        var step2 = new HrpcRequest
        {
            Controller = "Customer",
            Method = "GetCustomerById",
            StringData = JsonSerializer.Serialize(new List<HrpcParameter>
            {
                new() { ParameterName = "id", Data = "@newId" }
            }),
            Id = "chain-step2"
        };
        var multi = new HrpcMultiRequest
        {
            // Mode ist hier bedeutungslos — das DependencyMapping triggert Auto-Detect
            // und damit den topologischen Batch-Pfad unabhängig vom angegebenen Mode.
            Mode = ExecutionMode.Parallel,
            Requests = new List<HrpcRequest> { step1, step2 }
        };

        // Act
        var response = await _client.PostAsync("/api/hrpc/json/multi", CreateJsonContent(multi));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var responses = JsonSerializer.Deserialize<List<HrpcResponse>>(content, _jsonOpts);
        responses.Should().NotBeNull().And.HaveCount(2);
        var byId = responses!.ToDictionary(r => r.Id ?? string.Empty);

        byId["chain-step1"].Code.Should().Be(200);
        byId["chain-step1"].ExposedDependencies.Should().NotBeNull();
        byId["chain-step1"].ExposedDependencies!.Should().ContainKey("newId");

        byId["chain-step2"].Code.Should().Be(200);
        byId["chain-step2"].Error.Should().BeNull();
        // Der @newId-Platzhalter muss durch die numerische Id aus step1 ersetzt worden
        // sein — sonst läge hier ein 500 (NullRef bei int-Unboxing) oder ein 400 vor.
        byId["chain-step2"].Data.Value.GetRawText().Should().Contain("\"name\":\"DepChainIT\"");
    }

    /// <summary>
    /// Drei-Stufen-Chain, die zusätzlich zur numerischen ($) auch eine String-Dependency
    /// über einen ergebnisrelativen Eigenschafts-Pfad ($.name) prüft:
    ///   step1: AddCustomer("SrcNameIT") -> int id; expose newId = "$"
    ///   step2: GetCustomerById(@newId) -> Customer { Name }; expose srcName = "$.name"
    ///   step3: AddCustomer(@srcName)   -> int id (zweiter Kunde mit gleichem Namen)
    /// Stellt sicher, dass die typgetreue Substitution auch String-Werte intakt
    /// transportiert — der JSON-String "\"SrcNameIT\"" (mit Quotes) muss erhalten
    /// bleiben, sonst bricht die Deserialisierung in den string-Parameter "name".
    /// </summary>
    [Fact]
    public async Task DependencyChain_StringAlias_PropertyPath_PreservesValue()
    {
        // Arrange
        var step1 = new HrpcRequest
        {
            Controller = "Customer",
            Method = "AddCustomer",
            StringData = JsonSerializer.Serialize(new List<HrpcParameter>
            {
                new() { ParameterName = "name", Data = "\"SrcNameIT\"" }
            }),
            Id = "str-step1",
            DependencyMapping = new Dictionary<string, string> { { "newId", "$" } }
        };
        var step2 = new HrpcRequest
        {
            Controller = "Customer",
            Method = "GetCustomerById",
            StringData = JsonSerializer.Serialize(new List<HrpcParameter>
            {
                new() { ParameterName = "id", Data = "@newId" }
            }),
            Id = "str-step2",
            DependencyMapping = new Dictionary<string, string> { { "srcName", "$.name" } }
        };
        var step3 = new HrpcRequest
        {
            Controller = "Customer",
            Method = "AddCustomer",
            StringData = JsonSerializer.Serialize(new List<HrpcParameter>
            {
                new() { ParameterName = "name", Data = "@srcName" }
            }),
            Id = "str-step3"
        };
        var multi = new HrpcMultiRequest
        {
            Mode = ExecutionMode.Parallel,
            Requests = new List<HrpcRequest> { step1, step2, step3 }
        };

        // Act
        var response = await _client.PostAsync("/api/hrpc/json/multi", CreateJsonContent(multi));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var responses = JsonSerializer.Deserialize<List<HrpcResponse>>(content, _jsonOpts);
        responses.Should().NotBeNull().And.HaveCount(3);
        var byId = responses!.ToDictionary(r => r.Id ?? string.Empty);

        byId["str-step1"].Code.Should().Be(200);
        byId["str-step2"].Code.Should().Be(200);
        byId["str-step2"].ExposedDependencies.Should().NotBeNull();
        // $.name liefert den JSON-String "\"SrcNameIT\"" (mit Quotes) — nur so
        // übersteht die Substitution für ein string-Ziel intakt.
        byId["str-step2"].ExposedDependencies!["srcName"].Should().Be("\"SrcNameIT\"");

        // step3 muss erfolgreich sein und einen neuen Kunden mit dem übernommenen
        // Namen angelegt haben — ein kaputtes String-Handling (fehlende Quotes)
        // würde hier in einem 400/500 enden.
        byId["str-step3"].Code.Should().Be(200);
        byId["str-step3"].Error.Should().BeNull();
        byId["str-step3"].Data.Should().NotBeNull();
    }

    /// <summary>
    /// Ganzes Objekt als Dependency über HTTP: MakeDto(7,"Alice") exposes "$" als "dto";
    /// EchoDto(@dto) muss das Objekt mit Id=7 zurückgeben. End-to-End-Guard für die
    /// Objekt-Substitution über den REST-/json/multi-Pfad (topologischer Auto-Detect).
    /// </summary>
    [Fact]
    public async Task DependencyChain_ObjectWholeResult_AutoDetect_ResolvesOverHttp()
    {
        // Arrange
        var step1 = new HrpcRequest
        {
            Controller = "DepChain",
            Method = "MakeDto",
            StringData = JsonSerializer.Serialize(new List<HrpcParameter>
            {
                new() { ParameterName = "id", Data = "7" },
                new() { ParameterName = "name", Data = "\"Alice\"" }
            }),
            Id = "obj-s1",
            DependencyMapping = new Dictionary<string, string> { { "dto", "$" } }
        };
        var step2 = new HrpcRequest
        {
            Controller = "DepChain",
            Method = "EchoDto",
            StringData = JsonSerializer.Serialize(new List<HrpcParameter>
            {
                new() { ParameterName = "dto", Data = "@dto" }
            }),
            Id = "obj-s2"
        };
        var multi = new HrpcMultiRequest
        {
            Mode = ExecutionMode.Parallel,
            Requests = new List<HrpcRequest> { step1, step2 }
        };

        // Act
        var response = await _client.PostAsync("/api/hrpc/json/multi", CreateJsonContent(multi));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var responses = JsonSerializer.Deserialize<List<HrpcResponse>>(content, _jsonOpts);
        var byId = responses!.ToDictionary(r => r.Id ?? string.Empty);
        byId["obj-s2"].Code.Should().Be(200);
        byId["obj-s2"].Error.Should().BeNull();
        var dto = byId["obj-s2"].Data.Value.Deserialize<TestDto>(_jsonOpts);
        dto!.Id.Should().Be(7);
        dto.Name.Should().Be("Alice");
    }

    /// <summary>
    /// Ganze Collection als Dependency über HTTP: MakeDtoList() exposes "$" als "dtos";
    /// CountDtoList(@dtos) muss 3 liefern. End-to-End-Guard für Array-Substitution.
    /// </summary>
    [Fact]
    public async Task DependencyChain_ArrayWholeResult_AutoDetect_ResolvesOverHttp()
    {
        // Arrange
        var step1 = new HrpcRequest
        {
            Controller = "DepChain",
            Method = "MakeDtoList",
            StringData = "[]",
            Id = "arr-s1",
            DependencyMapping = new Dictionary<string, string> { { "dtos", "$" } }
        };
        var step2 = new HrpcRequest
        {
            Controller = "DepChain",
            Method = "CountDtoList",
            StringData = JsonSerializer.Serialize(new List<HrpcParameter>
            {
                new() { ParameterName = "dtos", Data = "@dtos" }
            }),
            Id = "arr-s2"
        };
        var multi = new HrpcMultiRequest
        {
            Mode = ExecutionMode.Parallel,
            Requests = new List<HrpcRequest> { step1, step2 }
        };

        // Act
        var response = await _client.PostAsync("/api/hrpc/json/multi", CreateJsonContent(multi));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var responses = JsonSerializer.Deserialize<List<HrpcResponse>>(content, _jsonOpts);
        var byId = responses!.ToDictionary(r => r.Id ?? string.Empty);
        byId["arr-s2"].Code.Should().Be(200);
        byId["arr-s2"].Error.Should().BeNull();
        byId["arr-s2"].Data.Value.Deserialize<int>().Should().Be(3);
    }

    /// <summary>
    /// Mehrere @alias-Platzhalter in einen Request aus zwei Vorläufern über HTTP
    /// (Diamond-Verbraucher): step1 exposes "$.id" als "id", step2 exposes "$.name" als
    /// "name", step3 MakeDto(@id, @name) kombiniert beide.
    /// </summary>
    [Fact]
    public async Task DependencyChain_MultipleAliases_AutoDetect_ResolvesOverHttp()
    {
        // Arrange
        var step1 = new HrpcRequest
        {
            Controller = "DepChain",
            Method = "MakeDto",
            StringData = JsonSerializer.Serialize(new List<HrpcParameter>
            {
                new() { ParameterName = "id", Data = "7" },
                new() { ParameterName = "name", Data = "\"Alice\"" }
            }),
            Id = "ma-s1",
            DependencyMapping = new Dictionary<string, string> { { "id", "$.id" } }
        };
        var step2 = new HrpcRequest
        {
            Controller = "DepChain",
            Method = "MakeDto",
            StringData = JsonSerializer.Serialize(new List<HrpcParameter>
            {
                new() { ParameterName = "id", Data = "99" },
                new() { ParameterName = "name", Data = "\"Bob\"" }
            }),
            Id = "ma-s2",
            DependencyMapping = new Dictionary<string, string> { { "name", "$.name" } }
        };
        var step3 = new HrpcRequest
        {
            Controller = "DepChain",
            Method = "MakeDto",
            StringData = JsonSerializer.Serialize(new List<HrpcParameter>
            {
                new() { ParameterName = "id", Data = "@id" },
                new() { ParameterName = "name", Data = "@name" }
            }),
            Id = "ma-s3"
        };
        var multi = new HrpcMultiRequest
        {
            Mode = ExecutionMode.Parallel,
            Requests = new List<HrpcRequest> { step1, step2, step3 }
        };

        // Act
        var response = await _client.PostAsync("/api/hrpc/json/multi", CreateJsonContent(multi));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var responses = JsonSerializer.Deserialize<List<HrpcResponse>>(content, _jsonOpts);
        var byId = responses!.ToDictionary(r => r.Id ?? string.Empty);
        byId["ma-s3"].Code.Should().Be(200);
        byId["ma-s3"].Error.Should().BeNull();
        var dto = byId["ma-s3"].Data.Value.Deserialize<TestDto>(_jsonOpts);
        dto!.Id.Should().Be(7);
        dto.Name.Should().Be("Bob");
    }

    /// <summary>
    /// Nicht auflösbarer @alias über HTTP: step2 fragt "@nonexistent" ab, den kein
    /// Vorläufer exposed. Der /json/multi-Endpoint liefert HTTP 200 mit pro-Response-
    /// Codes; step2 muss 400 tragen (kein 500, keine Server-Exception).
    /// </summary>
    [Fact]
    public async Task DependencyChain_UnresolvedAlias_Returns400OverHttp()
    {
        // Arrange
        var step1 = new HrpcRequest
        {
            Controller = "DepChain",
            Method = "MakeDto",
            StringData = JsonSerializer.Serialize(new List<HrpcParameter>
            {
                new() { ParameterName = "id", Data = "7" },
                new() { ParameterName = "name", Data = "\"Alice\"" }
            }),
            Id = "un-s1",
            DependencyMapping = new Dictionary<string, string> { { "id", "$.id" } }
        };
        var step2 = new HrpcRequest
        {
            Controller = "DepChain",
            Method = "EchoLong",
            StringData = JsonSerializer.Serialize(new List<HrpcParameter>
            {
                new() { ParameterName = "value", Data = "@nonexistent" }
            }),
            Id = "un-s2"
        };
        var multi = new HrpcMultiRequest
        {
            Mode = ExecutionMode.Parallel,
            Requests = new List<HrpcRequest> { step1, step2 }
        };

        // Act
        var response = await _client.PostAsync("/api/hrpc/json/multi", CreateJsonContent(multi));

        // Assert — Endpoint bleibt 200, der Fehler steckt in der Einzel-Response
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var responses = JsonSerializer.Deserialize<List<HrpcResponse>>(content, _jsonOpts);
        var byId = responses!.ToDictionary(r => r.Id ?? string.Empty);
        byId["un-s1"].Code.Should().Be(200);
        byId["un-s2"].Code.Should().Be(400);
        byId["un-s2"].Error.Should().NotBeNull();
    }

    /// <summary>
    /// Mode=Serial mit DependencyMapping über HTTP: Auto-Detect übersteuert auf den
    /// topologischen Pfad, die Chain muss trotzdem korrekt auflösen. Garantiert, dass
    /// ein Client, der Serial anfordert und trotzdem Dependencies deklariert, keine
    /// kaputte Antwort bekommt.
    /// </summary>
    [Fact]
    public async Task DependencyChain_ModeSerial_WithMapping_ResolvesOverHttp()
    {
        // Arrange
        var step1 = new HrpcRequest
        {
            Controller = "DepChain",
            Method = "EchoLong",
            StringData = JsonSerializer.Serialize(new List<HrpcParameter>
            {
                new() { ParameterName = "value", Data = "42" }
            }),
            Id = "se-s1",
            DependencyMapping = new Dictionary<string, string> { { "big", "$" } }
        };
        var step2 = new HrpcRequest
        {
            Controller = "DepChain",
            Method = "EchoLong",
            StringData = JsonSerializer.Serialize(new List<HrpcParameter>
            {
                new() { ParameterName = "value", Data = "@big" }
            }),
            Id = "se-s2"
        };
        var multi = new HrpcMultiRequest
        {
            Mode = ExecutionMode.Serial,
            Requests = new List<HrpcRequest> { step1, step2 }
        };

        // Act
        var response = await _client.PostAsync("/api/hrpc/json/multi", CreateJsonContent(multi));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var responses = JsonSerializer.Deserialize<List<HrpcResponse>>(content, _jsonOpts);
        var byId = responses!.ToDictionary(r => r.Id ?? string.Empty);
        byId["se-s2"].Code.Should().Be(200);
        byId["se-s2"].Error.Should().BeNull();
        byId["se-s2"].Data.Value.Deserialize<long>().Should().Be(42L);
    }

}