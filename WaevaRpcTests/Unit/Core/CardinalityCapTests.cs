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
/// Kardinalitäts-Caps des Invokers — MaxParameterArrayLength (Default 1000) und
/// MaxResultElementCount (Default 10000), jeweils 0 = unbegrenzt. Schützen den Server
/// vor Riesen-Arrays, insb. beim @alias-Whole-Collection-Passthrough (server-seitig
/// erzeugte Arrays — Body-Size-Limits greifen dort nicht). Jeder Test baut einen
/// frischen Invoker mit explizit gesetztem Cap, um die Defaults nicht zu kreuzen.
/// </summary>
public class CardinalityCapTests
{
    private static WaevaRpcInvoker BuildInvoker(int maxParameterArrayLength, int maxResultElementCount)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTransient<TestInvokerController>();
        services.AddTransient<DependencyChainController>();
        var sp = services.BuildServiceProvider();
        var invoker = new WaevaRpcInvoker(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<ILogger<WaevaRpcInvoker>>());
        invoker.Register<TestInvokerController>();
        invoker.Register<DependencyChainController>();
        invoker.MaxParameterArrayLength = maxParameterArrayLength;
        invoker.MaxResultElementCount = maxResultElementCount;
        return invoker;
    }

    private static WaevaRpcRequest CreateRequest(string controller, string method,
        params (string name, string jsonValue)[] parameters)
    {
        var paramList = parameters.Select(p => new WaevaRpcParameter
        {
            ParameterName = p.name,
            Data = p.jsonValue
        }).ToList();
        return new WaevaRpcRequest
        {
            Controller = controller,
            Method = method,
            StringData = JsonSerializer.Serialize(paramList),
            Id = $"{controller}.{method}"
        };
    }

    private static string JsonArray(int count) =>
        "[" + string.Join(",", Enumerable.Range(1, count)) + "]";

    #region MaxParameterArrayLength

    [Fact]
    public async Task MaxParameterArrayLength_RejectsOversizedArray()
    {
        // Arrange: Cap 5, Array mit 10 Elementen.
        var invoker = BuildInvoker(maxParameterArrayLength: 5, maxResultElementCount: 0);
        var request = CreateRequest("DepChain", "EchoIntList", ("values", JsonArray(10)));

        // Act
        var response = await invoker.InvokeDi(request, null);

        // Assert: 400 + Hinweis auf MaxParameterArrayLength. Methode wird nicht aufgerufen.
        response.Should().NotBeNull();
        response!.Code.Should().Be((int)HttpStatusCode.BadRequest);
        response.Error.Should().NotBeNull();
        response.Error!.Message.Should().Contain("MaxParameterArrayLength");
    }

    [Fact]
    public async Task MaxParameterArrayLength_AllowsUnderCap()
    {
        // Arrange: Cap 5, Array mit 3 Elementen.
        var invoker = BuildInvoker(maxParameterArrayLength: 5, maxResultElementCount: 0);
        var request = CreateRequest("DepChain", "EchoIntList", ("values", JsonArray(3)));

        // Act
        var response = await invoker.InvokeDi(request, null);

        // Assert: 200, Liste intakt durchgereicht.
        response.Should().NotBeNull();
        response!.Code.Should().Be((int)HttpStatusCode.OK);
        var echoed = response.Data!.Value.Deserialize<List<int>>();
        echoed.Should().HaveCount(3).And.BeEquivalentTo(new[] { 1, 2, 3 });
    }

    [Fact]
    public async Task MaxParameterArrayLength_Zero_AllowsUnlimited()
    {
        // Arrange: Cap 0 (unbegrenzt), großes Array (über dem Default von 1000).
        var invoker = BuildInvoker(maxParameterArrayLength: 0, maxResultElementCount: 0);
        var request = CreateRequest("DepChain", "EchoIntList", ("values", JsonArray(2000)));

        // Act
        var response = await invoker.InvokeDi(request, null);

        // Assert: 200 — 0 schaltet den Cap ab.
        response.Should().NotBeNull();
        response!.Code.Should().Be((int)HttpStatusCode.OK);
        var echoed = response.Data!.Value.Deserialize<List<int>>();
        echoed.Should().HaveCount(2000);
    }

    [Fact]
    public async Task MaxParameterArrayLength_StringParam_NotCounted()
    {
        // Arrange: Cap 5, aber ein langer String-Parameter. string ist IEnumerable<char>,
        // darf also NICHT als Collection gezählt werden.
        var invoker = BuildInvoker(maxParameterArrayLength: 5, maxResultElementCount: 0);
        var longString = new string('x', 500);
        var request = CreateRequest("TestInvoker", "Echo",
            ("message", JsonSerializer.Serialize(longString)));

        // Act
        var response = await invoker.InvokeDi(request, null);

        // Assert: 200 — String-Parameter wird nicht vom Array-Cap erfasst.
        response.Should().NotBeNull();
        response!.Code.Should().Be((int)HttpStatusCode.OK);
    }

    #endregion

    #region MaxResultElementCount

    [Fact]
    public async Task MaxResultElementCount_RejectsOversizedStreamResult()
    {
        // Arrange: Result-Cap 5, Stream liefert 10 Elemente → Early-Stop beim Konsumieren.
        var invoker = BuildInvoker(maxParameterArrayLength: 0, maxResultElementCount: 5);
        var request = CreateRequest("TestInvoker", "StreamNumbers", ("count", "10"));

        // Act
        var response = await invoker.InvokeDi(request, null);

        // Assert: 413 + Hinweis auf MaxResultElementCount (Streaming-Pfad).
        response.Should().NotBeNull();
        response!.Code.Should().Be((int)HttpStatusCode.RequestEntityTooLarge);
        response.Error.Should().NotBeNull();
        response.Error!.Message.Should().Contain("MaxResultElementCount");
    }

    [Fact]
    public async Task MaxResultElementCount_AllowsUnderCapStream()
    {
        // Arrange: Result-Cap 5, Stream liefert 3 Elemente.
        var invoker = BuildInvoker(maxParameterArrayLength: 0, maxResultElementCount: 5);
        var request = CreateRequest("TestInvoker", "StreamNumbers", ("count", "3"));

        // Act
        var response = await invoker.InvokeDi(request, null);

        // Assert: 200, 3 Elemente.
        response.Should().NotBeNull();
        response!.Code.Should().Be((int)HttpStatusCode.OK);
        var numbers = response.Data!.Value.Deserialize<List<int>>();
        numbers.Should().HaveCount(3);
    }

    [Fact]
    public async Task MaxResultElementCount_RejectsOversizedMaterializedResult()
    {
        // Arrange: Result-Cap 3 (Param-Cap 0, damit der 10-Element-Input durchgeht),
        // EchoIntList gibt 10 Elemente zurück → ReturnResponse-Cap schlägt zu.
        var invoker = BuildInvoker(maxParameterArrayLength: 0, maxResultElementCount: 3);
        var request = CreateRequest("DepChain", "EchoIntList", ("values", JsonArray(10)));

        // Act
        var response = await invoker.InvokeDi(request, null);

        // Assert: 413 (materialisierter List<T>-Return-Pfad).
        response.Should().NotBeNull();
        response!.Code.Should().Be((int)HttpStatusCode.RequestEntityTooLarge);
        response.Error.Should().NotBeNull();
        response.Error!.Message.Should().Contain("MaxResultElementCount");
    }

    #endregion

    #region Defaults

    [Fact]
    public void Defaults_AreSecureByDefault()
    {
        // Ein nackter `new WaevaRpcInvoker(...)` ohne Optionen-Verdrahtung muss
        // trotzdem geschützt sein („Server schützt sich selbst").
        var services = new ServiceCollection();
        services.AddLogging();
        var sp = services.BuildServiceProvider();
        var invoker = new WaevaRpcInvoker(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<ILogger<WaevaRpcInvoker>>());

        invoker.MaxParameterArrayLength.Should().Be(1000);
        invoker.MaxResultElementCount.Should().Be(10000);
    }

    #endregion
}