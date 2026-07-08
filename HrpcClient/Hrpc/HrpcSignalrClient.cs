using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using System.Timers;
// HrpcException is now provided via global using alias from GlobalUsings.cs
using System.Threading;

namespace HrpcClient.Hrpc;

/// <summary>
/// Ein HRPC-Client, der über SignalR mit dem Server kommuniziert.
/// Unterstützt automatische Wiederverbindung und asynchrone Einzel- und Multi-Requests.
/// </summary>
public class HrpcSignalrClient : HrpcClientBase, IHrpcClient, IAsyncDisposable
{
    /// <summary>
    /// Timer für automatische Reconnect-Versuche bei Verbindungsabbruch.
    /// </summary>
    private readonly System.Timers.Timer _nextTryTimer;
    /// <summary>
    /// Die SignalR HubConnection zum Server.
    /// </summary>
    private readonly HubConnection _connection;
    private bool _connected;
    private bool _disposed;
    private bool _handlersRegistered;
    private string _jwtToken = String.Empty;

    /// <summary>
    /// Erstellt einen neuen SignalR-Client mit JWT-Token für Authentifizierung.
    /// </summary>
    /// <param name="server">Die Basis-URL des Servers.</param>
    /// <param name="bearer">Das JWT-Token für die Authentifizierung.</param>
    public HrpcSignalrClient(string server, string bearer)
    : this(server)
    {
        _jwtToken = bearer;
    }

    /// <summary>
    /// Erstellt einen neuen SignalR-Client.
    /// </summary>
    /// <param name="server">Die Basis-URL des Servers.</param>
    public HrpcSignalrClient(string server)
    {
        string baseUrl = server;
        if (baseUrl.EndsWith("/"))
            baseUrl = baseUrl.Remove(baseUrl.Length - 1);

        if (!baseUrl.EndsWith("/"))
            baseUrl += "/";

        _nextTryTimer = new System.Timers.Timer();
        _nextTryTimer.Interval = TimeSpan.FromMinutes(2).TotalMilliseconds;
        _nextTryTimer.AutoReset = true;
        _nextTryTimer.Elapsed += NextTryTimerOnElapsed;

        var reconnectTimes = new List<TimeSpan>()
                    {
                        TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2),
                        TimeSpan.FromSeconds(5),TimeSpan.FromSeconds(5),TimeSpan.FromSeconds(5),TimeSpan.FromSeconds(5),
                        TimeSpan.FromSeconds(10),TimeSpan.FromSeconds(10),TimeSpan.FromSeconds(10),TimeSpan.FromSeconds(10),
                        TimeSpan.FromSeconds(30),TimeSpan.FromSeconds(30),TimeSpan.FromSeconds(30),TimeSpan.FromSeconds(30),
                        TimeSpan.FromSeconds(60),TimeSpan.FromSeconds(60),TimeSpan.FromSeconds(60),TimeSpan.FromSeconds(60),
                        TimeSpan.FromMinutes(5),TimeSpan.FromMinutes(5),TimeSpan.FromMinutes(5),TimeSpan.FromMinutes(5),
                        TimeSpan.FromMinutes(10),TimeSpan.FromMinutes(10),TimeSpan.FromMinutes(10),TimeSpan.FromMinutes(10),
                        TimeSpan.FromMinutes(10),TimeSpan.FromMinutes(10),TimeSpan.FromMinutes(10),TimeSpan.FromMinutes(10),
                        TimeSpan.FromMinutes(10),TimeSpan.FromMinutes(10),TimeSpan.FromMinutes(10),TimeSpan.FromMinutes(10),
                        TimeSpan.FromMinutes(10),TimeSpan.FromMinutes(10),TimeSpan.FromMinutes(10),TimeSpan.FromMinutes(10),
                        TimeSpan.FromMinutes(30),TimeSpan.FromMinutes(30),TimeSpan.FromMinutes(30),TimeSpan.FromMinutes(30),
                        TimeSpan.FromMinutes(30),TimeSpan.FromMinutes(30),TimeSpan.FromMinutes(30),TimeSpan.FromMinutes(30),
                        TimeSpan.FromMinutes(30),TimeSpan.FromMinutes(30),TimeSpan.FromMinutes(30),TimeSpan.FromMinutes(30),
                        TimeSpan.FromMinutes(60),TimeSpan.FromMinutes(60),TimeSpan.FromMinutes(60),TimeSpan.FromMinutes(60),
                        TimeSpan.FromMinutes(60),TimeSpan.FromMinutes(60),TimeSpan.FromMinutes(60),TimeSpan.FromMinutes(60)
                    };

