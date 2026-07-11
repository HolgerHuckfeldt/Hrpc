using TrameCommon.Models;
using System.Collections.Concurrent;

namespace TrameCore.Services.Helper;

/// <summary>
/// Builds a dependency graph from TrameRequests and performs topological sorting.
/// Groups independent requests into parallel batches (level-based execution).
/// Detects cycles and throws InvalidOperationException with cycle details.
/// </summary>
public static class DependencyGraphBuilder
{
    /// <summary>
    /// Sorts requests into execution batches based on their dependency mappings.
    /// Requests without dependencies are grouped into the first batch.
    /// Requests depending on batch N results are in batch N+1.
    /// </summary>
    /// <param name="requests">The list of requests to sort.</param>
    /// <returns>Ordered list of batches (each batch can execute in parallel).</returns>
    /// <exception cref="InvalidOperationException">Thrown when a cycle is detected.</exception>
    public static List<List<TrameRequest>> SortByDependencyBatches(List<TrameRequest> requests)
    {
        if (requests == null || requests.Count == 0)
            return new List<List<TrameRequest>>();

        // Build dependency map: requestId -> set of alias names it depends on
        var dependencies = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        // Build provider map: alias -> requestId that provides it (via DependencyMapping)
        var providers = new Dictionary<string, string>(StringComparer.Ordinal);
        // Index requests by ID
        var requestById = new Dictionary<string, TrameRequest>(StringComparer.Ordinal);

        foreach (var request in requests)
        {
            var id = request.Id ?? string.Empty;
            if (string.IsNullOrEmpty(id))
                id = $"{request.Controller}.{request.Method}";
            requestById[id] = request;
            dependencies[id] = new HashSet<string>(StringComparer.Ordinal);

            // Register what this request provides
            if (request.DependencyMapping != null)
            {
                foreach (var kvp in request.DependencyMapping)
                {
                    // kvp.Key = alias, kvp.Value = jsonPath
                    providers[kvp.Key] = id;
                }
            }
        }

        // Build dependency edges: for each request, find which aliases it uses (via @alias in StringData)
        foreach (var request in requests)
        {
            var id = request.Id ?? string.Empty;
            if (string.IsNullOrEmpty(id))
                id = $"{request.Controller}.{request.Method}";

            if (string.IsNullOrEmpty(request.StringData))
                continue;

            // Find all @alias placeholders in the parameter JSON
            var usedAliases = ExtractAliases(request.StringData);
            foreach (var alias in usedAliases)
            {
                if (providers.TryGetValue(alias, out var providerId))
                {
                    if (providerId != id) // Don't depend on self
                        dependencies[id].Add(providerId);
                }
            }
        }

        // Topological sort with cycle detection (Kahn's algorithm, batch-based)
        var result = new List<List<TrameRequest>>();
        var completed = new HashSet<string>(StringComparer.Ordinal);
        var remaining = new HashSet<string>(dependencies.Keys, StringComparer.Ordinal);

        while (remaining.Count > 0)
        {
            // Find all requests whose dependencies are fully satisfied
            var batch = remaining
                .Where(id => dependencies[id].All(dep => completed.Contains(dep)))
                .ToList();

            if (batch.Count == 0)
            {
                // Cycle detected: remaining items all have unsatisfied dependencies
                var cycleMembers = remaining.ToList();
                throw new InvalidOperationException(
                    $"Zyklus in Abhängigkeiten erkannt. Beteiligte Requests: {string.Join(", ", cycleMembers)}");
            }

            var batchRequests = batch.Select(id => requestById[id]).ToList();
            result.Add(batchRequests);

            foreach (var id in batch)
            {
                completed.Add(id);
                remaining.Remove(id);
            }
        }

        return result;
    }

    /// <summary>
    /// Extracts all @alias placeholders from a JSON parameter string.
    /// </summary>
    private static HashSet<string> ExtractAliases(string json)
    {
        var aliases = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(json))
            return aliases;

        // Simple token scan: find strings starting with @
        // This is intentionally simple to avoid full JSON parsing overhead.
        // The actual replacement logic in the invoker handles the precise matching.
        for (int i = 0; i < json.Length; i++)
        {
            if (json[i] == '@')
            {
                // Extract the alias name (alphanumeric + underscore)
                var sb = new System.Text.StringBuilder();
                int j = i + 1;
                while (j < json.Length && (char.IsLetterOrDigit(json[j]) || json[j] == '_'))
                {
                    sb.Append(json[j]);
                    j++;
                }
                if (sb.Length > 0)
                {
                    aliases.Add(sb.ToString());
                }
                i = j - 1;
            }
        }
        return aliases;
    }
}