using BenchmarkDotNet.Attributes;
using HrpcCommon.Models;

namespace HrpcBench;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
[HideColumns("Error", "StdDev")]
public class BatchCallBenchmarks
{
    private BenchmarkFixture _fixture = null!;
    private List<HrpcRequest> _batchRequests = null!;
    private const int BatchSize = 10;

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

    [Benchmark(Description = "REST: 10x sequential GetCustomerById")]
    public async Task Rest_Sequential10()
    {
        for (int i = 1; i <= BatchSize; i++)
            await _fixture.RestGetCustomerById(i);
    }

    [Benchmark(Description = "REST: 10x parallel GetCustomerById")]
    public async Task Rest_Parallel10()
    {
        var tasks = Enumerable.Range(1, BatchSize).Select(i => _fixture.RestGetCustomerById(i));
        await Task.WhenAll(tasks);
    }

    [Benchmark(Description = "Hrpc REST: 10x batch parallel")]
    public async Task<object?> HrpcRest_BatchParallel()
    {
        var multiRequest = new HrpcMultiRequest
        {
            Mode = ExecutionMode.Parallel,
            Requests = _batchRequests
        };
        return await _fixture.HrpcRestClient.Call(multiRequest);
    }

    [Benchmark(Description = "Hrpc REST: 10x batch serial")]
    public async Task<object?> HrpcRest_BatchSerial()
    {
        var multiRequest = new HrpcMultiRequest
        {
            Mode = ExecutionMode.Serial,
            Requests = _batchRequests.ToList()
        };
        return await _fixture.HrpcRestClient.Call(multiRequest);
    }

    [Benchmark(Description = "Hrpc WebSocket: 10x batch parallel")]
    public async Task<object?> HrpcWs_BatchParallel()
    {
        var multiRequest = new HrpcMultiRequest
        {
            Mode = ExecutionMode.Parallel,
            Requests = _batchRequests
        };
        return await _fixture.HrpcWebSocketClient.Call(multiRequest);
    }

    [Benchmark(Description = "Hrpc WebSocket: 10x batch serial")]
    public async Task<object?> HrpcWs_BatchSerial()
    {
        var multiRequest = new HrpcMultiRequest
        {
            Mode = ExecutionMode.Serial,
            Requests = _batchRequests.ToList()
        };
        return await _fixture.HrpcWebSocketClient.Call(multiRequest);
    }

    [Benchmark(Description = "Hrpc SignalR: 10x batch parallel")]
    public async Task<object?> HrpcSignalR_BatchParallel()
    {
        var multiRequest = new HrpcMultiRequest
        {
            Mode = ExecutionMode.Parallel,
            Requests = _batchRequests
        };
        return await _fixture.HrpcSignalrClient.Call(multiRequest);
    }
}
