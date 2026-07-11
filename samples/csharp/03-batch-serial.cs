// ==============================================================================
// 03 — Batch Serial (C#-Client)
// ==============================================================================
// Mehrere Aufrufe nacheinander in einer Roundtrip (ExecutionMode.Serial). Der
// Server führt sie in Request-Reihenfolge sequenziell aus.
//
// Serial OHNE DependencyMapping/​@alias löst keine Aliase auf — es ist schlicht
// geordnete Ausführung. Wer Werte zwischen Calls weitergibt, braucht
// DependencyMapping (siehe 04). Serial ist nützlich, wenn Reihenfolge/Bandbreite
// wichtig sind oder der Server-Roundtrip einmal bleiben soll.
// ==============================================================================

using HrpcClient.Hrpc;
using HrpcCommon.Models;

using var client = new HrpcRestJsonClient("https://localhost:5001");

var multi = new HrpcMultiRequest
{
    Mode = ExecutionMode.Serial,          // sequenziell in Request-Reihenfolge
    Requests = new List<HrpcRequest>
    {
        HrpcCall.Init("Customer", "GetCustomerById").With(1).Named("a").ToRequest(),
        HrpcCall.Init("Customer", "GetCustomerById").With(2).Named("b").ToRequest(),
    },
};

var responses = (await client.Call(multi))!.ToList();
var a = responses[0];
var b = responses[1];

// Gotcha: sobald IRGENDEIN Request ein DependencyMapping hat, schaltet der Server
// automatisch auf topologische Batch-Ausführung und ignoriert Mode. Für reine
// Serial-Semantik ohne Aliase einfach kein DependencyMapping setzen (wie hier).