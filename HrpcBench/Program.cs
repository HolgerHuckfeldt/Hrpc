using HrpcBench;
using System.Diagnostics;

Console.WriteLine("Hrpc Performance Benchmarks (Custom Stopwatch)");
Console.WriteLine("===============================================");
Console.WriteLine();

var cmdArgs = Environment.GetCommandLineArgs();
var benchArg = cmdArgs.Skip(1).FirstOrDefault(a => !a.StartsWith("-") && !a.Contains("HrpcBench") && !a.Contains("dotnet") && !a.Contains("run") && !a.Contains("Release"));

var fixture = new BenchmarkFixture();
fixture.Initialize();

try
{
    switch (benchArg?.ToLowerInvariant())
    {
        case "single":
            await RunSingleCallBenchmarks(fixture);
            break;
        case "batch":
            await RunBatchCallBenchmarks(fixture);
            break;
        case "dependency":
        case "chain":
            await RunDependencyChainBenchmarks(fixture);
            break;
        default:
            await RunSingleCallBenchmarks(fixture);
            await RunBatchCallBenchmarks(fixture);
            await RunDependencyChainBenchmarks(fixture);
            break;
    }
}
finally
{
    fixture.Dispose();
}

Console.WriteLine();
Console.WriteLine("Done!");

static async Task RunSingleCallBenchmarks(BenchmarkFixture f)
{
    Console.WriteLine("=== Single-Call Benchmarks ===");
    Console.WriteLine();

    const int iterations = 1000;
    const int warmup = 100;

    // Warmup
    Console.WriteLine($"Warmup ({warmup} iterations)...");
    for (int i = 0; i < warmup; i++)
    {
        await f.RestGetAllCustomers();
        await f.HrpcRestClient.Call(new HrpcCommon.Models.HrpcRequest { Controller = "Customer", Method = "GetAllCustomers", StringData = "[]", Id = "warmup" });
        await f.HrpcWebSocketClient.Call(new HrpcCommon.Models.HrpcRequest { Controller = "Customer", Method = "GetAllCustomers", StringData = "[]", Id = "warmup" });
        try { await f.HrpcSignalrClient.Call(new HrpcCommon.Models.HrpcRequest { Controller = "Customer", Method = "GetAllCustomers", StringData = "[]", Id = "warmup" }); } catch { }
    }
    Console.WriteLine();

    // REST GetAllCustomers
    var sw = Stopwatch.StartNew();
    for (int i = 0; i < iterations; i++) await f.RestGetAllCustomers();
    sw.Stop();
    PrintResult("REST: GetAllCustomers", sw.Elapsed.TotalMilliseconds / iterations * 1000, iterations);

    // REST GetCustomerById
    sw.Restart();
    for (int i = 0; i < iterations; i++) await f.RestGetCustomerById(1);
    sw.Stop();
    PrintResult("REST: GetCustomerById", sw.Elapsed.TotalMilliseconds / iterations * 1000, iterations);

    // Hrpc REST GetAllCustomers
    sw.Restart();
    for (int i = 0; i < iterations; i++)
        await f.HrpcRestClient.Call(new HrpcCommon.Models.HrpcRequest { Controller = "Customer", Method = "GetAllCustomers", StringData = "[]", Id = "bench" });
    sw.Stop();
    PrintResult("Hrpc REST: GetAllCustomers", sw.Elapsed.TotalMilliseconds / iterations * 1000, iterations);

    // Hrpc REST GetCustomerById
    sw.Restart();
    for (int i = 0; i < iterations; i++)
        await f.HrpcRestClient.Call(new HrpcCommon.Models.HrpcRequest { Controller = "Customer", Method = "GetCustomerById", StringData = "[{\"ParameterName\":\"id\",\"Data\":\"1\"}]", Id = "bench" });
    sw.Stop();
    PrintResult("Hrpc REST: GetCustomerById", sw.Elapsed.TotalMilliseconds / iterations * 1000, iterations);

    // Hrpc WebSocket GetAllCustomers
    sw.Restart();
    for (int i = 0; i < iterations; i++)
        await f.HrpcWebSocketClient.Call(new HrpcCommon.Models.HrpcRequest { Controller = "Customer", Method = "GetAllCustomers", StringData = "[]", Id = "bench" });
    sw.Stop();
    PrintResult("Hrpc WebSocket: GetAllCustomers", sw.Elapsed.TotalMilliseconds / iterations * 1000, iterations);

    // Hrpc WebSocket GetCustomerById
    sw.Restart();
    for (int i = 0; i < iterations; i++)
        await f.HrpcWebSocketClient.Call(new HrpcCommon.Models.HrpcRequest { Controller = "Customer", Method = "GetCustomerById", StringData = "[{\"ParameterName\":\"id\",\"Data\":\"1\"}]", Id = "bench" });
    sw.Stop();
    PrintResult("Hrpc WebSocket: GetCustomerById", sw.Elapsed.TotalMilliseconds / iterations * 1000, iterations);

    // Hrpc SignalR GetAllCustomers (if connected)
    try
    {
        sw.Restart();
        for (int i = 0; i < iterations; i++)
            await f.HrpcSignalrClient.Call(new HrpcCommon.Models.HrpcRequest { Controller = "Customer", Method = "GetAllCustomers", StringData = "[]", Id = "bench" });
        sw.Stop();
        PrintResult("Hrpc SignalR: GetAllCustomers", sw.Elapsed.TotalMilliseconds / iterations * 1000, iterations);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Hrpc SignalR: GetAllCustomers -> FAILED: {ex.Message}");
    }

    Console.WriteLine();
}

