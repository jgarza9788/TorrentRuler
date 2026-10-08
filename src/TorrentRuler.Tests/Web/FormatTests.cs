using TorrentRuler.Web;
using Xunit;

namespace TorrentRuler.Tests.Web;

public class FormatTests
{
    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(999L, "999 B")]
    [InlineData(1_500L, "1.5 KB")]
    [InlineData(5_368_709_120L, "5.37 GB")]
    [InlineData(2_000_398_934_016L, "2 TB")]
    [InlineData(-1L, "—")]
    public void Bytes_UsesDecimalUnits(long bytes, string expected) => Assert.Equal(expected, Format.Bytes(bytes));

    [Theory]
    [InlineData(-5, "now")]
    [InlineData(20, "now")]
    [InlineData(3 * 60 + 10, "in 3 min")]
    [InlineData(2 * 3600 + 60, "in 2 hr")]
    [InlineData(3 * 86400, "in 3 days")]
    public void Until_DescribesAFutureTime(int seconds, string expected)
    {
        var now = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(expected, Format.Until(now.AddSeconds(seconds), now));
    }
}
