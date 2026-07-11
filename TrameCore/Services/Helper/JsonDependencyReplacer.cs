using System.Text.Json;
using System.Text.Json.Nodes;

namespace TrameCore.Services.Helper;

public static class JsonDependencyReplacer
{
    /// <summary>
    /// Ersetzt im übergebenen JsonNode rekursiv alle Vorkommen des Strings "@dependency"
    /// durch den Wert, der aus dependencyJson anhand von dataPath extrahiert wird.
    /// </summary>
    public static void ReplaceDependency(JsonNode node, string dependencyJson, string dataPath)
    {
        if (node is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<string>(out var stringValue) &&
                stringValue == "\"@dependency\"")
            {
                // Den neuen Wert extrahieren (als JsonNode). DependencyResolver arbeitet
                // seit dem Single-Pass-Fix auf JsonElement — hier den String parsen.
                var extractedNode = DependencyResolver.ExtractValue(
                    JsonDocument.Parse(dependencyJson).RootElement.Clone(), dataPath);
                if (extractedNode != null)
                {
                    // Ersetze den aktuellen Knoten in der Elternstruktur.
                    ReplaceInParent(node, extractedNode);
                }
            }
        }
        else if (node is JsonObject jsonObject)
        {
            // Über eine Kopie der Properties iterieren, um Modifikationen während der Iteration zu vermeiden
            foreach (var property in jsonObject.ToList())
            {
                ReplaceDependency(property.Value, dependencyJson, dataPath);
            }
        }
        else if (node is JsonArray jsonArray)
        {
            for (int i = 0; i < jsonArray.Count; i++)
            {
                ReplaceDependency(jsonArray[i], dependencyJson, dataPath);
            }
        }
    }

    /// <summary>
    /// Ersetzt in der Elternstruktur von oldNode den alten Knoten durch newNode.
    /// Unterstützt JsonObject und JsonArray.
    /// </summary>
    private static void ReplaceInParent(JsonNode oldNode, JsonNode newNode)
    {
        // Erzeuge einen Klon des neuen Knotens, damit er keinen Elternbezug hat.
        var newNodeClone = JsonNode.Parse(newNode.ToJsonString());
        var parent = oldNode.Parent;
        if (parent is JsonObject parentObj)
        {
            foreach (var kvp in parentObj.ToList())
            {
                if (kvp.Value == oldNode)
                {
                    parentObj[kvp.Key] = newNodeClone.ToString();
                    break;
                }
            }
        }
        else if (parent is JsonArray parentArr)
        {
            for (int i = 0; i < parentArr.Count; i++)
            {
                if (parentArr[i] == oldNode)
                {
                    parentArr[i] = newNodeClone.ToString();
                    break;
                }
            }
        }
        else
        {
            throw new InvalidOperationException("Knoten ohne Eltern können nicht ersetzt werden.");
        }
    }

}