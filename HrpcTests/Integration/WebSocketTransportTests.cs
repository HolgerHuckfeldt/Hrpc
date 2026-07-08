using FluentAssertions;
using HrpcClient.Hrpc;
using HrpcCommon.Models;
using HrpcTests.Fixtures;
using System.Text;
using System.Text.Json;
using Xunit;

namespace HrpcTests.Integration;

/// <summary>
/// End-to-End-Tests für den WebSocket-Transport gegen einen realen Kestrel-Host.
/// Deckt die behobenen Korrektheits-Bugs ab: Response-Deserialisierung (A1),
/// UTF-8-Chunk-Decoding (A2), Void/204, Binary, parallele Korrelation (B3),
/// Cancellation, dotted Namespace.
/// </summary>
public class WebSocketTransportTests : IClassFixture<TransportTestFixture>
{
    private readonly TransportTestFixture _fixture;

    public WebSocketTransportTests(TransportTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Echo_Returns200_AndData()
    {
        // Beweist A1: pre-Fix wäre Code=0 / Data=null wegen Default-Options.
        var client = _fixture.CreateWsClient();
        var request = HrpcCall.Init("TestInvoker", "Echo").With("hello").ToRequest();

        var resp = await client.Call(request);

        resp!.Code.Should().Be(200);
        resp.Data.Should().NotBeNull();
        resp.Data.Should().Contain("hello");
    }

    [Fact]
    public async Task Echo_NonAscii_LargePayload_RoundTrips()
    {
        // Beweist A2: ≥8 KB mit Multi-Byte-Zeichen über Chunk-Grenzen hinweg.
        var client = _fixture.CreateWsClient();
        var payload = new string('ü', 3000) + "🎉" + new string('ä', 3000);
        var request = HrpcCall.Init("TestInvoker", "Echo").With(payload).ToRequest();

        var result = await client.Call<string>(request);

        result.Should().Be(payload);
    }

    [Fact]
    public async Task VoidMethod_Returns204()
    {
        var client = _fixture.CreateWsClient();
        var request = HrpcCall.Init("TestInvoker", "VoidMethod").With("data").ToRequest();

        var resp = await client.Call(request);

        resp!.Code.Should().Be(204);
        resp.IsSuccess.Should().BeTrue();
        resp.Data.Should().BeNull();
    }

    [Fact]
    public async Task UploadBlob_DownloadBlob_BinaryRoundTrip()
    {
        var client = _fixture.CreateWsClient();
        var bytes = Encoding.UTF8.GetBytes("hello-binary-payload-" + new string('x', 5000));

        // byte[] data wird aus BinaryData injiziert; filename als normaler Parameter.
        var upload = new HrpcRequest
        {
            Controller = "TestInvoker",
            Method = "UploadBlob",
            StringData = JsonSerializer.Serialize(new[]
            {
                new HrpcParameter { ParameterName = "filename", Data = JsonSerializer.Serialize("f.bin") }
            }),
            BinaryData = bytes,
            Id = "TestInvoker.UploadBlob"
        };

        var uploadResp = await client.Call(upload);
        uploadResp!.Code.Should().Be(200);
        uploadResp.Data.Should().Contain(bytes.Length.ToString());

        // DownloadBlob liefert byte[] -> Content-Feld.
        var download = HrpcCall.Init("TestInvoker", "DownloadBlob").With("blob").ToRequest();
        var received = await client.CallBinary(download);

        received.Should().NotBeNull();
        received.Should().Equal(Encoding.UTF8.GetBytes("Blob content for blob"));
    }

    [Fact]
    public async Task ParallelEcho_CorrelatesCorrectly()
    {
        // Beweist B3: jeder parallele Call bekommt exakt sein eigenes Resultat.
        var client = _fixture.CreateWsClient();
        var tasks = Enumerable.Range(0, 5).Select(i =>
            client.Call(HrpcCall.Init("TestInvoker", "Echo")
                .With($"msg{i}").Named($"req{i}").ToRequest())).ToList();

        var responses = await Task.WhenAll(tasks);

        for (var i = 0; i < 5; i++)
        {
            responses[i]!.Code.Should().Be(200);
            responses[i]!.Id.Should().Be($"req{i}");
            responses[i]!.Data.Should().Contain($"msg{i}");
        }
    }

    [Fact]
    public async Task WithCancellation_PropagatesCancellation()
    {
        // Cancellation muss als OCE propagieren, nicht als HrpcException (P0.7).
        var client = _fixture.CreateWsClient();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var request = HrpcCall.Init("TestInvoker", "WithCancellation").With("x").ToRequest();

        var act = async () => await client.Call(request, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task NestedNamespace_Add_RoutesCorrectly()
    {
        var client = _fixture.CreateWsClient();
        var request = HrpcCall.Init("Customer.Address.Contact", "Add").With("Alice").ToRequest();

        var resp = await client.Call(request);

        resp!.Code.Should().Be(200);
        resp.Data.Should().Contain("added Alice");
    }
}