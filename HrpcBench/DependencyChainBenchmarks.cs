using BenchmarkDotNet.Attributes;
using HrpcCommon.Models;
using System.Text.Json;

namespace HrpcBench;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
[HideColumns("Error", "StdDev")]
public class DependencyChainBenchmarks
{
    private BenchmarkFixture _fixture = null!;

    [GlobalSetup]
    public void Setup()
    {
        _fixture = new BenchmarkFixture();
        _fixture.Initialize();
    }

    [GlobalCleanup]
    public void Cleanup() => _fixture.Dispose();

    [Benchmark(Description = "REST: 3 sequential calls (N+1 pattern)")]
    public async Task<object?> Rest_ThreeSequentialCalls()
    {
        var customerId = await _fixture.RestAddCustomer("Bench-Chain");
        var customer = await _fixture.RestGetCustomerById(customerId);
        if (customer == null) return null;
        return await _fixture.RestGetOrders(customer.OrderId);
    }

    [Benchmark(Description = "Hrpc: 1 batch call (dependency chain, 3 reqs)")]
    public async Task<object?> Hrpc_DependencyChain_SingleBatch()
    {
        var multiRequest = new HrpcMultiRequest
        {
            Mode = ExecutionMode.Serial,
            Requests = new List<HrpcRequest>
            {
                new HrpcRequest
                {
                    Controller = "Customer",
                    Method = "AddCustomer",
                    StringData = "[{\"ParameterName\":\"name\",\"Data\":\"\\\"Bench-Hrpc\\\"\"}]",
                    Id = "step1",
                    DependencyMapping = new Dictionary<string, string>
                    {
                        { "newCustomerId", "$" }
                    }
                },
                new HrpcRequest
                {
                    Controller = "Customer",
                    Method = "GetCustomerById",
                    StringData = "[{\"ParameterName\":\"id\",\"Data\":\"@newCustomerId\"}]",
                    Id = "step2",
                    DependencyMapping = new Dictionary<string, string>
                    {
                        { "orderId", "$.OrderId" }
                    }
                },
                new HrpcRequest
                {
                    Controller = "Customer",
                    Method = "GetOrderByOrderId",
                    StringData = "[{\"ParameterName\":\"id\",\"Data\":\"@orderId\"}]",
                    Id = "step3"
                }
            }
        };
        return await _fixture.HrpcRestClient.Call(multiRequest);
    }

    [Benchmark(Description = "Hrpc WebSocket: 1 batch call (dependency chain)")]
    public async Task<object?> HrpcWs_DependencyChain_SingleBatch()
    {
        var multiRequest = new HrpcMultiRequest
        {
            Mode = ExecutionMode.Serial,
            Requests = new List<HrpcRequest>
            {
                new HrpcRequest
                {
                    Controller = "Customer",
                    Method = "AddCustomer",
                    StringData = "[{\"ParameterName\":\"name\",\"Data\":\"\\\"Bench-Ws\\\"\"}]",
                    Id = "step1",
                    DependencyMapping = new Dictionary<string, string>
                    {
                        { "newCustomerId", "$" }
                    }
                },
                new HrpcRequest
                {
                    Controller = "Customer",
                    Method = "GetCustomerById",
                    StringData = "[{\"ParameterName\":\"id\",\"Data\":\"@newCustomerId\"}]",
                    Id = "step2",
                    DependencyMapping = new Dictionary<string, string>
                    {
                        { "orderId", "$.OrderId" }
                    }
                },
                new HrpcRequest
                {
                    Controller = "Customer",
                    Method = "GetOrderByOrderId",
                    StringData = "[{\"ParameterName\":\"id\",\"Data\":\"@orderId\"}]",
                    Id = "step3"
                }
            }
        };
        return await _fixture.HrpcWebSocketClient.Call(multiRequest);
    }

    [Benchmark(Description = "Hrpc: 3 sequential single calls (no chaining)")]
    public async Task<object?> Hrpc_ThreeSequentialCalls_NoChaining()
    {
        var resp1 = await _fixture.HrpcRestClient.Call(new HrpcRequest
        {
            Controller = "Customer",
            Method = "AddCustomer",
            StringData = "[{\"ParameterName\":\"name\",\"Data\":\"\\\"Bench-Seq\\\"\"}]",
            Id = "s1"
        });
        var customerId = JsonDocument.Parse(resp1!.Data!).RootElement.GetRawText();

        var resp2 = await _fixture.HrpcRestClient.Call(new HrpcRequest
        {
            Controller = "Customer",
            Method = "GetCustomerById",
            StringData = "[{\"ParameterName\":\"id\",\"Data\":" + customerId + "}]",
            Id = "s2"
        });
        using var doc = JsonDocument.Parse(resp2!.Data!);
        var orderId = doc.RootElement.GetProperty("orderId").GetRawText();

        var resp3 = await _fixture.HrpcRestClient.Call(new HrpcRequest
        {
            Controller = "Customer",
            Method = "GetOrderByOrderId",
            StringData = "[{\"ParameterName\":\"id\",\"Data\":" + orderId + "}]",
            Id = "s3"
        });
        return resp3;
    }
}
