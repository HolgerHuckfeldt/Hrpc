using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Path;

namespace HrpcCore.Services.Helper;

public static class DependencyResolver
{
    /// <summary>
    /// Extrahiert den Wert aus dem strukturierten <paramref name="element"/> anhand des
    /// jsonPath-Ausdrucks. Liefert ein JsonNode zurück (oder null, falls nichts gefunden).
    /// Data ist seit dem Single-Pass-Fix ein JsonElement (kein JSON-String mehr) — wir
    /// materialisieren es über GetRawText() zu einem JsonNode für JsonPath.Net.
    /// </summary>
    public static JsonNode? ExtractValue(JsonElement element, string jsonPath)
    {
        // JsonElement → JSON-Text → JsonNode (JsonPath.Net.Evaluate erwartet JsonNode-Root).
        var root = JsonNode.Parse(element.GetRawText());
        var parsedPath = JsonPath.Parse(jsonPath);
        var result = parsedPath.Evaluate(root);
        if (result.Matches.Any())
        {
            return result.Matches.First().Value;
        }
        return null;
    }
}
