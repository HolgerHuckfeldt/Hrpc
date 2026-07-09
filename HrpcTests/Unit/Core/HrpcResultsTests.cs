using FluentAssertions;
using HrpcCommon.Models;
using HrpcCommon.Results;
using System.Text.Json;
using Xunit;

namespace HrpcTests.Unit.Core;

/// <summary>
/// Unit tests für <see cref="HrpcResults"/> — verifiziert Code/Data/Error-Belegung
/// und IsSuccess-Ableitung für den unterstützten Controller-Fehlerkanal.
/// </summary>
public class HrpcResultsTests
{
    [Fact]
    public void Ok_object_serializes_result_and_sets_200()
    {
        var resp = HrpcResults.Ok(new { Id = 7, Name = "Alice" });

        resp.Code.Should().Be(200);
        resp.IsSuccess.Should().BeTrue();
        resp.Error.Should().BeNull();
        // Default-Options = PascalCase (wie der Invoker-Pfad).
        resp.Data.Should().Contain("\"Id\":7");
        resp.Data.Should().Contain("\"Name\":\"Alice\"");
    }

    [Fact]
    public void Ok_null_returns_no_content()
    {
        var resp = HrpcResults.Ok((object?)null);

        resp.Code.Should().Be(204);
        resp.IsSuccess.Should().BeTrue();
        resp.Data.Should().BeNull();
    }

    [Fact]
    public void Ok_string_keeps_raw_data()
    {
        var resp = HrpcResults.Ok("{\"id\":42}");

        resp.Code.Should().Be(200);
        resp.Data.Should().Be("{\"id\":42}");
    }

    [Fact]
    public void Ok_binary_sets_base64_data_and_raw_content()
    {
        var bytes = new byte[] { 1, 2, 3, 250 };

        var resp = HrpcResults.Ok(bytes);

        resp.Code.Should().Be(200);
        resp.Content.Should().BeEquivalentTo(bytes);
        resp.Data.Should().Be(Convert.ToBase64String(bytes));
    }

    [Fact]
    public void NoContent_is_204_without_data()
    {
        var resp = HrpcResults.NoContent();

        resp.Code.Should().Be(204);
        resp.IsSuccess.Should().BeTrue();
        resp.Data.Should().BeNull();
    }

    [Fact]
    public void Error_sets_code_data_and_structured_error()
    {
        var resp = HrpcResults.Error(404, "Customer '99' not found.");

        resp.Code.Should().Be(404);
        resp.IsSuccess.Should().BeFalse();
        // Data und Error.Message konsistent belegt (beide Wege abgedeckt).
        resp.Data.Should().Be("Customer '99' not found.");
        resp.Error.Should().NotBeNull();
        resp.Error!.Code.Should().Be(404);
        resp.Error.Message.Should().Be("Customer '99' not found.");
        resp.Error.Details.Should().BeNull();
        resp.Error.RequestId.Should().BeNull(); // wird vom Invoker/Transport gesetzt
    }

    [Fact]
    public void Error_with_details_carries_them()
    {
        var resp = HrpcResults.Error(400, "Invalid parameter 'id'.", "Expected positive int, got -1.");

        resp.Error!.Details.Should().Be("Expected positive int, got -1.");
    }

    [Theory]
    [InlineData(nameof(HrpcResults.BadRequest), 400)]
    [InlineData(nameof(HrpcResults.NotFound), 404)]
    [InlineData(nameof(HrpcResults.Conflict), 409)]
    [InlineData(nameof(HrpcResults.InternalServerError), 500)]
    public void Convenience_methods_use_correct_codes(string method, int expected)
    {
        var resp = method switch
        {
            nameof(HrpcResults.BadRequest) => HrpcResults.BadRequest("x"),
            nameof(HrpcResults.NotFound) => HrpcResults.NotFound("x"),
            nameof(HrpcResults.Conflict) => HrpcResults.Conflict("x"),
            nameof(HrpcResults.InternalServerError) => HrpcResults.InternalServerError("x"),
            _ => throw new InvalidOperationException($"unmapped method {method}"),
        };

        resp.Code.Should().Be(expected);
        resp.IsSuccess.Should().BeFalse();
        resp.Error!.Code.Should().Be(expected);
    }

    [Fact]
    public void Unauthorized_defaults_to_401()
    {
        var resp = HrpcResults.Unauthorized();

        resp.Code.Should().Be(401);
        resp.Error!.Message.Should().Be("Unauthorized.");
    }

    [Fact]
    public void ProblemDetails_serializes_camel_case_and_maps_title_detail_to_error()
    {
        var problem = new ProblemDetails
        {
            Type = "https://example.com/errors/customer-not-found",
            Title = "Customer not found.",
            Status = 404,
            Detail = "No customer with id 99.",
            Instance = "/api/hrpc/json",
        };

        var resp = HrpcResults.Error(problem);

        resp.Code.Should().Be(404);
        resp.IsSuccess.Should().BeFalse();

        // RFC 7807: CamelCase-Keys im Data-JSON.
        using var doc = JsonDocument.Parse(resp.Data!);
        doc.RootElement.TryGetProperty("type", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("title", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("status", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("detail", out _).Should().BeTrue();
        doc.RootElement.GetProperty("status").GetInt32().Should().Be(404);

        // Title/Detail zusätzlich auf HrpcError gespiegelt (einfache Clients).
        resp.Error!.Message.Should().Be("Customer not found.");
        resp.Error.Details.Should().Be("No customer with id 99.");
        resp.Error.Code.Should().Be(404);
    }
}