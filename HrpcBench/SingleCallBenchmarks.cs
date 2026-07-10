using BenchmarkDotNet.Attributes;
using HrpcCommon.Models;

namespace HrpcBench;

/// <summary>
/// Single-Call comparison: REST vs Hrpc REST vs Hrpc WebSocket vs Hrpc SignalR.
/// Uses a real Kestrel server for WebSocket/SignalR (needs TCP connections).
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
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

    // ─── Hrpc REST (HTTP/JSON via Hrpc) ────────────────────────────

    [Benchmark(Description = "Hrpc REST: GetAllCustomers")]
    [BenchmarkCategory("Hrpc")]
    public async Task<object?> HrpcRest_GetAllCustomers()
    {
        var request = new HrpcRequest
        {
            Controller = "Customer",
            Method = "GetAllCustomers",
            StringData = "[]",
            Id = "Customer.GetAllCustomers"
        };
        return await _fixture.HrpcRestClient.Call(request);
    }

    [Benchmark(Description = "Hrpc REST: GetCustomerById")]
    [BenchmarkCategory("Hrpc")]
    public async Task<object?> HrpcRest_GetCustomerById()
    {
        var request = new HrpcRequest
        {
            Controller = "Customer",
            Method = "GetCustomerById",
            StringData = "[{\"" + "ParameterName" + "\":\"" + "id" + "\",\"" + "Data" + "\":\"" + "1" + "\"}]",
            Id = "Customer.GetCustomerById"
        };
        return await _fixture.HrpcRestClient.Call(request);
    }

    // ─── Hrpc WebSocket (persistent connection, no HTTP overhead) ──

    [Benchmark(Description = "Hrpc WebSocket: GetAllCustomers")]
    [BenchmarkCategory("Hrpc")]
    public async Task<object?> HrpcWs_GetAllCustomers()
    {
        var request = new HrpcRequest
        {
            Controller = "Customer",
            Method = "GetAllCustomers",
            StringData = "[]",
            Id = "Customer.GetAllCustomers"
        };
        return await _fixture.HrpcWebSocketClient.Call(request);
    }

    [Benchmark(Description = "Hrpc WebSocket: GetCustomerById")]
    [BenchmarkCategory("Hrpc")]
    public async Task<object?> HrpcWs_GetCustomerById()
    {
        var request = new HrpcRequest
        {
            Controller = "Customer",
            Method = "GetCustomerById",
            StringData = "[{\"" + "ParameterName" + "\":\"" + "id" + "\",\"" + "Data" + "\":\"" + "1" + "\"}]",
            Id = "Customer.GetCustomerById"
        };
        return await _fixture.HrpcWebSocketClient.Call(request);
    }

    // ─── Hrpc SignalR (persistent connection, MessagePack) ─────────

    [Benchmark(Description = "Hrpc SignalR: GetAllCustomers")]
    [BenchmarkCategory("Hrpc")]
    public async Task<object?> HrpcSignalR_GetAllCustomers()
    {
        var request = new HrpcRequest
        {
            Controller = "Customer",
            Method = "GetAllCustomers",
            StringData = "[]",
            Id = "Customer.GetAllCustomers"
        };
        return await _fixture.HrpcSignalrClient.Call(request);
    }

    [Benchmark(Description = "Hrpc SignalR: GetCustomerById")]
    [BenchmarkCategory("Hrpc")]
    public async Task<object?> HrpcSignalR_GetCustomerById()
    {
        var request = new HrpcRequest
        {
            Controller = "Customer",
            Method = "GetCustomerById",
            StringData = "[{\"" + "ParameterName" + "\":\"" + "id" + "\",\"" + "Data" + "\":\"" + "1" + "\"}]",
            Id = "Customer.GetCustomerById"
        };
        return await _fixture.HrpcSignalrClient.Call(request);
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
