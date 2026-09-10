// SS220 chat ban port: Exodus migration for the existing ban database.
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Content.Server.Database.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class SS220ChatBans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ban_chat",
                columns: table => new
                {
                    ban_chat_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    chat = table.Column<byte>(type: "smallint", nullable: false),
                    ban_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ban_chat", x => x.ban_chat_id);
                    table.CheckConstraint("ValidChatBanChannel", "chat IN (1, 2, 3)");
                    table.ForeignKey(
                        name: "FK_ban_chat_ban_ban_id",
                        column: x => x.ban_id,
                        principalTable: "ban",
                        principalColumn: "ban_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ban_chat_ban_id",
                table: "ban_chat",
                column: "ban_id");

            migrationBuilder.CreateIndex(
                name: "IX_ban_chat_chat_ban_id",
                table: "ban_chat",
                columns: new[] { "chat", "ban_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ban_chat");
        }
    }
}
