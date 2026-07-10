using FluentAssertions;
using HrpcCore.Services.Helper;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace HrpcTests.Unit.Core;

/// <summary>
/// Unit tests for DependencyResolver – JsonPath-based value extraction.
/// </summary>
public class DependencyResolverTests
{
    [Fact]
    public void ExtractValue_SimplePath_ReturnsValue()
    {
        // Arrange
        var json = "{\"Id\":42,\"Name\":\"Test\"}";

        // Act
        var result = DependencyResolver.ExtractValue(JsonDocument.Parse(json).RootElement.Clone(), "$.Id");

        // Assert
        result.Should().NotBeNull();
        result!.GetValue<int>().Should().Be(42);
    }

    [Fact]
    public void ExtractValue_NestedPath_ReturnsValue()
    {
        // Arrange
        var json = "{\"Customer\":{\"Id\":7,\"Name\":\"Alice\"}}";

        // Act
        var result = DependencyResolver.ExtractValue(JsonDocument.Parse(json).RootElement.Clone(), "$.Customer.Id");

        // Assert
        result.Should().NotBeNull();
        result!.GetValue<int>().Should().Be(7);
    }

    [Fact]
    public void ExtractValue_ArrayIndex_ReturnsValue()
    {
        // Arrange
        var json = "[10,20,30]";

        // Act
        var result = DependencyResolver.ExtractValue(JsonDocument.Parse(json).RootElement.Clone(), "$[1]");

        // Assert
        result.Should().NotBeNull();
        result!.GetValue<int>().Should().Be(20);
    }

    [Fact]
    public void ExtractValue_StringValue_ReturnsString()
    {
        // Arrange
        var json = "{\"Name\":\"Hello\"}";

        // Act
        var result = DependencyResolver.ExtractValue(JsonDocument.Parse(json).RootElement.Clone(), "$.Name");

        // Assert
        result.Should().NotBeNull();
        result!.GetValue<string>().Should().Be("Hello");
    }

    [Fact]
    public void ExtractValue_NonExistentPath_ReturnsNull()
    {
        // Arrange
        var json = "{\"Id\":1}";

        // Act
        var result = DependencyResolver.ExtractValue(JsonDocument.Parse(json).RootElement.Clone(), "$.NonExistent");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ExtractValue_RootPath_ReturnsEntireDocument()
    {
        // Arrange
        var json = "42";

        // Act
        var result = DependencyResolver.ExtractValue(JsonDocument.Parse(json).RootElement.Clone(), "$");

        // Assert
        result.Should().NotBeNull();
        result!.GetValue<int>().Should().Be(42);
    }
}