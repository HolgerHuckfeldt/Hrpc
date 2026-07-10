using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using HrpcCommon.Models;

namespace HrpcCommon.Results;

/// <summary>
/// Statische Fabrik für <see cref="HrpcResponse"/>-Instanzen aus Controllern
/// (Attribut-basiert: <c>[HrpcController]</c>/<c>[HrpcMethod]</c>). Erzeugt
/// saubere Responses inkl. strukturiertem <see cref="HrpcError"/> bei non-2xx,
/// sodass die Fehlermeldung verlustfrei beim Client ankommt — im C#-Client über
/// die geworfene <c>HrpcException.Error.Message</c>, im JS/TS-Client über
/// <c>response.error.message</c> bzw. die DevUI-Anzeige.
/// </summary>
/// <remarks>
/// Der <see cref="HrpcCore.Services.HrpcInvoker"/> gibt eine vom Controller
/// zurückgegebene <see cref="HrpcResponse"/> unverändert durch
/// (<c>ReturnResponse</c>: <c>if (result is HrpcResponse hrpcResp) return hrpcResp;</c>).
/// Damit ist das der unterstützte Kanal, um aus einer Controller-Methode einen
/// eigenen Status-Code + Fehlertext an den Client zu liefern. Geworfene
/// Exceptions hingegen werden pauschal zu <c>500</c> mit generischer Message
/// (kein Leak in Produktion) — für client-sichtbare Validierungs-/Domain-Fehler
/// also <emphasis>nicht</emphasis> werfen, sondern <c>HrpcResults.Error(...)</c>
/// zurückgeben.
/// </remarks>
public static class HrpcResults
{
    // camelCase + relaxed Encoder (wie der Invoker). ProblemDetails (RFC 7807) wird
    // kanonisch in CamelCase serialisiert; UnsafeRelaxed verhindert `"`-Escaping.
    private static readonly JsonSerializerOptions CamelCaseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// 200 OK mit JSON-serialisiertem Ergebnis. <paramref name="result"/> wird
    /// strukturiert (camelCase, relaxed Encoder) als rohe UTF-8-Bytes in
    /// <see cref="HrpcResponse.DataBytes"/> abgelegt — identisch zum Framework-Pfad
    /// im Invoker, sodass Controller- und Default-Serialisierung dasselbe Wire-Bild
    /// ergeben. Der Transport-Converter schreibt die Bytes in einem Pass (kein
    /// JsonDocument-Baum). <see cref="HrpcResponse.Data"/> bleibt null und wird erst
    /// lazy materialisiert, wenn ein Reader zugreift. <c>null</c> ergibt 204 No Content.
    /// </summary>
    public static HrpcResponse Ok(object? result)
    {
        if (result is null) return NoContent();
        return new HrpcResponse
        {
            Code = 200,
            DataBytes = JsonSerializer.SerializeToUtf8Bytes(result, CamelCaseJsonOptions),
        };
    }

    /// <summary>
    /// 200 OK mit bereits JSON-serialisiertem String. Der String wird als rohe
    /// UTF-8-Bytes in <see cref="HrpcResponse.DataBytes"/> abgelegt und vom
    /// Transport-Converter in einem Pass in den Wire geschrieben — <b>erfordert
    /// gültiges JSON</b> (Wire-Break gegenüber der alten String-through-Data-Semantik).
    /// Nützlich, wenn der Caller selbst serialisiert.
    /// </summary>
    public static HrpcResponse Ok(string jsonData) => new()
    {
        Code = 200,
        DataBytes = Encoding.UTF8.GetBytes(jsonData),
    };

    /// <summary>
    /// 200 OK mit binärem Ergebnis: die Rohbytes ausschließlich in
    /// <see cref="HrpcResponse.Content"/> (kein Base64-String mehr in Data → keine
    /// doppelte Belegung, kein Parse-Problem im Dependency-Pfad). Spiegel des
    /// Invoker-byte[]-Pfads.
    /// </summary>
    public static HrpcResponse Ok(byte[] binary) => new()
    {
        Code = 200,
        Content = binary,
    };

