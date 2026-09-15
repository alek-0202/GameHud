using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamesHud.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableRuntimeImageIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReconciledRetryAttempt",
                table: "provisioning_steps",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_provisioning_operations_Id_GameServerId",
                table: "provisioning_operations",
                columns: new[] { "Id", "GameServerId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_managed_game_servers_Id_GameId_RuntimeType",
                table: "managed_game_servers",
                columns: new[] { "Id", "GameId", "RuntimeType" });

            migrationBuilder.CreateTable(
                name: "provisioning_reconciliations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    OperationId = table.Column<string>(type: "TEXT", nullable: false),
                    StepId = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    OperationVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    Attempt = table.Column<int>(type: "INTEGER", nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    PriorStatus = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    PriorFailureType = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    PriorErrorCode = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    AppliedCode = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    ObservedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provisioning_reconciliations", x => x.Id);
                    table.CheckConstraint("CK_reconciliation_outcome", "Outcome IN ('effect_exists', 'effect_absent', 'ambiguous')");
                    table.ForeignKey(
                        name: "FK_provisioning_reconciliations_provisioning_operations_OperationId",
                        column: x => x.OperationId,
                        principalTable: "provisioning_operations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "runtime_image_intents",
                columns: table => new
                {
                    OperationId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    GameServerId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    GameId = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    RuntimeType = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Registry = table.Column<string>(type: "TEXT", maxLength: 253, nullable: false),
                    Repository = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    ApprovedDigest = table.Column<string>(type: "TEXT", maxLength: 71, nullable: false),
                    PlatformOs = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    PlatformArchitecture = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    PlatformVariant = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    ApprovalSource = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    VerifiedLocalImageId = table.Column<string>(type: "TEXT", maxLength: 71, nullable: true),
                    VerificationState = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    VerifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_runtime_image_intents", x => x.OperationId);
                    table.CheckConstraint("CK_runtime_image_digest", "length(ApprovedDigest) = 71 AND substr(ApprovedDigest, 1, 7) = 'sha256:' AND substr(ApprovedDigest, 8) NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_runtime_image_id", "VerifiedLocalImageId IS NULL OR (length(VerifiedLocalImageId) = 71 AND substr(VerifiedLocalImageId, 1, 7) = 'sha256:' AND substr(VerifiedLocalImageId, 8) NOT GLOB '*[^0-9a-f]*')");
                    table.CheckConstraint("CK_runtime_image_platform", "PlatformOs IN ('linux', 'windows') AND PlatformArchitecture IN ('amd64', 'arm64')");
                    table.CheckConstraint("CK_runtime_image_state", "(VerificationState = 'pending' AND VerifiedLocalImageId IS NULL AND VerifiedAtUtc IS NULL) OR (VerificationState = 'verified' AND VerifiedLocalImageId IS NOT NULL AND VerifiedAtUtc IS NOT NULL)");
                    table.CheckConstraint("CK_runtime_image_version", "Version > 0");
                    table.ForeignKey(
                        name: "FK_runtime_image_intents_managed_game_servers_GameServerId_GameId_RuntimeType",
                        columns: x => new { x.GameServerId, x.GameId, x.RuntimeType },
                        principalTable: "managed_game_servers",
                        principalColumns: new[] { "Id", "GameId", "RuntimeType" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_runtime_image_intents_provisioning_operations_OperationId_GameServerId",
                        columns: x => new { x.OperationId, x.GameServerId },
                        principalTable: "provisioning_operations",
                        principalColumns: new[] { "Id", "GameServerId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_provisioning_reconciliations_OperationId_OperationVersion",
                table: "provisioning_reconciliations",
                columns: new[] { "OperationId", "OperationVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_runtime_image_intents_GameServerId_GameId_RuntimeType",
                table: "runtime_image_intents",
                columns: new[] { "GameServerId", "GameId", "RuntimeType" });

            migrationBuilder.CreateIndex(
                name: "IX_runtime_image_intents_OperationId_GameServerId",
                table: "runtime_image_intents",
                columns: new[] { "OperationId", "GameServerId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "provisioning_reconciliations");

            migrationBuilder.DropTable(
                name: "runtime_image_intents");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_provisioning_operations_Id_GameServerId",
                table: "provisioning_operations");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_managed_game_servers_Id_GameId_RuntimeType",
                table: "managed_game_servers");

            migrationBuilder.DropColumn(
                name: "ReconciledRetryAttempt",
                table: "provisioning_steps");
        }
    }
}
