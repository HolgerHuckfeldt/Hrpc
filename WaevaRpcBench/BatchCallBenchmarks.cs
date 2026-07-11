using BenchmarkDotNet.Attributes;
using WaevaRpcCommon.Models;

namespace WaevaRpcBench;

/// <summary>
/// 100-Call-Matrix: 100× GetCustomerById — nativ (REST/gRPC) seriell+parallel
/// vs. WaevaRpc-Batch (REST/WebSocket/SignalR) parallel+serial. Ein WaevaRpc-Batch = 1
/// Roundtrip, der 100 Aufrufe serverseitig bündelt; nativ braucht 100 Roundtrips.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
[HideColumns("Error", "StdDev")]
public class BatchCallBenchmarks
{
    private BenchmarkFixture _fixture = null!;
    private List<WaevaRpcRequest> _batchRequests = null!;
    private const int BatchSize = 100;

    [GlobalSetup]
    public void Setup()
    {
        _fixture = new BenchmarkFixture();
        _fixture.Initialize();
        _batchRequests = new List<WaevaRpcRequest>();
        for (int i = 1; i <= BatchSize; i++)
        {
            _batchRequests.Add(new WaevaRpcRequest
            {
                Controller = "Customer",
                Method = "GetCustomerById",
                StringData = "[{\"" + "ParameterName" + "\":\"" + "id" + "\",\"" + "Data" + "\":\"" + i + "\"}]",
                Id = "req-" + i
            });
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _fixture.Dispose();

    // ─── Natives REST (100 Roundtrips) ───────────────────────────────

    [Benchmark(Description = "REST: 100x sequential GetCustomerById", Baseline = true)]
    [BenchmarkCategory("REST")]
    public async Task Rest_Sequential100()
    {
        for (int i = 1; i <= BatchSize; i++)
            await _fixture.RestGetCustomerById(i);
    }

    [Benchmark(Description = "REST: 100x parallel GetCustomerById")]
    [BenchmarkCategory("REST")]
    public async Task Rest_Parallel100()
    {
        var tasks = Enumerable.Range(1, BatchSize).Select(i => _fixture.RestGetCustomerById(i));
        await Task.WhenAll(tasks);
    }

    // ─── WaevaRpc REST (1 Roundtrip, 100 Aufrufe gebündelt) ─────────────

    [Benchmark(Description = "WaevaRpc REST: 100x batch parallel")]
    [BenchmarkCategory("WaevaRpc")]
    public async Task<object?> WaevaRpcRest_BatchParallel()
    {
        var multiRequest = new WaevaRpcMultiRequest
        {
            Mode = ExecutionMode.Parallel,
            Requests = _batchRequests
        };
        return await _fixture.WaevaRpcRestClient.Call(multiRequest);
    }

    [Benchmark(Description = "WaevaRpc REST: 100x batch serial")]
    [BenchmarkCategory("WaevaRpc")]
    public async Task<object?> WaevaRpcRest_BatchSerial()
    {
        var multiRequest = new WaevaRpcMultiRequest
        {
            Mode = ExecutionMode.Serial,
            Requests = _batchRequests.ToList()
        };
        return await _fixture.WaevaRpcRestClient.Call(multiRequest);
    }

    // ─── WaevaRpc WebSocket (1 Roundtrip über persistente Verbindung) ────

    [Benchmark(Description = "WaevaRpc WebSocket: 100x batch parallel")]
    [BenchmarkCategory("WaevaRpc")]
    public async Task<object?> WaevaRpcWs_BatchParallel()
    {
        var multiRequest = new WaevaRpcMultiRequest
        {
            Mode = ExecutionMode.Parallel,
            Requests = _batchRequests
        };
        return await _fixture.WaevaRpcWebSocketClient.Call(multiRequest);
    }

    [Benchmark(Description = "WaevaRpc WebSocket: 100x batch serial")]
    [BenchmarkCategory("WaevaRpc")]
    public async Task<object?> WaevaRpcWs_BatchSerial()
    {
        var multiRequest = new WaevaRpcMultiRequest
        {
            Mode = ExecutionMode.Serial,
            Requests = _batchRequests.ToList()
        };
        return await _fixture.WaevaRpcWebSocketClient.Call(multiRequest);
    }

    // ─── WaevaRpc SignalR (1 Roundtrip, MessagePack-Binär) ───────────────

    [Benchmark(Description = "WaevaRpc SignalR: 100x batch parallel")]
    [BenchmarkCategory("WaevaRpc")]
    public async Task<object?> WaevaRpcSignalR_BatchParallel()
    {
        var multiRequest = new WaevaRpcMultiRequest
        {
            Mode = ExecutionMode.Parallel,
            Requests = _batchRequests
        };
        return await _fixture.WaevaRpcSignalrClient.Call(multiRequest);
    }

    [Benchmark(Description = "WaevaRpc SignalR: 100x batch serial")]
    [BenchmarkCategory("WaevaRpc")]
    public async Task<object?> WaevaRpcSignalR_BatchSerial()
    {
        var multiRequest = new WaevaRpcMultiRequest
        {
            Mode = ExecutionMode.Serial,
            Requests = _batchRequests.ToList()
        };
        return await _fixture.WaevaRpcSignalrClient.Call(multiRequest);
    }

    // ─── Natives gRPC (100 Roundtrips, Protobuf) ────────────────────

    [Benchmark(Description = "gRPC: 100x sequential GetCustomerById")]
    [BenchmarkCategory("gRPC")]
    public async Task Grpc_Sequential100()
    {
        for (int i = 1; i <= BatchSize; i++)
            await _fixture.GrpcGetCustomerById(i);
    }

    [Benchmark(Description = "gRPC: 100x parallel GetCustomerById")]
    [BenchmarkCategory("gRPC")]
    public async Task Grpc_Parallel100()
    {
        var tasks = Enumerable.Range(1, BatchSize).Select(i => _fixture.GrpcGetCustomerById(i));
        await Task.WhenAll(tasks);
    }
}