static async Task RunBatchCallBenchmarks(BenchmarkFixture f)
{
    Console.WriteLine("=== Batch-Call Benchmarks (10x GetCustomerById) ===");
    Console.WriteLine();

    const int iterations = 500;
    const int batchSize = 10;

    var batchRequests = new List<HrpcCommon.Models.HrpcRequest>();
    for (int i = 1; i <= batchSize; i++)
        batchRequests.Add(new HrpcCommon.Models.HrpcRequest { Controller = "Customer", Method = "GetCustomerById", StringData = "[{\"ParameterName\":\"id\",\"Data\":\"" + i + "\"}]", Id = "req-" + i });

    // REST sequential
    var sw = Stopwatch.StartNew();
    for (int iter = 0; iter < iterations; iter++)
        for (int i = 1; i <= batchSize; i++) await f.RestGetCustomerById(i);
    sw.Stop();
    PrintResult("REST: 10x sequential", sw.Elapsed.TotalMilliseconds / iterations * 1000, iterations);

    // REST parallel
    sw.Restart();
    for (int iter = 0; iter < iterations; iter++)
        await Task.WhenAll(Enumerable.Range(1, batchSize).Select(i => f.RestGetCustomerById(i)));
    sw.Stop();
    PrintResult("REST: 10x parallel", sw.Elapsed.TotalMilliseconds / iterations * 1000, iterations);

    // Hrpc REST batch parallel
    sw.Restart();
    for (int iter = 0; iter < iterations; iter++)
        await f.HrpcRestClient.Call(new HrpcCommon.Models.HrpcMultiRequest { Mode = HrpcCommon.Models.ExecutionMode.Parallel, Requests = batchRequests });
    sw.Stop();
    PrintResult("Hrpc REST: 10x batch parallel", sw.Elapsed.TotalMilliseconds / iterations * 1000, iterations);

    // Hrpc REST batch serial
    sw.Restart();
    for (int iter = 0; iter < iterations; iter++)
        await f.HrpcRestClient.Call(new HrpcCommon.Models.HrpcMultiRequest { Mode = HrpcCommon.Models.ExecutionMode.Serial, Requests = batchRequests.ToList() });
    sw.Stop();
    PrintResult("Hrpc REST: 10x batch serial", sw.Elapsed.TotalMilliseconds / iterations * 1000, iterations);

    // Hrpc WebSocket batch
    sw.Restart();
    for (int iter = 0; iter < iterations; iter++)
        await f.HrpcWebSocketClient.Call(new HrpcCommon.Models.HrpcMultiRequest { Mode = HrpcCommon.Models.ExecutionMode.Parallel, Requests = batchRequests });
    sw.Stop();
    PrintResult("Hrpc WebSocket: 10x batch", sw.Elapsed.TotalMilliseconds / iterations * 1000, iterations);

    Console.WriteLine();
}

static async Task RunDependencyChainBenchmarks(BenchmarkFixture f)
{
    Console.WriteLine("=== Dependency-Chain Benchmarks (3 dependent calls) ===");
    Console.WriteLine();

    const int iterations = 100;

    // REST: 3 sequential calls
    var sw = Stopwatch.StartNew();
    for (int i = 0; i < iterations; i++)
    {
        var customerId = await f.RestAddCustomer("Bench");
        var customer = await f.RestGetCustomerById(customerId);
        if (customer != null) await f.RestGetOrders(customer.OrderId);
    }
    sw.Stop();
    PrintResult("REST: 3 sequential (N+1)", sw.Elapsed.TotalMilliseconds / iterations * 1000, iterations);

    // Hrpc REST: 1 batch with dependency chain
    var chainRequest = new HrpcCommon.Models.HrpcMultiRequest
    {
        Mode = HrpcCommon.Models.ExecutionMode.Serial,
        Requests = new List<HrpcCommon.Models.HrpcRequest>
        {
            new() { Controller = "Customer", Method = "AddCustomer", StringData = "[{\"ParameterName\":\"name\",\"Data\":\"\\\"Bench\\\"\"}]", Id = "s1", DependencyMapping = new() { { "cid", "$" } } },
            new() { Controller = "Customer", Method = "GetCustomerById", StringData = "[{\"ParameterName\":\"id\",\"Data\":\"@cid\"}]", Id = "s2", DependencyMapping = new() { { "oid", "$.OrderId" } } },
            new() { Controller = "Customer", Method = "GetOrderByOrderId", StringData = "[{\"ParameterName\":\"id\",\"Data\":\"@oid\"}]", Id = "s3" }
        }
    };

    sw.Restart();
    for (int i = 0; i < iterations; i++)
        await f.HrpcRestClient.Call(chainRequest);
    sw.Stop();
    PrintResult("Hrpc REST: 1 batch (dependency chain)", sw.Elapsed.TotalMilliseconds / iterations * 1000, iterations);

    // Hrpc WebSocket: 1 batch with dependency chain
    sw.Restart();
    for (int i = 0; i < iterations; i++)
        await f.HrpcWebSocketClient.Call(chainRequest);
    sw.Stop();
    PrintResult("Hrpc WebSocket: 1 batch (dependency chain)", sw.Elapsed.TotalMilliseconds / iterations * 1000, iterations);

    Console.WriteLine();
}

static void PrintResult(string name, double meanMicroseconds, int iterations)
{
    Console.WriteLine($"{name,-45} {meanMicroseconds,10:F2} us  ({iterations} iterations)");
}
