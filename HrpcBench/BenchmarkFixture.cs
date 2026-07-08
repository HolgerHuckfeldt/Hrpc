using Hrpc.Grpc;
using Hrpc.Hrpc;
using Hrpc.Services;
using HrpcClient.Hrpc;
using HrpcHub.Extensions;
using HrpcRest;
using HrpcWebSocket;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using System.Text.Json;

namespace HrpcBench;

/// <summary>
/// Shared fixture that starts a real Kestrel server for WebSocket/SignalR benchmarks
/// and a TestServer for REST/Hrpc-REST benchmarks.
/// </summary>
public class BenchmarkFixture
{
    private TestServer _testServer = null!;
    private Microsoft.AspNetCore.Hosting.IWebHost? _realServer;
    private int _port;

    public HttpClient RestClient { get; private set; } = null!;
    public HrpcRestJsonClient HrpcRestClient { get; private set; } = null!;
    public HrpcWebSocketClient HrpcWebSocketClient { get; private set; } = null!;
    public HrpcSignalrClient HrpcSignalrClient { get; private set; } = null!;
    public CustomerService CustomerService { get; private set; } = null!;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string ServerUrl { get; private set; } = string.Empty;

    public void Initialize()
    {
        // Find a free port for the real server
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        _port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        ServerUrl = $"http://localhost:{_port}/";

        // Start a REAL Kestrel server for WebSocket/SignalR (needs real TCP connections)
        _realServer = Microsoft.AspNetCore.WebHost.CreateDefaultBuilder()
            .UseUrls(ServerUrl)
            .ConfigureServices(services =>
            {
                services.AddControllers()
                    .AddApplicationPart(typeof(Hrpc.Hrpc.CustomerHandler).Assembly);
                services.AddEndpointsApiExplorer();
                services.AddSwaggerGen();

                services.AddCors(options =>
                {
                    options.AddDefaultPolicy(policy => policy
                        .AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
                });

                services.AddHrpc(new HrpcHub.Extensions.HrpcOptions
                {
                    EnableDetailedErrors = true,
                    UseMessagePack = true,
                    UseSignalR = true,
                    MaximumParallelInvocationsPerClient = 100,
                    MaximumReceiveMessageSize = 102400,
                    StreamBufferCapacity = 100
                });

                services.AddSingleton<CustomerService>();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseCors();
                app.UseAuthorization();
                app.UseHrpc();
                app.UseWebSockets();
                app.UseHrpcWebSocket("/hrpcws");
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapControllers();
                    endpoints.MapHrpcEndpoints();
                    endpoints.MapHub<HrpcHub.Hub.HrpcHub>("/Hrpc");
                });
            })
            .Build();

        _realServer.Start();

        // Use the real server's HTTP client for REST and Hrpc REST
        RestClient = new HttpClient { BaseAddress = new Uri(ServerUrl) };
        CustomerService = _realServer.Services.GetRequiredService<CustomerService>();
        HrpcRestClient = new HrpcRestJsonClient(ServerUrl, RestClient);

        // Connect WebSocket client
        HrpcWebSocketClient = new HrpcWebSocketClient(ServerUrl);
        HrpcWebSocketClient.ConnectAsync().GetAwaiter().GetResult();

        // Connect SignalR client
        HrpcSignalrClient = new HrpcSignalrClient(ServerUrl);

        // Seed data
        SeedData().GetAwaiter().GetResult();
    }

    private async Task SeedData()
    {
        for (int i = 0; i < 100; i++)
        {
            await CustomerService.AddCustomer($"Customer-{i}");
        }
    }

    public async Task<List<Hrpc.Model.Customer>?> RestGetAllCustomers()
    {
        var response = await RestClient.GetAsync("api/customer/all");
        response.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<List<Hrpc.Model.Customer>>(await response.Content.ReadAsStringAsync(), JsonOpts);
    }

    public async Task<Hrpc.Model.Customer?> RestGetCustomerById(int id)
    {
        var response = await RestClient.GetAsync($"api/customer/{id}");
        response.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<Hrpc.Model.Customer>(await response.Content.ReadAsStringAsync(), JsonOpts);
    }

    public async Task<List<Hrpc.Model.Order>?> RestGetOrders(int orderId)
    {
        var response = await RestClient.GetAsync($"api/customer/{orderId}/orders");
        response.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<List<Hrpc.Model.Order>>(await response.Content.ReadAsStringAsync(), JsonOpts);
    }

    public async Task<int> RestAddCustomer(string name)
    {
        var json = JsonSerializer.Serialize(new { Name = name }, JsonOpts);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await RestClient.PostAsync("api/customer", content);
        response.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<int>(await response.Content.ReadAsStringAsync(), JsonOpts);
    }

    public void Dispose()
    {
        HrpcWebSocketClient?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        HrpcRestClient?.Dispose();
        RestClient?.Dispose();
        _realServer?.Dispose();
    }
}
