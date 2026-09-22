using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamesHud.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManagedGameServerRequestIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "managed_game_server_requests",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    IdempotencyKeyHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    GameServerId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    ProvisioningOperationId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_managed_game_server_requests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_managed_game_server_requests_managed_game_servers_GameServerId",
                        column: x => x.GameServerId,
                        principalTable: "managed_game_servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_managed_game_server_requests_provisioning_operations_ProvisioningOperationId",
                        column: x => x.ProvisioningOperationId,
                        principalTable: "provisioning_operations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_managed_game_server_requests_GameServerId",
                table: "managed_game_server_requests",
                column: "GameServerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_managed_game_server_requests_IdempotencyKeyHash",
                table: "managed_game_server_requests",
                column: "IdempotencyKeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_managed_game_server_requests_ProvisioningOperationId",
                table: "managed_game_server_requests",
                column: "ProvisioningOperationId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "managed_game_server_requests");
        }
    }
}
