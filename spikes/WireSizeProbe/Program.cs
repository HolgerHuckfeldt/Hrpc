// Profiling-Spike: Wo sitzen die Bytes beim WaevaRpc-REST-GetAll?
//
// Antwortet die Frage, ob eine kompakte Draht-Codierung (MessagePack Route B =
// JSON→MessagePack 1:1, bzw. Route A = typisiert) den Single-Call-Nachteil
// gegenüber gRPC tatsächlich schließt — oder ob die Bytes woanders stecken
// (Envelope-Tax, Defaults, String-Keys).
//
// Spiegelt EXAKT die Benchmark-Bedingungen:
//  - 100 sparse Customers, wie CustomerService.AddCustomer sie anlegt
//    (nur Id/Name/OrderId gesetzt; Created=default, ResourceId=Guid.Empty,
//     Map=null, Addresses=[]).
//  - Invoker legt das Ergebnis als JSON-String in WaevaRpcResponse.Data
//    (WaevaRpcInvoker.ReturnResponse: JsonSerializer.Serialize(result) OHNE Options
//     → PascalCase). Results.Ok(response) serialisiert den Mantel mit Minimal-API-
//     Default (camelCase) → das innere JSON wird als escapetes String-Literal
//     eingebettet. Genau diese Doppel-Wicklung wird hier gemessen.
//
// Kein Server, kein HTTP — reine Serialisierungs-Messung (Draht-Größen).
using Google.Protobuf;
using WaevaRpc.Model;
using WaevaRpcCommon.Models;
using System.Text.Encodings.Web;
using MessagePack;
using MessagePack.Resolvers;
using System.Text.Json;

const int N = 100;

// 100 sparse Customers — exakt wie AddCustomer sie erzeugt.
var customers = new List<Customer>();
for (int i = 1; i <= N; i++)
    customers.Add(new Customer(i, $"Customer-{i-1}") { OrderId = i });

// Minimal-API-Default (camelCase, Web-Defaults) — Mantel-Serialisierung.
var webOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
// Invoker-Default (PascalCase, keine camel-Policy) — Payload-in-Data.
var invokerOptions = new JsonSerializerOptions(); // default: PascalCase, schreibt nulls

// ─── 1. Natives REST: List<Customer> direkt als JSON (camelCase) ────────────
string nativeRestBody = JsonSerializer.Serialize(customers, webOptions);
int nativeRestBytes = System.Text.Encoding.UTF8.GetByteCount(nativeRestBody);

// ─── 2. WaevaRpc: Payload-Inner (Invoker legt in Data) — PascalCase-JSON ───────
string waevaRpcPayloadJson = JsonSerializer.Serialize(customers, invokerOptions);
int waevaRpcPayloadBytes = System.Text.Encoding.UTF8.GetByteCount(waevaRpcPayloadJson);

// ─── 3. WaevaRpc: volle REST-Antwort (Mantel camelCase, Data=escapeter Payload-String) ──
var waevaRpcResponse = new WaevaRpcResponse
{
    Code = 200,
    Data = waevaRpcPayloadJson,   // inner JSON wird als String-Literal eingebettet
    Id = "req-1"
};
string waevaRpcFullJson = JsonSerializer.Serialize(waevaRpcResponse, webOptions);
int waevaRpcFullBytes = System.Text.Encoding.UTF8.GetByteCount(waevaRpcFullJson);
int envelopeTax = waevaRpcFullBytes - waevaRpcPayloadBytes; // Mantel + Escape-Overhead

// ─── 3b. JSON-Fix-Hebel (ohne neues Draht-Format!) ───────────────────────────
//  (a) Relaxed Encoder: `"` → `\"` statt `"` (6 B). Eine Options-Änderung.
var relaxedOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
};
string relaxedFull = JsonSerializer.Serialize(waevaRpcResponse, relaxedOptions);
int relaxedFullBytes = System.Text.Encoding.UTF8.GetByteCount(relaxedFull);
//  (b) Single-Pass: Payload strukturiert einbetten statt als JSON-String in Data —
//      kein Double-Wrapping, keine Quote-Escapes im Payload. (Entspricht einer
//      Invoker-Änderung: Ergebnis als strukturierter Wert, nicht als JSON-String.)
string singlePass = JsonSerializer.Serialize(new { code = 200, data = customers, id = "req-1" }, relaxedOptions);
int singlePassBytes = System.Text.Encoding.UTF8.GetByteCount(singlePass);

