using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TorrentRuler.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Advanced SQL used to be only the expression after a fixed "SELECT … FROM qbittorrent t WHERE";
    /// it is now the whole query. Renames the column and prepends that header to every stored
    /// expression. The header is a literal here, not AdvancedSqlTemplate.Header: a migration must
    /// keep producing the same rows even if the template later changes.
    /// </summary>
    public partial class RenameAdvancedSqlWhereToAdvancedSql : Migration
    {
        private const string HeaderSql =
            "'SELECT DISTINCT t.instance_id AS instance_id, t.hash AS torrent_hash' || char(10) || 'FROM qbittorrent t' || char(10) || 'WHERE '";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "AdvancedSqlWhere",
                table: "Rules",
                newName: "AdvancedSql");

            // Only non-blank expressions; UseAdvancedSql is never touched, so basic rules are unaffected.
            migrationBuilder.Sql(
                $"UPDATE Rules SET AdvancedSql = {HeaderSql} || trim(AdvancedSql) WHERE AdvancedSql IS NOT NULL AND trim(AdvancedSql) <> '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Strip the header back off where it is still exactly what Up added; a query edited into
            // some other shape since can't be reduced to a WHERE expression and is left as written.
            migrationBuilder.Sql(
                $"UPDATE Rules SET AdvancedSql = substr(AdvancedSql, length({HeaderSql}) + 1) WHERE substr(AdvancedSql, 1, length({HeaderSql})) = {HeaderSql};");

            migrationBuilder.RenameColumn(
                name: "AdvancedSql",
                table: "Rules",
                newName: "AdvancedSqlWhere");
        }
    }
}
