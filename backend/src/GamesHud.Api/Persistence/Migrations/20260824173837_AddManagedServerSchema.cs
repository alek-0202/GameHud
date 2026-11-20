using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamesHud.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManagedServerSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "managed_game_servers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    GameId = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    InstallationType = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    RuntimeType = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    LifecycleState = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_managed_game_servers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "provisioning_operations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    GameServerId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    ActiveSlot = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    CurrentStep = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    ErrorMessageSafe = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provisioning_operations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_provisioning_operations_managed_game_servers_GameServerId",
                        column: x => x.GameServerId,
                        principalTable: "managed_game_servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "port_reservations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    GameServerId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    PortDefinitionId = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Protocol = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Port = table.Column<int>(type: "INTEGER", nullable: false),
                    Exposure = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    ProvisioningOperationId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_port_reservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_port_reservations_managed_game_servers_GameServerId",
                        column: x => x.GameServerId,
                        principalTable: "managed_game_servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_port_reservations_provisioning_operations_ProvisioningOperationId",
                        column: x => x.ProvisioningOperationId,
                        principalTable: "provisioning_operations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "storage_reservations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    GameServerId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    StorageDefinitionId = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Ownership = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    ProvisioningOperationId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_storage_reservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_storage_reservations_managed_game_servers_GameServerId",
                        column: x => x.GameServerId,
                        principalTable: "managed_game_servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_storage_reservations_provisioning_operations_ProvisioningOperationId",
                        column: x => x.ProvisioningOperationId,
                        principalTable: "provisioning_operations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_port_reservations_GameServerId_PortDefinitionId",
                table: "port_reservations",
                columns: new[] { "GameServerId", "PortDefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_port_reservations_Protocol_Port",
                table: "port_reservations",
                columns: new[] { "Protocol", "Port" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_port_reservations_ProvisioningOperationId",
                table: "port_reservations",
                column: "ProvisioningOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_provisioning_operations_GameServerId",
                table: "provisioning_operations",
                column: "GameServerId");

            migrationBuilder.CreateIndex(
                name: "IX_provisioning_operations_GameServerId_Type_ActiveSlot",
                table: "provisioning_operations",
                columns: new[] { "GameServerId", "Type", "ActiveSlot" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_storage_reservations_GameServerId_StorageDefinitionId",
                table: "storage_reservations",
                columns: new[] { "GameServerId", "StorageDefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_storage_reservations_ProvisioningOperationId",
                table: "storage_reservations",
                column: "ProvisioningOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_storage_reservations_RelativePath",
                table: "storage_reservations",
                column: "RelativePath",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "port_reservations");

            migrationBuilder.DropTable(
                name: "storage_reservations");

            migrationBuilder.DropTable(
                name: "provisioning_operations");

            migrationBuilder.DropTable(
                name: "managed_game_servers");
        }
    }
}
