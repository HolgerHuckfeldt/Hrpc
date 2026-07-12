using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Path;

namespace TrameCore.Services.Helper;

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
        var matches = parsedPath.Evaluate(root).Matches;

        // 0 Treffer → kein Wert (Alias bleibt ungesetzt → BadRequest „Unresolved").
        if (matches.Count == 0)
            return null;

        // Genau 1 Treffer → Skalar wie bisher (bestehende Semantik bleibt unverändert,
        // auch wenn der eine Treffer selbst ein Array/Object ist — dann kommt genau
        // dieser Knoten zurück, z. B. "$" über einer List<int>).
        if (matches.Count == 1)
            return matches[0].Value;

        // >1 Treffer (z. B. "$[*].id", "$..Prop") → sammle alle Treffer in ein JSON-Array.
        // Bis v1 wurde hier .Matches.First() geliefert, wodurch ein Wildcard-Pfad still
        // auf das erste Element kollabierte. Jetzt entsteht ein Array, das die @alias-
        // Injektion typgetreu in einen List<T>/T[]/IEnumerable<T>-Parameter übergibt
        // (BuildParameters deserialisiert den Array-JSON-String per Deserialize<List<T>>).
        // DeepClone: die Match-Werte gehören zum geparsten root-Baum und dürfen beim
        // Umhängen ins neue Array nicht ihren Elternbezug behalten.
        var arr = new JsonArray();
        foreach (var match in matches)
            arr.Add(match.Value?.DeepClone());
        return arr;
    }
}