    /// <summary>204 No Content — für <c>void</c>-/<c>Task</c>-Methoden ohne Ergebnis.</summary>
    public static HrpcResponse NoContent() => new() { Code = 204 };

    /// <summary>
    /// Non-2xx-Fehlerantwort. <paramref name="message"/> wohnt ausschließlich in
    /// <see cref="HrpcError.Message"/> (Data bleibt null) — strukturierte Clients
    /// lesen <c>response.error.message</c>, der C#-Client wirft
    /// <c>HrpcException.Error.Message</c>, der JS/TS-Client <c>response.error.message</c>.
    /// </summary>
    /// <param name="code">HTTP-like Status-Code (400, 401, 404, 409, 500, …).</param>
    /// <param name="message">Mensch-lesbare Fehlernachricht (sichtbar beim Client).</param>
    /// <param name="details">Optionale Details (z. B. Diagnose-Hinweis; NICHT für
    /// sensible Stack-Traces verwenden — die gehören in <c>EnableDetailedErrors</c>
    /// auf Server-Seite und werden dort vom Invoker verwaltet).</param>
    public static HrpcResponse Error(int code, string message, string? details = null) => new()
    {
        Code = code,
        Data = null,
        Error = new HrpcError
        {
            Code = code,
            Message = message,
            Details = details,
        },
    };

    /// <summary>400 Bad Request — ungültige Parameter / Validierungsfehler.</summary>
    public static HrpcResponse BadRequest(string message, string? details = null)
        => Error(400, message, details);

    /// <summary>401 Unauthorized — Authentifizierung erforderlich/fehlgeschlagen.</summary>
    public static HrpcResponse Unauthorized(string message = "Unauthorized.")
        => Error(401, message);

    /// <summary>404 Not Found — Ressource/Entität nicht gefunden.</summary>
    public static HrpcResponse NotFound(string message, string? details = null)
        => Error(404, message, details);

    /// <summary>409 Conflict — Konflikt mit aktuellem Zustand (z. B. Duplikat).</summary>
    public static HrpcResponse Conflict(string message, string? details = null)
        => Error(409, message, details);

    /// <summary>
    /// 500 Internal Server Error — nur für Controller, die bewusst eine interne
    /// Fehlersituation signalisieren wollen. Im Normalfall wirft man stattdessen
    /// eine Exception (der Invoker erzeugt das generische 500 + Dev-Details).
    /// </summary>
    public static HrpcResponse InternalServerError(string message, string? details = null)
        => Error(500, message, details);

    /// <summary>
    /// Non-2xx-Fehlerantwort im RFC-7807-ProblemDetails-Stil. <paramref name="problem"/>
    /// wird strukturiert (CamelCase) als <see cref="JsonElement"/> in
    /// <see cref="HrpcResponse.Data"/> serialisiert (kanonisch für Interop); zusätzlich
    /// werden <see cref="HrpcError.Message"/> = <c>title</c> und
    /// <see cref="HrpcError.Details"/> = <c>detail</c> belegt, damit einfache Clients
    /// eine Klartext-Message sehen und ProblemDetails-kundige Clients das vollständige
    /// Objekt in <c>Data</c> lesen.
    /// </summary>
    public static HrpcResponse Error(ProblemDetails problem) => new()
    {
        Code = problem.Status,
        Data = JsonSerializer.SerializeToElement(problem, CamelCaseJsonOptions),
        Error = new HrpcError
        {
            Code = problem.Status,
            Message = problem.Title ?? "Unknown error",
            Details = problem.Detail,
        },
    };
}

/// <summary>
/// Minimaler RFC-7807-ProblemDetails-Datensatz (application/problem+json). Alle
/// Felder optional bis auf <see cref="Status"/>; <see cref="Type"/> ist ein URI-
/// Bezeichner des Fehlertyps (RFC-Default <c>"about:blank"</c>), <see cref="Instance"/>
/// ein URI der konkreten Vorkommens (z. B. Request-/Ressourcen-Bezug).
/// </summary>
public sealed class ProblemDetails
{
    public string? Type { get; set; }
    public string? Title { get; set; }
    public int Status { get; set; }
    public string? Detail { get; set; }
    public string? Instance { get; set; }
}