        _connection = new HubConnectionBuilder()
            .WithUrl($"{baseUrl}Hrpc", options =>
            {
                if (!string.IsNullOrEmpty(_jwtToken))
                {
                    options.AccessTokenProvider = () => Task.FromResult(_jwtToken)!;
                }
            })
            .AddMessagePackProtocol()
            .WithAutomaticReconnect(reconnectTimes.ToArray())
            .Build();
    }

    /// <summary>
    /// Sendet einen einzelnen HrpcRequest an den Server und gibt die Antwort zurück.
    /// </summary>
    public override async Task<HrpcResponse?> Call(HrpcRequest? request, CancellationToken ct = default)
    {
        if (request == null)
            return null;

        if (!_connected)
        {
            var connected = await Connect();
            if (!connected)
                throw new HrpcException("Not connected to server.");
        }

        if (await _connection.InvokeCoreAsync(
                "DoWork",
                typeof(HrpcResponse),
                new object[] { request },
                ct
            ) is HrpcResponse r)
        {
            return r;
        }

        return null;
    }

    /// <summary>
    /// Sendet mehrere HrpcRequests (Batch) an den Server und gibt die Antworten zurück.
    /// </summary>
    public override async Task<IEnumerable<HrpcResponse?>?> Call(HrpcMultiRequest? request, CancellationToken ct = default)
    {
        if (request == null)
            return null;

        if (!_connected)
        {
            var connected = await Connect();
            if (!connected)
                throw new HrpcException("Not connected to server.");
        }

        try
        {
            if (await _connection.InvokeCoreAsync(
                    "DoWorkMany",
                    typeof(IEnumerable<HrpcResponse?>),
                    new object[] { request },
                    ct
                ) is IEnumerable<HrpcResponse?> r)
            {
                return r;
            }
        }
        catch (Exception ex)
        {
            throw new HrpcException("Error from server.", ex);
        }

        return new List<HrpcResponse?>();
    }

    /// <summary>
    /// Timer-Callback für erneuten Verbindungsaufbau.
    /// </summary>
    private async void NextTryTimerOnElapsed(object? sender, ElapsedEventArgs e)
    {
        try
        {
            await Connect();
        }
        catch
        {
            // Errors are logged inside Connect(); avoid async void crash
        }
    }

    /// <summary>
    /// Baut die Verbindung zum Server auf und registriert Event-Handler.
    /// </summary>
    /// <returns>True, wenn die Verbindung erfolgreich war.</returns>
    private async Task<bool> Connect()
    {
        // Register handlers only once to prevent duplicates on reconnect
        if (!_handlersRegistered)
        {
            _connection.On<string, string>("ReceiveMessage", (user, message) =>
            {
                string msg = message;
            });

            _connection.On<string, byte[]>("ReceiveData", (user, data) =>
            {
                byte[] mydata = data;
            });

            _connection.Closed += ConnectionOnClosed;
            _connection.Reconnecting += ConnectionOnReconnecting;
            _connection.Reconnected += ConnectionOnReconnected;
            _handlersRegistered = true;
        }

        try
        {
            await _connection.StartAsync();
            _nextTryTimer.Stop();
            _connected = true;
            return true;
        }
        catch (HttpRequestException)
        {
            _nextTryTimer.Start();
            return false;
        }
        catch (Exception ex)
        {
            throw new HrpcException("Error connecting to server via SignalR", ex);
        }
    }

    /// <summary>
    /// Wird aufgerufen, wenn die Verbindung erfolgreich wiederhergestellt wurde.
    /// </summary>
    private Task ConnectionOnReconnected(string? arg)
    {
        _connected = true;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Wird aufgerufen, wenn die Verbindung gerade wiederhergestellt wird.
    /// </summary>
    private Task ConnectionOnReconnecting(Exception? arg)
    {
        _connected = false;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Wird aufgerufen, wenn die Verbindung geschlossen wurde.
    /// </summary>
    private Task ConnectionOnClosed(Exception? arg)
    {
        _connected = false;
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _nextTryTimer.Stop();
        _nextTryTimer.Elapsed -= NextTryTimerOnElapsed;
        _nextTryTimer.Dispose();

        if (_handlersRegistered)
        {
            _connection.Closed -= ConnectionOnClosed;
            _connection.Reconnecting -= ConnectionOnReconnecting;
            _connection.Reconnected -= ConnectionOnReconnected;
        }

        if (_connection.State == HubConnectionState.Connected)
        {
            await _connection.StopAsync();
        }

        await _connection.DisposeAsync();
    }
}
