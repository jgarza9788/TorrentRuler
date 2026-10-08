using TorrentRuler.Core.Domain;
using Xunit;

namespace TorrentRuler.Tests.Engine;

public class AdvancedSqlTemplateTests
{
    [Fact]
    public void FromLegacyWhere_PrependsHeader() =>
        Assert.Equal("SELECT DISTINCT t.instance_id AS instance_id, t.hash AS torrent_hash\nFROM qbittorrent t\nWHERE t.ratio > 2",
                     AdvancedSqlTemplate.FromLegacyWhere("  t.ratio > 2 "));

    [Theory, InlineData(null), InlineData(""), InlineData("   ")]
    public void FromLegacyWhere_LeavesBlankAlone(string? where) => Assert.Equal(where, AdvancedSqlTemplate.FromLegacyWhere(where));

    [Fact]
    public void NewRule_EndsWithAnAlwaysTrueWhere() => Assert.EndsWith("WHERE 1 = 1", AdvancedSqlTemplate.NewRule);
}
