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
}