//    Keine Schema-Änderung, keine Attribute — „JSON, nur binär". Envelope-Tax
//    entfällt (keine escapeten Quotes), aber Payload behält String-Keys + Defaults.
byte[] routeBPayload = MessagePackSerializer.ConvertFromJson(waevaRpcPayloadJson);
int routeBPayloadBytes = routeBPayload.Length;
// Route B volle Antwort: MessagePack-Envelope (Integer-Keys, [MessagePackObject])
// + Payload als binär in Content (statt JSON-String in Data).
byte[] routeBFull = MessagePackSerializer.Serialize(new WaevaRpcResponse
{
    Code = 200,
    Content = routeBPayload,
    Id = "req-1"
});
int routeBFullBytes = routeBFull.Length;

// ─── 5. MessagePack Route A: typisiert (contractless, String-Keys) ──────────
//    Customer ist NICHT [MessagePackObject] → contractless nutzt Property-Namen
//    als String-Keys (kein IDL, „Klassen sind der Contract"). Werte typisiert
//    (DateTime→int64, Guid→string), aber Defaults werden geschrieben.
var contractlessOpts = MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance);
byte[] routeAPayload = MessagePackSerializer.Serialize(customers, contractlessOpts);
int routeAPayloadBytes = routeAPayload.Length;
byte[] routeAFull = MessagePackSerializer.Serialize(new WaevaRpcResponse
{
    Code = 200,
    Content = routeAPayload,
    Id = "req-1"
});
int routeAFullBytes = routeAFull.Length;

// ─── 6. gRPC-Protobuf: proto3 lässt Defaults weg (int-Field-Tags, keine Keys) ──
var protoList = new WaevaRpc.Grpc.CustomerList();
for (int i = 1; i <= N; i++)
{
    protoList.Customers.Add(new WaevaRpc.Grpc.Customer
    {
        Id = i,
        OrderId = i,
        Name = $"Customer-{i-1}"
        // created=0, resource_id="" → proto3 lässt Defaults weg
    });
}
int grpcBytes = protoList.ToByteArray().Length;

// ─── Ausgabe ────────────────────────────────────────────────────────────────
double KB(int b) => b / 1024.0;

// Diagnose: woher kommt die Envelope-Tax wirklich?
int quoteCount = waevaRpcPayloadJson.Count(c => c == '"');
int backslashCount = waevaRpcFullJson.Count(c => c == '\\');
// Data allein als JSON-String serialisiert = exakte escaped-Größe.
string dataAsJsonString = JsonSerializer.Serialize(waevaRpcPayloadJson, webOptions);
int dataAsJsonStringBytes = System.Text.Encoding.UTF8.GetByteCount(dataAsJsonString);
// Reiner Mantel: WaevaRpcResponse mit Data=null.
string pureEnvelope = JsonSerializer.Serialize(new WaevaRpcResponse { Code = 200, Id = "req-1" }, webOptions);
int pureEnvelopeBytes = System.Text.Encoding.UTF8.GetByteCount(pureEnvelope);
Console.WriteLine("DIAGNOSE Envelope-Tax (präzise):");
Console.WriteLine($"  Payload-Inner (Data-Rohstring)   : {waevaRpcPayloadBytes:N0} B  (Quotes: {quoteCount:N0})");
Console.WriteLine($"  Data als JSON-String (escaped)   : {dataAsJsonStringBytes:N0} B  → Escape-Overhead: {dataAsJsonStringBytes - waevaRpcPayloadBytes:N0} B");
Console.WriteLine($"  Reiner Mantel (WaevaRpcResponse, Data=null): {pureEnvelopeBytes:N0} B");
Console.WriteLine($"  Summe (Data-escaped + Mantel)    : {dataAsJsonStringBytes + pureEnvelopeBytes:N0} B  (Full gemessen: {waevaRpcFullBytes:N0})");
Console.WriteLine($"  Backslashes im Full              : {backslashCount:N0}");
Console.WriteLine($"  Reiner Mantel (erste 200)        : {pureEnvelope[..Math.Min(200, pureEnvelope.Length)]}");
int unicodeEscapes = System.Text.RegularExpressions.Regex.Matches(dataAsJsonString, @"\\u[0-9a-fA-F]{4}").Count;
Console.WriteLine($"  \\uXXXX-Escapes im Data-String    : {unicodeEscapes:N0}");
Console.WriteLine($"  Data-escaped (erste 400)        : {dataAsJsonString[..Math.Min(400, dataAsJsonString.Length)]}");
Console.WriteLine();

