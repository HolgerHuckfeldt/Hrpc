using BenchmarkDotNet.Attributes;
using HrpcCommon.Models;

namespace HrpcBench;

/// <summary>
/// 100-Call-Matrix: 100× GetCustomerById — nativ (REST/gRPC) seriell+parallel
/// vs. Hrpc-Batch (REST/WebSocket/SignalR) parallel+serial. Ein Hrpc-Batch = 1
/// Roundtrip, der 100 Aufrufe serverseitig bündelt; nativ braucht 100 Roundtrips.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
[HideColumns("Error", "StdDev")]
public class BatchCallBenchmarks
{
    private BenchmarkFixture _fixture = null!;
    private List<HrpcRequest> _batchRequests = null!;
    private const int BatchSize = 100;

    [GlobalSetup]
    public void Setup()
    {
        _fixture = new BenchmarkFixture();
        _fixture.Initialize();
        _batchRequests = new List<HrpcRequest>();
        for (int i = 1; i <= BatchSize; i++)
        {
            _batchRequests.Add(new HrpcRequest
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

    // ─── Hrpc REST (1 Roundtrip, 100 Aufrufe gebündelt) ─────────────

    [Benchmark(Description = "Hrpc REST: 100x batch parallel")]
    [BenchmarkCategory("Hrpc")]
    public async Task<object?> HrpcRest_BatchParallel()
    {
        var multiRequest = new HrpcMultiRequest
        {
            Mode = ExecutionMode.Parallel,
            Requests = _batchRequests
        };
        return await _fixture.HrpcRestClient.Call(multiRequest);
    }

    [Benchmark(Description = "Hrpc REST: 100x batch serial")]
    [BenchmarkCategory("Hrpc")]
    public async Task<object?> HrpcRest_BatchSerial()
    {
        var multiRequest = new HrpcMultiRequest
        {
            Mode = ExecutionMode.Serial,
            Requests = _batchRequests.ToList()
        };
        return await _fixture.HrpcRestClient.Call(multiRequest);
    }

    // ─── Hrpc WebSocket (1 Roundtrip über persistente Verbindung) ────

    [Benchmark(Description = "Hrpc WebSocket: 100x batch parallel")]
    [BenchmarkCategory("Hrpc")]
    public async Task<object?> HrpcWs_BatchParallel()
    {
        var multiRequest = new HrpcMultiRequest
        {
            Mode = ExecutionMode.Parallel,
            Requests = _batchRequests
        };
        return await _fixture.HrpcWebSocketClient.Call(multiRequest);
    }

    [Benchmark(Description = "Hrpc WebSocket: 100x batch serial")]
    [BenchmarkCategory("Hrpc")]
    public async Task<object?> HrpcWs_BatchSerial()
    {
        var multiRequest = new HrpcMultiRequest
        {
            Mode = ExecutionMode.Serial,
            Requests = _batchRequests.ToList()
        };
        return await _fixture.HrpcWebSocketClient.Call(multiRequest);
    }

    // ─── Hrpc SignalR (1 Roundtrip, MessagePack-Binär) ───────────────

    [Benchmark(Description = "Hrpc SignalR: 100x batch parallel")]
    [BenchmarkCategory("Hrpc")]
    public async Task<object?> HrpcSignalR_BatchParallel()
    {
        var multiRequest = new HrpcMultiRequest
        {
            Mode = ExecutionMode.Parallel,
            Requests = _batchRequests
        };
        return await _fixture.HrpcSignalrClient.Call(multiRequest);
    }

    [Benchmark(Description = "Hrpc SignalR: 100x batch serial")]
    [BenchmarkCategory("Hrpc")]
    public async Task<object?> HrpcSignalR_BatchSerial()
    {
        var multiRequest = new HrpcMultiRequest
        {
            Mode = ExecutionMode.Serial,
            Requests = _batchRequests.ToList()
        };
        return await _fixture.HrpcSignalrClient.Call(multiRequest);
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