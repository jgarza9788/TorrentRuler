using TorrentRuler.Engine.Conditions.AdvancedSql;
using Xunit;

namespace TorrentRuler.Tests.Engine;

public class SqlGuardTests
{
    [Theory]
    [InlineData("SELECT 1")]
    [InlineData("  with x AS (SELECT 1) SELECT * FROM x")]
    [InlineData("SELECT * FROM qbittorrent t WHERE t.name LIKE '%update%'")]
    [InlineData("SELECT * FROM qbittorrent t WHERE t.category = 'Delete Me'")]
    [InlineData("SELECT replace(t.name, '.', ' ') FROM qbittorrent t")]
    [InlineData("SELECT * FROM pragma_table_info('qbittorrent')")]
    [InlineData("SELECT 1 -- ; DROP TABLE x\n")]
    [InlineData("SELECT 1 /* DELETE */;")]
    public void Check_Allows(string sql) => Assert.Null(SqlGuard.Check(sql));

    [Theory]
    [InlineData("", "SQL cannot be empty.")]
    [InlineData("SELECT 1; SELECT 2", "Only a single statement is allowed.")]
    [InlineData("UPDATE qbittorrent SET name = 'x'", "Only SELECT or WITH … SELECT queries are allowed.")]
    [InlineData("WITH x AS (SELECT 1) DELETE FROM qbittorrent", "'DELETE' is not allowed.")]
    [InlineData("SELECT * FROM pragma_table_info('x') WHERE 1 AND (PRAGMA)", "'PRAGMA' is not allowed.")]
    [InlineData("SELECT 1 WHERE 1 IN (REPLACE INTO x VALUES (1))", "'REPLACE' is not allowed.")]
    [InlineData("EXPLAIN SELECT 1", "Only SELECT or WITH … SELECT queries are allowed.")]
    public void Check_Rejects(string sql, string message) => Assert.Equal(message, SqlGuard.Check(sql));

    [Fact]
    public void Check_AllowsExplain_WhenAsked() => Assert.Null(SqlGuard.Check("EXPLAIN QUERY PLAN SELECT 1", allowExplain: true));
}
