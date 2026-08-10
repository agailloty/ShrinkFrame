using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShrinkFrame.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddImmichConnectionTestDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastTestKeyId",
                table: "ImmichConnections",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastTestKeyName",
                table: "ImmichConnections",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastTestPermissions",
                table: "ImmichConnections",
                type: "TEXT",
                maxLength: 4000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastTestKeyId",
                table: "ImmichConnections");

            migrationBuilder.DropColumn(
                name: "LastTestKeyName",
                table: "ImmichConnections");

            migrationBuilder.DropColumn(
                name: "LastTestPermissions",
                table: "ImmichConnections");
        }
    }
}
