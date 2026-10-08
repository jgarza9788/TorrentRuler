using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using TorrentRuler.Web;
using Xunit;

namespace TorrentRuler.Tests.Web;

public class ToastsTests
{
    private sealed class MemoryTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private static TempDataDictionary NewTempData() => new(new DefaultHttpContext(), new MemoryTempDataProvider());

    [Fact]
    public void Add_Twice_KeepsBothInOrder()
    {
        var tempData = NewTempData();

        Toasts.Add(tempData, ToastKind.Success, "Saved.");
        Toasts.Add(tempData, ToastKind.Error, "Then failed.", undoUrl: "/Rules?handler=RestoreDeleted");

        var toasts = Toasts.Peek(tempData);
        Assert.Equal(2, toasts.Count);
        Assert.Equal(new Toast("success", "Saved.", null), toasts[0]);
        Assert.Equal(new Toast("error", "Then failed.", "/Rules?handler=RestoreDeleted"), toasts[1]);
    }

    [Fact]
    public void Trigger_MergesWithAnExistingHxTriggerHeader()
    {
        var response = new DefaultHttpContext().Response;
        response.Headers["HX-Trigger"] = """{"other":1}""";

        Toasts.Trigger(response, ToastKind.Info, "Hello");

        using var doc = JsonDocument.Parse(response.Headers["HX-Trigger"].ToString());
        Assert.Equal(1, doc.RootElement.GetProperty("other").GetInt32());
        var toast = Assert.Single(doc.RootElement.GetProperty("qf-toast").EnumerateArray());
        Assert.Equal("info", toast.GetProperty("kind").GetString());
        Assert.Equal("Hello", toast.GetProperty("message").GetString());
    }

    [Fact]
    public void Trigger_Twice_AppendsToTheToastArray()
    {
        var response = new DefaultHttpContext().Response;

        Toasts.Trigger(response, ToastKind.Success, "One");
        Toasts.Trigger(response, ToastKind.Error, "Two");

        using var doc = JsonDocument.Parse(response.Headers["HX-Trigger"].ToString());
        Assert.Equal(["One", "Two"], doc.RootElement.GetProperty("qf-toast").EnumerateArray().Select(t => t.GetProperty("message").GetString()));
    }

    [Fact]
    public void Trigger_EscapesNonAsciiSafely()
    {
        // Response headers must be ASCII; a message with an em dash or quote must still round-trip.
        var response = new DefaultHttpContext().Response;

        Toasts.Trigger(response, ToastKind.Success, "Rule \"A\" saved — ok");

        var header = response.Headers["HX-Trigger"].ToString();
        Assert.True(header.All(c => c < 128), header);
        using var doc = JsonDocument.Parse(header);
        Assert.Equal("Rule \"A\" saved — ok", doc.RootElement.GetProperty("qf-toast")[0].GetProperty("message").GetString());
    }
}
