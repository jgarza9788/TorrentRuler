using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TorrentRuler.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInstanceLastTest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LastTestLatencyMs",
                table: "Instances",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastTestMessage",
                table: "Instances",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LastTestSucceeded",
                table: "Instances",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastTestedAt",
                table: "Instances",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastTestLatencyMs",
                table: "Instances");

            migrationBuilder.DropColumn(
                name: "LastTestMessage",
                table: "Instances");

            migrationBuilder.DropColumn(
                name: "LastTestSucceeded",
                table: "Instances");

            migrationBuilder.DropColumn(
                name: "LastTestedAt",
                table: "Instances");
        }
    }
}
