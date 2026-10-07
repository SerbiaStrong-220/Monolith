// Exodus dynamic market
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class EconomyMarketQuotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "economy_market_quotes",
                columns: table => new
                {
                    market_key = table.Column<string>(type: "text", nullable: false),
                    factor = table.Column<double>(type: "double precision", nullable: false),
                    trend = table.Column<float>(type: "real", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_economy_market_quotes", x => x.market_key);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "economy_market_quotes");
        }
    }
}
