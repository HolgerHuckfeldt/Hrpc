using HrpcCommon.Attribute;
using HrpcCommon.Models;
using HrpcCommon.Results;
using HrpcCore.Attributes;

namespace HrpcTests.Fixtures;

[HrpcController("TestInvoker")]
public class TestInvokerController
{
    [HrpcMethod("Echo")]
    public string Echo(string message) => message;

    [HrpcMethod("Add")]
    public int Add(int a, int b) => a + b;

    [HrpcMethod("EchoAsync")]
    public async Task<string> EchoAsync(string message)
    {
        await Task.Delay(10);
        return message;
    }

    [HrpcMethod("AddAsync")]
    public async Task<int> AddAsync(int a, int b)
    {
        await Task.Delay(10);
        return a + b;
    }

    [HrpcMethod("VoidMethod")]
    public void VoidMethod(string data) { }

    [HrpcMethod("WithCancellation")]
    public async Task<string> WithCancellation(string input, CancellationToken ct)
    {
        await Task.Delay(10, ct);
        return input;
    }

    [HrpcMethod("ComplexReturn")]
    public TestDto ComplexReturn(int id) => new() { Id = id, Name = "Test" };

    [HrpcMethod("NoParams")]
    public string NoParams() => "Hello World";

    // Controller-Methoden, die direkt ein HrpcResponse-Objekt zurückgeben (Weg A:
    // strukturierte Domain-Fehler statt werfen). Der Invoker gibt die Response
    // unverändert durch (HrpcInvoker.ReturnResponse: result is HrpcResponse).
    [HrpcMethod("GetOr404")]
    public HrpcResponse GetOr404(int id)
        => id == 99
            ? HrpcResults.NotFound($"Customer '{id}' not found.")
            : HrpcResults.Ok(new TestDto { Id = id, Name = "Found" });

    [HrpcMethod("ValidationProblem")]
    public HrpcResponse ValidationProblem(string input)
        => string.IsNullOrWhiteSpace(input)
            ? HrpcResults.BadRequest("input must not be empty.", "ParameterName=input")
            : HrpcResults.Ok(input);

    [HrpcMethod("Secured")]
    [HrpcAuthorise]
    public string Secured(string data) => data;

    [HrpcMethod("SecuredWithRole")]
    [HrpcAuthorise(Role = "Admin")]
    public string SecuredWithRole(string data) => data;

    [HrpcMethod("StreamNumbers")]
    public async IAsyncEnumerable<int> StreamNumbers(int count, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        for (int i = 0; i < count; i++)
        {
            await Task.Delay(1, ct);
            yield return i;
        }
    }

    [HrpcMethod("StreamNumbersTask")]
    public async Task<IAsyncEnumerable<int>> StreamNumbersTask(int count, CancellationToken ct = default)
        => StreamNumbers(count, ct);

    [HrpcMethod("UploadBlob")]
    public string UploadBlob(byte[] data, string filename)
        => $"Received {data.Length} bytes for {filename}";

    [HrpcMethod("DownloadBlob")]
    public byte[] DownloadBlob(string name)
        => System.Text.Encoding.UTF8.GetBytes($"Blob content for {name}");

    [HrpcMethod("UploadAndProcess")]
    public async Task<int> UploadAndProcess(byte[] data, CancellationToken ct)
    {
        await Task.Delay(1, ct);
        return data.Length;
    }

    [HrpcMethod("DownloadStream")]
    public System.IO.Stream DownloadStream(string name)
    {
        var content = System.Text.Encoding.UTF8.GetBytes($"Streamed content for {name}");
        return new System.IO.MemoryStream(content);
    }
}

[HrpcDataContract]
public class TestDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Controller mit gepunktetem Namespace, um beliebig tiefe Routing-Pfade
/// (Customer.Address.Contact) über das Controller-Feld abzubilden.
/// </summary>
[HrpcController("Customer.Address.Contact")]
public class NestedContactController
{
    [HrpcMethod("Add")]
    public string Add(string name) => $"added {name}";
}

/// <summary>
/// Deterministischer Controller für Dependency-Chain-Tests. Jede Methode ist
/// ein reiner Roundtrip/Producer ohne externe Zustände, sodass das @alias-Walking
/// pro Datentyp isoliert und deterministisch geprüft werden kann — unabhängig
/// vom zustandsbehafteten, langsamem CustomerService des Sample-Apps.
/// </summary>
[HrpcController("DepChain")]
public class DependencyChainController
{
    // --- Skalare Roundtrips (Producer + Consumer in einer Methode) ---

    [HrpcMethod("EchoBool")]
    public bool EchoBool(bool value) => value;

