using System.Text.Json.Nodes;
using Json.Path;

namespace HrpcCore.Services.Helper;

public static class DependencyResolver
{
    /// <summary>
    /// Extrahiert den Wert aus dem dependencyJson anhand des jsonPath-Ausdrucks.
    /// Liefert ein JsonNode zurück (oder null, falls nichts gefunden wurde).
    /// </summary>
    public static JsonNode? ExtractValue(string dependencyJson, string jsonPath)
    {
        // Direkt als JsonNode parsen, damit Evaluate ein JsonNode als Root erhält
        var root = JsonNode.Parse(dependencyJson);
        var parsedPath = JsonPath.Parse(jsonPath);
        var result = parsedPath.Evaluate(root);
        if (result.Matches.Any())
        {
            return result.Matches.First().Value;
        }
        return null;
    }
}
