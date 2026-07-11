// ==============================================================================
// 01 — Single Call (C#-Client)
// ==============================================================================
// Ein einzelner RPC-Aufruf. Gezeigt für alle drei Transporte (REST, WebSocket,
// SignalR) — wähle je nach Anforderung. WebSocket ist der empfohlene primäre
// Kanal (persistent, geringe Latenz); REST ist zustandslos und am einfachsten;
// SignalR bietet MessagePack-Binary + Auto-Reconnect.
// ==============================================================================

using HrpcClient.Hrpc;
using HrpcCommon.Models;

// --- REST (zustandslos, IDisposable) ------------------------------------------
using var rest = new HrpcRestJsonClient("https://localhost:5001");

// Fluent Builder: verkettet Controller/Method/Parameter zu einem HrpcRequest.
var req = HrpcCall.Init("Customer", "GetCustomerById")
    .Param("id", 42)                 // named parameter — server bindet nach Name
    .ToRequest();

// Call<T> deserialisiert das Ergebnis direkt in T (Single-Pass). Bei Nicht-2xx
// wirft es HrpcException (Code + Message).
Customer? customer = await rest.Call<Customer>(req);

// --- WebSocket (persistent, primärer Kanal) -----------------------------------
// IAsyncDisposable. ConnectAsync() explizit — oder lazy beim ersten Call.
await using var ws = new HrpcWebSocketClient("wss://localhost:5001/hrpcws");
await ws.ConnectAsync();

// With(params object?[]) = positionsbasiert (Namen param0, param1, …). Server
// bindet positional, wenn der Name nicht passt — für Ein-Parameter-Methoden ok.
var req2 = HrpcCall.Init("Customer", "GetCustomerById")
    .With(42)
    .ToRequest();

Customer? customer2 = await ws.Call<Customer>(req2);

// --- SignalR (Binary via MessagePack, Auto-Reconnect) -------------------------
// useMessagePack=true = kompakter Wire-Format. Bearer optional.
await using var signalr = new HrpcSignalrClient("https://localhost:5001", useMessagePack: true);
Customer? customer3 = await signalr.Call<Customer>(req);

// --- Raw-Form (ohne Fluent Builder) ------------------------------------------
// Gelegentlich nützlich, wenn man Requests dynamisch zusammenbaut.
var raw = new HrpcRequest
{
    Controller = "Customer",
    Method = "GetCustomerById",
    Id = "Customer.GetCustomerById",
    // StringData = JSON-serialisiertes HrpcParameter[]; Data ist jeweils der
    // JSON-token des Werts (Zahl 42 → "42", String "A" → "\"A\"").
    StringData = "[{\"Num\":0,\"ParameterName\":\"id\",\"Data\":\"42\"}]",
};
Customer? customer4 = await rest.Call<Customer>(raw);