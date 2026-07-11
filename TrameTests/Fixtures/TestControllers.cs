using TrameCommon.Attribute;
using TrameCommon.Models;
using TrameCommon.Results;
using TrameCore.Attributes;

namespace TrameTests.Fixtures;

[TrameController("TestInvoker")]
public class TestInvokerController
{
    [TrameMethod("Echo")]
    public string Echo(string message) => message;

    [TrameMethod("Add")]
    public int Add(int a, int b) => a + b;

    [TrameMethod("EchoAsync")]
    public async Task<string> EchoAsync(string message)
    {
        await Task.Delay(10);
        return message;
    }

    [TrameMethod("AddAsync")]
    public async Task<int> AddAsync(int a, int b)
    {
        await Task.Delay(10);
        return a + b;
    }

    [TrameMethod("VoidMethod")]
    public void VoidMethod(string data) { }

    [TrameMethod("WithCancellation")]
    public async Task<string> WithCancellation(string input, CancellationToken ct)
    {
        await Task.Delay(10, ct);
        return input;
    }

    [TrameMethod("ComplexReturn")]
    public TestDto ComplexReturn(int id) => new() { Id = id, Name = "Test" };

    [TrameMethod("NoParams")]
    public string NoParams() => "Hello World";

    // Controller-Methoden, die direkt ein TrameResponse-Objekt zurückgeben (Weg A:
    // strukturierte Domain-Fehler statt werfen). Der Invoker gibt die Response
    // unverändert durch (TrameInvoker.ReturnResponse: result is TrameResponse).
    [TrameMethod("GetOr404")]
    public TrameResponse GetOr404(int id)
        => id == 99
            ? TrameResults.NotFound($"Customer '{id}' not found.")
            : TrameResults.Ok(new TestDto { Id = id, Name = "Found" });

    [TrameMethod("ValidationProblem")]
    public TrameResponse ValidationProblem(string input)
        => string.IsNullOrWhiteSpace(input)
            ? TrameResults.BadRequest("input must not be empty.", "ParameterName=input")
            : TrameResults.Ok(input);

    [TrameMethod("Secured")]
    [TrameAuthorise]
    public string Secured(string data) => data;

    [TrameMethod("SecuredWithRole")]
    [TrameAuthorise(Role = "Admin")]
    public string SecuredWithRole(string data) => data;

    [TrameMethod("StreamNumbers")]
    public async IAsyncEnumerable<int> StreamNumbers(int count, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        for (int i = 0; i < count; i++)
        {
            await Task.Delay(1, ct);
            yield return i;
        }
    }

    [TrameMethod("StreamNumbersTask")]
    public async Task<IAsyncEnumerable<int>> StreamNumbersTask(int count, CancellationToken ct = default)
        => StreamNumbers(count, ct);

    [TrameMethod("UploadBlob")]
    public string UploadBlob(byte[] data, string filename)
        => $"Received {data.Length} bytes for {filename}";

    [TrameMethod("DownloadBlob")]
    public byte[] DownloadBlob(string name)
        => System.Text.Encoding.UTF8.GetBytes($"Blob content for {name}");

    [TrameMethod("UploadAndProcess")]
    public async Task<int> UploadAndProcess(byte[] data, CancellationToken ct)
    {
        await Task.Delay(1, ct);
        return data.Length;
    }

    [TrameMethod("DownloadStream")]
    public System.IO.Stream DownloadStream(string name)
    {
        var content = System.Text.Encoding.UTF8.GetBytes($"Streamed content for {name}");
        return new System.IO.MemoryStream(content);
    }
}

[TrameDataContract]
public class TestDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Controller mit gepunktetem Namespace, um beliebig tiefe Routing-Pfade
/// (Customer.Address.Contact) über das Controller-Feld abzubilden.
/// </summary>
[TrameController("Customer.Address.Contact")]
public class NestedContactController
{
    [TrameMethod("Add")]
    public string Add(string name) => $"added {name}";
}

/// <summary>
/// Deterministischer Controller für Dependency-Chain-Tests. Jede Methode ist
/// ein reiner Roundtrip/Producer ohne externe Zustände, sodass das @alias-Walking
/// pro Datentyp isoliert und deterministisch geprüft werden kann — unabhängig
/// vom zustandsbehafteten, langsamem CustomerService des Sample-Apps.
/// </summary>
[TrameController("DepChain")]
public class DependencyChainController
{
    // --- Skalare Roundtrips (Producer + Consumer in einer Methode) ---

    [TrameMethod("EchoBool")]
    public bool EchoBool(bool value) => value;

    [TrameMethod("EchoLong")]
    public long EchoLong(long value) => value;

    [TrameMethod("EchoDecimal")]
    public decimal EchoDecimal(decimal value) => value;

    [TrameMethod("EchoString")]
    public string EchoString(string value) => value;

    // --- Objekt (TestDto) ---

    [TrameMethod("MakeDto")]
    public TestDto MakeDto(int id, string name) => new() { Id = id, Name = name };

    /// <summary>Nimmt ein ganzes TestDto als @alias und gibt es unverändert zurück.</summary>
    [TrameMethod("EchoDto")]
    public TestDto EchoDto(TestDto dto) => dto;

    /// <summary>Nimmt ein ganzes TestDto als @alias und extrahiert dessen Id.</summary>
    [TrameMethod("GetDtoId")]
    public int GetDtoId(TestDto dto) => dto.Id;

    // --- Arrays / Collections ---

