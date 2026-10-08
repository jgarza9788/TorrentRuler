using System.Globalization;

namespace TorrentRuler.Web;

/// <summary>Display formatting shared by the pages.</summary>
public static class Format
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB"];

    /// <summary>
    /// Bytes in decimal units (1 GB = 1e9 bytes, matching the size_gb SQL helper), up to three
    /// significant digits: "5.37 GB", "1.5 KB", "2 TB". Negative values (unknown) render as "—".
    /// </summary>
    public static string Bytes(long bytes)
    {
        if (bytes < 0)
        {
            return "—";
        }

        double value = bytes;
        var unit = 0;
        while (value >= 1000 && unit < Units.Length - 1)
        {
            value /= 1000;
            unit++;
        }

        var text = unit == 0 || value >= 100
            ? value.ToString("0", CultureInfo.InvariantCulture)
            : value.ToString(value >= 10 ? "0.#" : "0.##", CultureInfo.InvariantCulture);
        return $"{text} {Units[unit]}";
    }

    /// <summary>How far <paramref name="when"/> is from <paramref name="now"/>: "now", "in 3 min", "in 2 hr", "in 3 days".</summary>
    public static string Until(DateTimeOffset when, DateTimeOffset now)
    {
        var span = when - now;
        if (span < TimeSpan.FromSeconds(45))
        {
            return "now";
        }
        if (span < TimeSpan.FromHours(1))
        {
            return $"in {(int)Math.Ceiling(span.TotalMinutes - 0.5)} min";
        }
        if (span < TimeSpan.FromDays(1))
        {
            return $"in {(int)span.TotalHours} hr";
        }
        var days = (int)span.TotalDays;
        return $"in {days} day{(days == 1 ? "" : "s")}";
    }
}
