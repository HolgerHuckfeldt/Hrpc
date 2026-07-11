// ==============================================================================
// 02 — Batch Parallel (C#-Client)
// ==============================================================================
// Mehrere UNABHÄNGIGE Aufrufe in einer einzigen Roundtrip. Der Server führt sie
// per Task.WhenAll concurrently aus (ExecutionMode.Parallel). Ideal, um Latenz
// zu amortisieren, wenn Calls einander nicht brauchen.
//
// Wichtig: Parallel löst KEINE @alias-Abhängigkeiten auf — dafür Serial oder
// Dependency-Chaining siehe 03/04.
// ==============================================================================

using WaevaRpcClient.WaevaRpc;
using WaevaRpcCommon.Models;

using var client = new WaevaRpcRestJsonClient("https://localhost:5001");

var multi = new WaevaRpcMultiRequest
{
    Mode = ExecutionMode.Parallel,        // Task.WhenAll über alle Requests
    Requests = new List<WaevaRpcRequest>
    {
        // .Named(id) setzt die Korrelations-Id — wichtig, um Responses zuzuordnen
        // (insbesondere bei WebSocket/SignalR, wo Batch-Responses an requests[0].id
        // korrelieren — eindeutige Ids verhindern Kollisionen bei konkurrierenden Batches).
        WaevaRpcCall.Init("Customer", "GetAllCustomers").Named("all").ToRequest(),
        WaevaRpcCall.Init("Customer", "GetCustomerById").With(1).Named("c1").ToRequest(),
        WaevaRpcCall.Init("Customer", "GetCustomerById").With(2).Named("c2").ToRequest(),
    },
};

// Call(WaevaRpcMultiRequest) liefert die Responses in Request-Reihenfolge.
var responses = (await client.Call(multi))!.ToList();

var all = responses[0];   // GetAllCustomers → IReadOnlyList<Customer>
var c1   = responses[1];  // GetCustomerById(1)
var c2   = responses[2];  // GetCustomerById(2)

// Hinweis: bei Call(WaevaRpcMultiRequest) bleiben die Ergebnisse als WaevaRpcResponse
// (mit .Data als JsonElement). Wer Typsicherheit pro Response will, parst
// individuell, z. B.:
//   var list = all.Data?.Deserialize<List<Customer>>();
// Für einheitlich typisierte Results gibt es (noch) keine generische Batch-Überladung.