Console.WriteLine($"Wire-Size-Probe — {N} sparse Customers (GetAll)");
Console.WriteLine(new string('=', 70));
Console.WriteLine();
Console.WriteLine($"{"Kodierung",-50} {"Bytes",10} {"KB",8}");
Console.WriteLine(new string('-', 70));
Console.WriteLine($"{"REST nativ (camelCase JSON)",-50} {nativeRestBytes,10:N0} {KB(nativeRestBytes),8:N2}");
Console.WriteLine($"{"WaevaRpc Payload-Inner (PascalCase JSON)",-50} {waevaRpcPayloadBytes,10:N0} {KB(waevaRpcPayloadBytes),8:N2}");
Console.WriteLine($"{"WaevaRpc REST voll (Mantel + escapeter Payload)",-50} {waevaRpcFullBytes,10:N0} {KB(waevaRpcFullBytes),8:N2}");
Console.WriteLine($"{"  davon Envelope-Tax (Mantel+Escape)",-50} {envelopeTax,10:N0} {KB(envelopeTax),8:N2}");
Console.WriteLine($"{"WaevaRpc REST + relaxed Encoder (\" statt \\u0022)",-50} {relaxedFullBytes,10:N0} {KB(relaxedFullBytes),8:N2}");
Console.WriteLine($"{"WaevaRpc REST Single-Pass (strukturiert, kein Wrap)",-50} {singlePassBytes,10:N0} {KB(singlePassBytes),8:N2}");
Console.WriteLine();
Console.WriteLine($"{"MessagePack Route B — Payload (JSON→MP 1:1)",-50} {routeBPayloadBytes,10:N0} {KB(routeBPayloadBytes),8:N2}");
Console.WriteLine($"{"MessagePack Route B — voll (MP-Envelope+binär)",-50} {routeBFullBytes,10:N0} {KB(routeBFullBytes),8:N2}");
Console.WriteLine($"{"MessagePack Route A — Payload (contractless)",-50} {routeAPayloadBytes,10:N0} {KB(routeAPayloadBytes),8:N2}");
Console.WriteLine($"{"MessagePack Route A — voll (MP-Envelope+binär)",-50} {routeAFullBytes,10:N0} {KB(routeAFullBytes),8:N2}");
Console.WriteLine($"{"gRPC-Protobuf (proto3, Defaults weggelassen)",-50} {grpcBytes,10:N0} {KB(grpcBytes),8:N2}");
Console.WriteLine();
Console.WriteLine("Verhältnisse (kleiner = besser):");
Console.WriteLine(new string('-', 70));
double Ref(int b) => Math.Round((double)b / grpcBytes, 2);
Console.WriteLine($"  gRPC-Protobuf              = {Ref(grpcBytes)}×  (Referenz)");
Console.WriteLine($"  WaevaRpc REST voll (JSON)      = {Ref(waevaRpcFullBytes)}×  vs gRPC");
Console.WriteLine($"  WaevaRpc REST + relaxed Encoder= {Ref(relaxedFullBytes)}×  vs gRPC");
Console.WriteLine($"  WaevaRpc REST Single-Pass      = {Ref(singlePassBytes)}×  vs gRPC");
Console.WriteLine($"  WaevaRpc Payload-Inner (JSON)  = {Ref(waevaRpcPayloadBytes)}×  vs gRPC");
Console.WriteLine($"  MessagePack Route B voll   = {Ref(routeBFullBytes)}×  vs gRPC");
Console.WriteLine($"  MessagePack Route A voll   = {Ref(routeAFullBytes)}×  vs gRPC");
Console.WriteLine();
Console.WriteLine("Wo sitzen die WaevaRpc-REST-Bytes (vs nativ-REST)?");
Console.WriteLine($"  Nativ-REST-Body            : {nativeRestBytes:N0} B");
Console.WriteLine($"  WaevaRpc-Envelope-Tax (Mantel) : {envelopeTax:N0} B  ({envelopeTax*100.0/waevaRpcFullBytes:0.0}% des WaevaRpc-Body)");
Console.WriteLine($"  → Envelope-Tax allein macht WaevaRpc {envelopeTax:n0} B größer als nötig.");