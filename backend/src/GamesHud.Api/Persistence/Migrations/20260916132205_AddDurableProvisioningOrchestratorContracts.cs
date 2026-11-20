using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamesHud.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableProvisioningOrchestratorContracts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_port_reservations_Protocol_Port",
                table: "port_reservations");

            migrationBuilder.AddColumn<string>(
                name: "ApiPath",
                table: "storage_reservations",
                type: "TEXT",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "HostPath",
                table: "storage_reservations",
                type: "TEXT",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ContainerPort",
                table: "port_reservations",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "HostPort",
                table: "port_reservations",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Published",
                table: "port_reservations",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""
                UPDATE port_reservations
                SET ContainerPort = Port,
                    HostPort = CASE WHEN Exposure = 'public' THEN Port ELSE NULL END,
                    Published = CASE WHEN Exposure = 'public' THEN 1 ELSE 0 END;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_port_reservations_Protocol_HostPort",
                table: "port_reservations",
                columns: new[] { "Protocol", "HostPort" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_port_reservations_Protocol_HostPort",
                table: "port_reservations");

            migrationBuilder.DropColumn(
                name: "ApiPath",
                table: "storage_reservations");

            migrationBuilder.DropColumn(
                name: "HostPath",
                table: "storage_reservations");

            migrationBuilder.DropColumn(
                name: "ContainerPort",
                table: "port_reservations");

            migrationBuilder.DropColumn(
                name: "HostPort",
                table: "port_reservations");

            migrationBuilder.DropColumn(
                name: "Published",
                table: "port_reservations");

            migrationBuilder.CreateIndex(
                name: "IX_port_reservations_Protocol_Port",
                table: "port_reservations",
                columns: new[] { "Protocol", "Port" },
                unique: true);
        }
    }
}
