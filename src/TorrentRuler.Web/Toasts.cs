using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace TorrentRuler.Web;

public enum ToastKind { Success, Error, Info }

/// <summary>One toast as the browser receives it. <c>Kind</c> is "success" | "error" | "info".</summary>
public sealed record Toast(string Kind, string Message, string? UndoUrl);

/// <summary>
/// The one way handlers raise a toast. A full-page post (redirect then GET) uses <see cref="Add"/>,
/// which survives the redirect in TempData and is rendered by the layout's toast host; an htmx
/// request uses <see cref="Trigger"/>, which rides the response's <c>HX-Trigger</c> header and is
/// picked up by site.js. Both carry the same <see cref="Toast"/> shape.
/// </summary>
public static class Toasts
{
    public const string TempDataKey = "qf.toasts";
    private const string HxTrigger = "HX-Trigger";
    private const string EventName = "qf-toast";

    // Default encoder escapes non-ASCII as \uXXXX, which keeps header values ASCII-only.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void Add(ITempDataDictionary tempData, ToastKind kind, string message, string? undoUrl = null)
    {
        var toasts = Peek(tempData).ToList();
        toasts.Add(new Toast(KindName(kind), message, undoUrl));
        tempData[TempDataKey] = JsonSerializer.Serialize(toasts, Json);
    }

    /// <summary>Toasts queued in TempData, without consuming them.</summary>
    public static IReadOnlyList<Toast> Peek(ITempDataDictionary tempData) =>
        tempData.Peek(TempDataKey) is string json ? JsonSerializer.Deserialize<List<Toast>>(json, Json) ?? [] : [];

    /// <summary>Toasts queued in TempData, consuming them (the layout calls this once per page).</summary>
    public static IReadOnlyList<Toast> Take(ITempDataDictionary tempData)
    {
        var toasts = Peek(tempData);
        tempData.Remove(TempDataKey);
        return toasts;
    }

    public static void Trigger(HttpResponse response, ToastKind kind, string message)
    {
        var existing = response.Headers[HxTrigger].ToString();
        var root = !string.IsNullOrWhiteSpace(existing) && JsonNode.Parse(existing) is JsonObject obj ? obj : [];
        if (root[EventName] is not JsonArray list)
        {
            list = [];
            root[EventName] = list;
        }
        list.Add(JsonSerializer.SerializeToNode(new Toast(KindName(kind), message, null), Json));
        response.Headers[HxTrigger] = root.ToJsonString(Json);
    }

    private static string KindName(ToastKind kind) => kind.ToString().ToLowerInvariant();
}
