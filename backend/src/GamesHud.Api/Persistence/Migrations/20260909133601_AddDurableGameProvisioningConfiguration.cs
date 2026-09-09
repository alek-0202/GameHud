using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamesHud.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableGameProvisioningConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "managed_game_configurations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    GameServerId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    GameId = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    ConfigurationKind = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    Payload = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_managed_game_configurations", x => x.Id);
                    table.CheckConstraint("CK_managed_game_configurations_schema_version", "\"SchemaVersion\" > 0");
                    table.ForeignKey(
                        name: "FK_managed_game_configurations_managed_game_servers_GameServerId",
                        column: x => x.GameServerId,
                        principalTable: "managed_game_servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_managed_game_configurations_GameServerId_ConfigurationKind",
                table: "managed_game_configurations",
                columns: new[] { "GameServerId", "ConfigurationKind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "managed_game_configurations");
        }
    }
}