    [HrpcMethod("EchoLong")]
    public long EchoLong(long value) => value;

    [HrpcMethod("EchoDecimal")]
    public decimal EchoDecimal(decimal value) => value;

    [HrpcMethod("EchoString")]
    public string EchoString(string value) => value;

    // --- Objekt (TestDto) ---

    [HrpcMethod("MakeDto")]
    public TestDto MakeDto(int id, string name) => new() { Id = id, Name = name };

    /// <summary>Nimmt ein ganzes TestDto als @alias und gibt es unverändert zurück.</summary>
    [HrpcMethod("EchoDto")]
    public TestDto EchoDto(TestDto dto) => dto;

    /// <summary>Nimmt ein ganzes TestDto als @alias und extrahiert dessen Id.</summary>
    [HrpcMethod("GetDtoId")]
    public int GetDtoId(TestDto dto) => dto.Id;

    // --- Arrays / Collections ---

    [HrpcMethod("MakeDtoList")]
    public List<TestDto> MakeDtoList() => new()
    {
        new() { Id = 1, Name = "One" },
        new() { Id = 2, Name = "Two" },
        new() { Id = 3, Name = "Three" }
    };

    /// <summary>Nimmt eine ganze List&lt;TestDto&gt; als @alias und gibt sie unverändert zurück.</summary>
    [HrpcMethod("EchoDtoList")]
    public List<TestDto> EchoDtoList(List<TestDto> dtos) => dtos;

    /// <summary>Nimmt eine ganze List&lt;TestDto&gt; als @alias und liefert deren Länge.</summary>
    [HrpcMethod("CountDtoList")]
    public int CountDtoList(List<TestDto> dtos) => dtos.Count;

    /// <summary>Liefert eine feste int-Liste [10,20,30] als Quelle für Array-Element-Pfade ($[1]).</summary>
    [HrpcMethod("MakeIntList")]
    public List<int> MakeIntList() => new() { 10, 20, 30 };

    // --- Weitere Primitives (Roundtrip) ---

    [HrpcMethod("EchoDouble")]
    public double EchoDouble(double value) => value;

    [HrpcMethod("EchoFloat")]
    public float EchoFloat(float value) => value;

    [HrpcMethod("EchoDateTime")]
    public DateTime EchoDateTime(DateTime value) => value;

    [HrpcMethod("EchoGuid")]
    public Guid EchoGuid(Guid value) => value;

    [HrpcMethod("EchoPriority")]
    public ChainPriority EchoPriority(ChainPriority value) => value;

    // --- Ganze primitive Liste als Dependency (nicht nur Element-Extraktion) ---

    /// <summary>Nimmt eine ganze List&lt;int&gt; als @alias und gibt sie unverändert zurück.</summary>
    [HrpcMethod("EchoIntList")]
    public List<int> EchoIntList(List<int> values) => values;

    // --- Geschachtelter Pfad ($.Inner.Id) ---

    [HrpcMethod("MakeNestedDto")]
    public NestedDto MakeNestedDto(int outerId, int innerId) => new()
    {
        Id = outerId,
        Inner = new() { Id = innerId, Name = "Inner" }
    };

    // --- Nullable-Result (FindDto liefert null für id <= 0) ---

    [HrpcMethod("FindDto")]
    public TestDto? FindDto(int id) => id > 0 ? new TestDto { Id = id, Name = "Found" } : null;

    // --- Binär (byte[]) — dokumentiert die Chain-Grenze ---

    [HrpcMethod("DownloadBytes")]
    public byte[] DownloadBytes() => System.Text.Encoding.UTF8.GetBytes("chain-bytes");

    [HrpcMethod("EchoBytes")]
    public byte[] EchoBytes(byte[] data) => data;

    // --- Dictionary als Dependency ---

    [HrpcMethod("MakeDict")]
    public Dictionary<string, int> MakeDict() => new() { { "a", 1 }, { "b", 2 }, { "c", 3 } };

    [HrpcMethod("EchoDict")]
    public Dictionary<string, int> EchoDict(Dictionary<string, int> map) => map;
}

/// <summary>Enum für den Enum-Roundtrip durch eine Chain (default-Serialisierung als Zahl).</summary>
public enum ChainPriority { Low = 0, Medium = 1, High = 2 }

/// <summary>
/// Verschachtelter DataContract für Multi-Level-JsonPath-Tests ($.Inner.Id) in einer Chain.
/// </summary>
[HrpcDataContract]
public class NestedDto
{
    public int Id { get; set; }
    public TestDto Inner { get; set; } = new();
}