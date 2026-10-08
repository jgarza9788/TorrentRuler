using System.Text.Json;

namespace TorrentRuler.Sources.Json;

/// <summary>Minimal dot-path traversal of a JsonElement tree, used to make the REST-history adapters' response shape configurable.</summary>
internal static class JsonPathResolver
{
    public static JsonElement? Resolve(JsonElement root, string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return root;
        }

        var current = root;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out var next))
            {
                return null;
            }
            current = next;
        }

        return current;
    }

    /// <summary>
    /// Reads a string (or number, as text) property. <paramref name="fieldName"/> may list
    /// fallbacks separated by '|' -- "EpisodeId|NowPlayingItemId" takes the first one that is
    /// present and non-empty.
    /// </summary>
    public static string? GetString(JsonElement element, string? fieldName)
    {
        if (fieldName is not null && fieldName.Contains('|'))
        {
            foreach (var alternative in fieldName.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var found = GetString(element, alternative);
                if (!string.IsNullOrEmpty(found))
                {
                    return found;
                }
            }
            return null;
        }

        if (string.IsNullOrEmpty(fieldName) || element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(fieldName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            _ => null
        };
    }

    /// <summary>
    /// Depth-first search for the first non-empty string property named <paramref name="fieldName"/>
    /// (case-insensitive) anywhere under <paramref name="element"/>, including inside arrays. For
    /// responses whose nesting varies by item type, e.g. Jellystat's getItemDetails.
    /// </summary>
    public static string? FindString(JsonElement element, string fieldName)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    if (string.Equals(prop.Name, fieldName, StringComparison.OrdinalIgnoreCase)
                        && prop.Value.ValueKind == JsonValueKind.String
                        && !string.IsNullOrEmpty(prop.Value.GetString()))
                    {
                        return prop.Value.GetString();
                    }
                }
                foreach (var prop in element.EnumerateObject())
                {
                    if (FindString(prop.Value, fieldName) is { } nested)
                    {
                        return nested;
                    }
                }
                return null;
            case JsonValueKind.Array:
                foreach (var child in element.EnumerateArray())
                {
                    if (FindString(child, fieldName) is { } nested)
                    {
                        return nested;
                    }
                }
                return null;
            default:
                return null;
        }
    }

    public static double? GetDouble(JsonElement element, string? fieldName)
    {
        if (string.IsNullOrEmpty(fieldName) || element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(fieldName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.String when double.TryParse(value.GetString(), out var d) => d,
            _ => null
        };
    }

    public static DateTimeOffset? GetUnixSeconds(JsonElement element, string? fieldName)
    {
        var value = GetDouble(element, fieldName);
        if (value is > 0)
        {
            return DateTimeOffset.FromUnixTimeSeconds((long)value.Value);
        }

        // Some sources (e.g. Jellystat) report ISO-8601 timestamps rather than epoch seconds.
        return DateTimeOffset.TryParse(GetString(element, fieldName), System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;
    }
}
