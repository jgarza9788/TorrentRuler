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

    /// <summary>
    /// The first non-null property named <paramref name="fieldName"/> (case-insensitive): the element's
    /// own properties first, then nested ones, descending through arrays in order. Like
    /// <see cref="FindString"/> but for any value type -- an array of genres, a number.
    /// </summary>
    public static JsonElement? FindElement(JsonElement element, string fieldName)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    if (string.Equals(prop.Name, fieldName, StringComparison.OrdinalIgnoreCase)
                        && prop.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
                    {
                        return prop.Value;
                    }
                }
                foreach (var prop in element.EnumerateObject())
                {
                    if (FindElement(prop.Value, fieldName) is { } nested)
                    {
                        return nested;
                    }
                }
                return null;
            case JsonValueKind.Array:
                foreach (var child in element.EnumerateArray())
                {
                    if (FindElement(child, fieldName) is { } nested)
                    {
                        return nested;
                    }
                }
                return null;
            default:
                return null;
        }
    }

    /// <summary>
    /// A list of names as one comma-separated string: from an array of strings, an array of
    /// <c>{ "Name": … }</c> objects, or a string that is already a list. Null when there is nothing in it.
    /// </summary>
    public static string? ToCommaList(JsonElement? value)
    {
        if (value is not { } v)
        {
            return null;
        }

        IEnumerable<string?> names = v.ValueKind switch
        {
            JsonValueKind.Array => v.EnumerateArray().Select(e => e.ValueKind switch
            {
                JsonValueKind.String => e.GetString(),
                JsonValueKind.Object => GetString(e, "Name"),
                _ => null
            }),
            JsonValueKind.String => [v.GetString()],
            _ => []
        };

        var list = names.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!.Trim()).ToList();
        return list.Count > 0 ? string.Join(",", list) : null;
    }

    /// <summary>A JSON number, or a string holding one (invariant culture), as a double; otherwise null.</summary>
    public static double? ToDouble(JsonElement? value) => value switch
    {
        { ValueKind: JsonValueKind.Number } v => v.GetDouble(),
        { ValueKind: JsonValueKind.String } v when double.TryParse(v.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) => d,
        _ => null
    };

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
