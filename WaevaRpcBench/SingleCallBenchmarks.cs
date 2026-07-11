using BenchmarkDotNet.Attributes;
using WaevaRpcCommon.Models;

namespace WaevaRpcBench;

/// <summary>
/// Single-Call comparison: REST vs WaevaRpc REST vs WaevaRpc WebSocket vs WaevaRpc SignalR.
/// Uses a real Kestrel server for WebSocket/SignalR (needs TCP connections).
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 10, iterationCount: 15)]
[HideColumns("Error", "StdDev")]
public class SingleCallBenchmarks
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

    // ─── REST (HTTP/JSON) ──────────────────────────────────────────

    [Benchmark(Description = "REST: GetAllCustomers")]
    [BenchmarkCategory("REST")]
    public async Task<object?> Rest_GetAllCustomers()
    {
        return await _fixture.RestGetAllCustomers();
    }

    [Benchmark(Description = "REST: GetCustomerById")]
    [BenchmarkCategory("REST")]
    public async Task<object?> Rest_GetCustomerById()
    {
        return await _fixture.RestGetCustomerById(1);
    }

    // ─── WaevaRpc REST (HTTP/JSON via WaevaRpc) ────────────────────────────

    [Benchmark(Description = "WaevaRpc REST: GetAllCustomers")]
    [BenchmarkCategory("WaevaRpc")]
    public async Task<object?> WaevaRpcRest_GetAllCustomers()
    {
        var request = new WaevaRpcRequest
        {
            Controller = "Customer",
            Method = "GetAllCustomers",
            StringData = "[]",
            Id = "Customer.GetAllCustomers"
        };
        return await _fixture.WaevaRpcRestClient.Call(request);
    }

    [Benchmark(Description = "WaevaRpc REST: GetCustomerById")]
    [BenchmarkCategory("WaevaRpc")]
    public async Task<object?> WaevaRpcRest_GetCustomerById()
    {
        var request = new WaevaRpcRequest
        {
            Controller = "Customer",
            Method = "GetCustomerById",
            StringData = "[{\"" + "ParameterName" + "\":\"" + "id" + "\",\"" + "Data" + "\":\"" + "1" + "\"}]",
            Id = "Customer.GetCustomerById"
        };
        return await _fixture.WaevaRpcRestClient.Call(request);
    }

    // ─── WaevaRpc WebSocket (persistent connection, no HTTP overhead) ──

    [Benchmark(Description = "WaevaRpc WebSocket: GetAllCustomers")]
    [BenchmarkCategory("WaevaRpc")]
    public async Task<object?> WaevaRpcWs_GetAllCustomers()
    {
        var request = new WaevaRpcRequest
        {
            Controller = "Customer",
            Method = "GetAllCustomers",
            StringData = "[]",
            Id = "Customer.GetAllCustomers"
        };
        return await _fixture.WaevaRpcWebSocketClient.Call(request);
    }

    [Benchmark(Description = "WaevaRpc WebSocket: GetCustomerById")]
    [BenchmarkCategory("WaevaRpc")]
    public async Task<object?> WaevaRpcWs_GetCustomerById()
    {
        var request = new WaevaRpcRequest
        {
            Controller = "Customer",
            Method = "GetCustomerById",
            StringData = "[{\"" + "ParameterName" + "\":\"" + "id" + "\",\"" + "Data" + "\":\"" + "1" + "\"}]",
            Id = "Customer.GetCustomerById"
        };
        return await _fixture.WaevaRpcWebSocketClient.Call(request);
    }

    // ─── WaevaRpc SignalR (persistent connection, MessagePack) ─────────

    [Benchmark(Description = "WaevaRpc SignalR: GetAllCustomers")]
    [BenchmarkCategory("WaevaRpc")]
    public async Task<object?> WaevaRpcSignalR_GetAllCustomers()
    {
        var request = new WaevaRpcRequest
        {
            Controller = "Customer",
            Method = "GetAllCustomers",
            StringData = "[]",
            Id = "Customer.GetAllCustomers"
        };
        return await _fixture.WaevaRpcSignalrClient.Call(request);
    }

    [Benchmark(Description = "WaevaRpc SignalR: GetCustomerById")]
    [BenchmarkCategory("WaevaRpc")]
    public async Task<object?> WaevaRpcSignalR_GetCustomerById()
    {
        var request = new WaevaRpcRequest
        {
            Controller = "Customer",
            Method = "GetCustomerById",
            StringData = "[{\"" + "ParameterName" + "\":\"" + "id" + "\",\"" + "Data" + "\":\"" + "1" + "\"}]",
            Id = "Customer.GetCustomerById"
        };
        return await _fixture.WaevaRpcSignalrClient.Call(request);
    }

    // ─── Natives gRPC (Protobuf, HTTP/2) — Binär-Baseline ───────────

    [Benchmark(Description = "gRPC: GetAllCustomers")]
    [BenchmarkCategory("gRPC")]
    public async Task<object?> Grpc_GetAllCustomers()
    {
        return await _fixture.GrpcGetAllCustomers();
    }

    [Benchmark(Description = "gRPC: GetCustomerById")]
    [BenchmarkCategory("gRPC")]
    public async Task<object?> Grpc_GetCustomerById()
    {
        return await _fixture.GrpcGetCustomerById(1);
    }
}
