using WaevaRpcCommon.Attribute;
using WaevaRpcCommon.Models;
using WaevaRpcCommon.Results;
using WaevaRpcCore.Attributes;

namespace WaevaRpcTests.Fixtures;

[WaevaRpcController("TestInvoker")]
public class TestInvokerController
{
    [WaevaRpcMethod("Echo")]
    public string Echo(string message) => message;

    [WaevaRpcMethod("Add")]
    public int Add(int a, int b) => a + b;

    [WaevaRpcMethod("EchoAsync")]
    public async Task<string> EchoAsync(string message)
    {
        await Task.Delay(10);
        return message;
    }

    [WaevaRpcMethod("AddAsync")]
    public async Task<int> AddAsync(int a, int b)
    {
        await Task.Delay(10);
        return a + b;
    }

    [WaevaRpcMethod("VoidMethod")]
    public void VoidMethod(string data) { }

    [WaevaRpcMethod("WithCancellation")]
    public async Task<string> WithCancellation(string input, CancellationToken ct)
    {
        await Task.Delay(10, ct);
        return input;
    }

    [WaevaRpcMethod("ComplexReturn")]
    public TestDto ComplexReturn(int id) => new() { Id = id, Name = "Test" };

    [WaevaRpcMethod("NoParams")]
    public string NoParams() => "Hello World";

    // Controller-Methoden, die direkt ein WaevaRpcResponse-Objekt zurückgeben (Weg A:
    // strukturierte Domain-Fehler statt werfen). Der Invoker gibt die Response
    // unverändert durch (WaevaRpcInvoker.ReturnResponse: result is WaevaRpcResponse).
    [WaevaRpcMethod("GetOr404")]
    public WaevaRpcResponse GetOr404(int id)
        => id == 99
            ? WaevaRpcResults.NotFound($"Customer '{id}' not found.")
            : WaevaRpcResults.Ok(new TestDto { Id = id, Name = "Found" });

    [WaevaRpcMethod("ValidationProblem")]
    public WaevaRpcResponse ValidationProblem(string input)
        => string.IsNullOrWhiteSpace(input)
            ? WaevaRpcResults.BadRequest("input must not be empty.", "ParameterName=input")
            : WaevaRpcResults.Ok(input);

    [WaevaRpcMethod("Secured")]
    [WaevaRpcAuthorise]
    public string Secured(string data) => data;

    [WaevaRpcMethod("SecuredWithRole")]
    [WaevaRpcAuthorise(Role = "Admin")]
    public string SecuredWithRole(string data) => data;

    [WaevaRpcMethod("StreamNumbers")]
    public async IAsyncEnumerable<int> StreamNumbers(int count, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        for (int i = 0; i < count; i++)
        {
            await Task.Delay(1, ct);
            yield return i;
        }
    }

    [WaevaRpcMethod("StreamNumbersTask")]
    public async Task<IAsyncEnumerable<int>> StreamNumbersTask(int count, CancellationToken ct = default)
        => StreamNumbers(count, ct);

    [WaevaRpcMethod("UploadBlob")]
    public string UploadBlob(byte[] data, string filename)
        => $"Received {data.Length} bytes for {filename}";

    [WaevaRpcMethod("DownloadBlob")]
    public byte[] DownloadBlob(string name)
        => System.Text.Encoding.UTF8.GetBytes($"Blob content for {name}");

    [WaevaRpcMethod("UploadAndProcess")]
    public async Task<int> UploadAndProcess(byte[] data, CancellationToken ct)
    {
        await Task.Delay(1, ct);
        return data.Length;
    }

    [WaevaRpcMethod("DownloadStream")]
    public System.IO.Stream DownloadStream(string name)
    {
        var content = System.Text.Encoding.UTF8.GetBytes($"Streamed content for {name}");
        return new System.IO.MemoryStream(content);
    }
}

[WaevaRpcDataContract]
public class TestDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Controller mit gepunktetem Namespace, um beliebig tiefe Routing-Pfade
/// (Customer.Address.Contact) über das Controller-Feld abzubilden.
/// </summary>
[WaevaRpcController("Customer.Address.Contact")]
public class NestedContactController
{
    [WaevaRpcMethod("Add")]
    public string Add(string name) => $"added {name}";
}

/// <summary>
/// Deterministischer Controller für Dependency-Chain-Tests. Jede Methode ist
/// ein reiner Roundtrip/Producer ohne externe Zustände, sodass das @alias-Walking
/// pro Datentyp isoliert und deterministisch geprüft werden kann — unabhängig
/// vom zustandsbehafteten, langsamem CustomerService des Sample-Apps.
/// </summary>
[WaevaRpcController("DepChain")]
public class DependencyChainController
{
    // --- Skalare Roundtrips (Producer + Consumer in einer Methode) ---

