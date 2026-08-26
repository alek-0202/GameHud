using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamesHud.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProvisioningStateMachine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PipelineVersion",
                table: "provisioning_operations",
                type: "TEXT",
                maxLength: 40,
                nullable: false,
                defaultValue: "legacy-gh08");

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "provisioning_operations",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "provisioning_steps",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    OperationId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    StepId = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Attempt = table.Column<int>(type: "INTEGER", nullable: false),
                    RetryClassification = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    SideEffectClassification = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    MaxAttempts = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    FailureType = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    SafeErrorMessage = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CompensationStartedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CompensationCompletedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provisioning_steps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_provisioning_steps_provisioning_operations_OperationId",
                        column: x => x.OperationId,
                        principalTable: "provisioning_operations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_provisioning_steps_OperationId_Sequence",
                table: "provisioning_steps",
                columns: new[] { "OperationId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_provisioning_steps_OperationId_StepId",
                table: "provisioning_steps",
                columns: new[] { "OperationId", "StepId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "provisioning_steps");

            migrationBuilder.DropColumn(
                name: "PipelineVersion",
                table: "provisioning_operations");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "provisioning_operations");
        }
    }
}
