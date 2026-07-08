using FluentAssertions;
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
                var sp = services.BuildServiceProvider();
                var invoker = sp.GetRequiredService<IHrpcCore>();
                invoker.Register<TestInvokerController>();
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

}