    [WaevaRpcMethod("EchoBool")]
    public bool EchoBool(bool value) => value;

    [WaevaRpcMethod("EchoLong")]
    public long EchoLong(long value) => value;

    [WaevaRpcMethod("EchoDecimal")]
    public decimal EchoDecimal(decimal value) => value;

    [WaevaRpcMethod("EchoString")]
    public string EchoString(string value) => value;

    // --- Objekt (TestDto) ---

    [WaevaRpcMethod("MakeDto")]
    public TestDto MakeDto(int id, string name) => new() { Id = id, Name = name };

    /// <summary>Nimmt ein ganzes TestDto als @alias und gibt es unverändert zurück.</summary>
    [WaevaRpcMethod("EchoDto")]
    public TestDto EchoDto(TestDto dto) => dto;

    /// <summary>Nimmt ein ganzes TestDto als @alias und extrahiert dessen Id.</summary>
    [WaevaRpcMethod("GetDtoId")]
    public int GetDtoId(TestDto dto) => dto.Id;

    // --- Arrays / Collections ---

    [WaevaRpcMethod("MakeDtoList")]
    public List<TestDto> MakeDtoList() => new()
    {
        new() { Id = 1, Name = "One" },
        new() { Id = 2, Name = "Two" },
        new() { Id = 3, Name = "Three" }
    };

    /// <summary>Nimmt eine ganze List&lt;TestDto&gt; als @alias und gibt sie unverändert zurück.</summary>
    [WaevaRpcMethod("EchoDtoList")]
    public List<TestDto> EchoDtoList(List<TestDto> dtos) => dtos;

    /// <summary>Nimmt eine ganze List&lt;TestDto&gt; als @alias und liefert deren Länge.</summary>
    [WaevaRpcMethod("CountDtoList")]
    public int CountDtoList(List<TestDto> dtos) => dtos.Count;

    /// <summary>Liefert eine feste int-Liste [10,20,30] als Quelle für Array-Element-Pfade ($[1]).</summary>
    [WaevaRpcMethod("MakeIntList")]
    public List<int> MakeIntList() => new() { 10, 20, 30 };

    // --- Weitere Primitives (Roundtrip) ---

    [WaevaRpcMethod("EchoDouble")]
    public double EchoDouble(double value) => value;

    [WaevaRpcMethod("EchoFloat")]
    public float EchoFloat(float value) => value;

    [WaevaRpcMethod("EchoDateTime")]
    public DateTime EchoDateTime(DateTime value) => value;

    [WaevaRpcMethod("EchoGuid")]
    public Guid EchoGuid(Guid value) => value;

    [WaevaRpcMethod("EchoPriority")]
    public ChainPriority EchoPriority(ChainPriority value) => value;

    // --- Ganze primitive Liste als Dependency (nicht nur Element-Extraktion) ---

    /// <summary>Nimmt eine ganze List&lt;int&gt; als @alias und gibt sie unverändert zurück.</summary>
    [WaevaRpcMethod("EchoIntList")]
    public List<int> EchoIntList(List<int> values) => values;

    // --- Geschachtelter Pfad ($.Inner.Id) ---

    [WaevaRpcMethod("MakeNestedDto")]
    public NestedDto MakeNestedDto(int outerId, int innerId) => new()
    {
        Id = outerId,
        Inner = new() { Id = innerId, Name = "Inner" }
    };

    // --- Nullable-Result (FindDto liefert null für id <= 0) ---

    [WaevaRpcMethod("FindDto")]
    public TestDto? FindDto(int id) => id > 0 ? new TestDto { Id = id, Name = "Found" } : null;

    // --- Binär (byte[]) — dokumentiert die Chain-Grenze ---

    [WaevaRpcMethod("DownloadBytes")]
    public byte[] DownloadBytes() => System.Text.Encoding.UTF8.GetBytes("chain-bytes");

    [WaevaRpcMethod("EchoBytes")]
    public byte[] EchoBytes(byte[] data) => data;

    // --- Dictionary als Dependency ---

    [WaevaRpcMethod("MakeDict")]
    public Dictionary<string, int> MakeDict() => new() { { "a", 1 }, { "b", 2 }, { "c", 3 } };

    [WaevaRpcMethod("EchoDict")]
    public Dictionary<string, int> EchoDict(Dictionary<string, int> map) => map;
}

/// <summary>Enum für den Enum-Roundtrip durch eine Chain (default-Serialisierung als Zahl).</summary>
public enum ChainPriority { Low = 0, Medium = 1, High = 2 }

/// <summary>
/// Verschachtelter DataContract für Multi-Level-JsonPath-Tests ($.Inner.Id) in einer Chain.
/// </summary>
[WaevaRpcDataContract]
public class NestedDto
{
    public int Id { get; set; }
    public TestDto Inner { get; set; } = new();
}