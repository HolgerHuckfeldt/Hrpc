using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using System.Timers;
using HrpcClient.Hrpc.Exceptions;
using System.Threading;

namespace HrpcClient.Hrpc;

/// <summary>
/// Ein HRPC-Client, der über SignalR mit dem Server kommuniziert.
/// Unterstützt automatische Wiederverbindung und asynchrone Einzel- und Multi-Requests.
/// </summary>
public class HrpcSignalrClient : HrpcClientBase, IHrpcClient
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
                //options.AccessTokenProvider = () => Task.FromResult(_jwtToken);
            })
            .AddMessagePackProtocol(
                //options => options.SerializerOptions = MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray)
                )
            .WithAutomaticReconnect(reconnectTimes.ToArray())
            .Build();
    }

    /// <summary>
    /// Sendet einen einzelnen HrpcRequest an den Server und gibt die Antwort zurück.
    /// </summary>
    /// <param name="request">Der HrpcRequest.</param>
    /// <param name="ct">CancellationToken für Abbruch.</param>
    /// <returns>Die Antwort des Servers als HrpcResponse.</returns>
    public override async Task<HrpcResponse?> Call(HrpcRequest? request, CancellationToken ct = default)
    {
        if (request == null)
        {
            return null;
        }

        if (!_connected)
        {
            var t = await Connect();
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
    /// <param name="request">Der Multi-Request.</param>
    /// <param name="ct">CancellationToken für Abbruch.</param>
    /// <returns>Die Antworten des Servers als Liste von HrpcResponse.</returns>
    public override async Task<IEnumerable<HrpcResponse?>?> Call(HrpcMultiRequest? request, CancellationToken ct = default)
    {
        if (request == null)
        {
            return null;
        }

        try
        {
            if (!_connected)
            {
                var t = await Connect();
            }

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
            throw new HrpcException("Fehler vom Server.", ex);
        }

        return new List<HrpcResponse?>();
    }

    /// <summary>
    /// Timer-Callback für erneuten Verbindungsaufbau.
    /// </summary>
    private async void NextTryTimerOnElapsed(object sender, ElapsedEventArgs e)
    {
        await Connect();
    }

    /// <summary>
    /// Baut die Verbindung zum Server auf und registriert Event-Handler.
    /// </summary>
    /// <returns>True, wenn die Verbindung erfolgreich war.</returns>
    private async Task<bool> Connect()
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

        try
        {
            await _connection.StartAsync();
            _nextTryTimer.Stop();
            _connected = true;
        }
        catch (HttpRequestException)
        {
            _nextTryTimer.Start();
        }
        catch (Exception ex)
        {
            throw new HrpcException("Error connecting to server via SignalR", ex);
        }

        return true;
    }

    /// <summary>
    /// Wird aufgerufen, wenn die Verbindung erfolgreich wiederhergestellt wurde.
    /// </summary>
    private Task ConnectionOnReconnected(string? arg)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Wird aufgerufen, wenn die Verbindung gerade wiederhergestellt wird.
    /// </summary>
    private Task ConnectionOnReconnecting(Exception? arg)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Wird aufgerufen, wenn die Verbindung geschlossen wurde.
    /// </summary>
    private Task ConnectionOnClosed(Exception? arg)
    {
        return Task.CompletedTask;
    }
}