    [TrameMethod("MakeDtoList")]
    public List<TestDto> MakeDtoList() => new()
    {
        new() { Id = 1, Name = "One" },
        new() { Id = 2, Name = "Two" },
        new() { Id = 3, Name = "Three" }
    };

    /// <summary>Nimmt eine ganze List&lt;TestDto&gt; als @alias und gibt sie unverändert zurück.</summary>
    [TrameMethod("EchoDtoList")]
    public List<TestDto> EchoDtoList(List<TestDto> dtos) => dtos;

    /// <summary>Nimmt eine ganze List&lt;TestDto&gt; als @alias und liefert deren Länge.</summary>
    [TrameMethod("CountDtoList")]
    public int CountDtoList(List<TestDto> dtos) => dtos.Count;

    /// <summary>Liefert eine feste int-Liste [10,20,30] als Quelle für Array-Element-Pfade ($[1]).</summary>
    [TrameMethod("MakeIntList")]
    public List<int> MakeIntList() => new() { 10, 20, 30 };

    // --- Weitere Primitives (Roundtrip) ---

    [TrameMethod("EchoDouble")]
    public double EchoDouble(double value) => value;

    [TrameMethod("EchoFloat")]
    public float EchoFloat(float value) => value;

    [TrameMethod("EchoDateTime")]
    public DateTime EchoDateTime(DateTime value) => value;

    [TrameMethod("EchoGuid")]
    public Guid EchoGuid(Guid value) => value;

    [TrameMethod("EchoPriority")]
    public ChainPriority EchoPriority(ChainPriority value) => value;

    // --- Ganze primitive Liste als Dependency (nicht nur Element-Extraktion) ---

    /// <summary>Nimmt eine ganze List&lt;int&gt; als @alias und gibt sie unverändert zurück.</summary>
    [TrameMethod("EchoIntList")]
    public List<int> EchoIntList(List<int> values) => values;

    // --- Geschachtelter Pfad ($.Inner.Id) ---

    [TrameMethod("MakeNestedDto")]
    public NestedDto MakeNestedDto(int outerId, int innerId) => new()
    {
        Id = outerId,
        Inner = new() { Id = innerId, Name = "Inner" }
    };

    // --- Nullable-Result (FindDto liefert null für id <= 0) ---

    [TrameMethod("FindDto")]
    public TestDto? FindDto(int id) => id > 0 ? new TestDto { Id = id, Name = "Found" } : null;

    // --- Binär (byte[]) — dokumentiert die Chain-Grenze ---

    [TrameMethod("DownloadBytes")]
    public byte[] DownloadBytes() => System.Text.Encoding.UTF8.GetBytes("chain-bytes");

    [TrameMethod("EchoBytes")]
    public byte[] EchoBytes(byte[] data) => data;

    // --- Dictionary als Dependency ---

    [TrameMethod("MakeDict")]
    public Dictionary<string, int> MakeDict() => new() { { "a", 1 }, { "b", 2 }, { "c", 3 } };

    [TrameMethod("EchoDict")]
    public Dictionary<string, int> EchoDict(Dictionary<string, int> map) => map;
}

/// <summary>Enum für den Enum-Roundtrip durch eine Chain (default-Serialisierung als Zahl).</summary>
public enum ChainPriority { Low = 0, Medium = 1, High = 2 }

/// <summary>
/// Verschachtelter DataContract für Multi-Level-JsonPath-Tests ($.Inner.Id) in einer Chain.
/// </summary>
[TrameDataContract]
public class NestedDto
{
    public int Id { get; set; }
    public TestDto Inner { get; set; } = new();
}

// --- Fixtures für die Signatur-Inferenz-Tests (Weg C) ----------------------------
// Separate Controller, damit die 18-Methoden-Assertion für TestInvokerController
// unangetastet bleibt. Diese Controller üben die Expansion-Heuristik:
//   Regel 4: UnmarkedDto (own Assembly, kein Attribute) → expandiert.
//   Regel 5: TrameResponse (fremde Assembly, kein Override) → opaque.
//   Regel 2: ExcludedDto (own Assembly, Exclude=true)     → force-opaque.

/// <summary>
/// Controller für die Weg-C-Discovery-Inferenz-Tests. Jede Methode zielt auf genau
/// eine Heuristik-Regel ab.
/// </summary>
[TrameController("DiscoveryInference")]
public class DiscoveryInferenceController
{
    /// <summary>Regel 4: unmarkierter Own-Assembly-Typ als Rückgabewert → muss expandieren.</summary>
    [TrameMethod("ReturnUnmarked")]
    public UnmarkedDto ReturnUnmarked(int id) => new() { Id = id, Name = "inferred" };

    /// <summary>Regel 5: Framework-Envelope aus TrameCommon (fremde Assembly) → muss opaque bleiben.</summary>
    [TrameMethod("ReturnFrameworkType")]
    public TrameResponse ReturnFrameworkType(int id)
        => TrameResults.Ok(new UnmarkedDto { Id = id, Name = "envelope" });

    /// <summary>Regel 2: Own-Assembly-Typ mit [TrameDataContract(Exclude = true)] → force-opaque.</summary>
    [TrameMethod("TakeExcluded")]
    public int TakeExcluded(ExcludedDto d) => d.X;
}

/// <summary>Unmarkierter DTO in der Test-Assembly (Contract-Assembly-Set) — Regel 4.</summary>
public class UnmarkedDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>Own-Assembly-Typ mit Exclude-Override — Regel 2 (force-opaque).</summary>
[TrameDataContract(Exclude = true)]
public class ExcludedDto
{
    public int X { get; set; }
}