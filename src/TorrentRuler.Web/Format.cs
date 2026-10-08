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
}
