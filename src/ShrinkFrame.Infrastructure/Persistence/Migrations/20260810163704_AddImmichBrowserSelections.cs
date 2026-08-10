using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShrinkFrame.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddImmichBrowserSelections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ImmichBrowserSelections",
                columns: table => new
                {
                    ConnectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssetId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SelectedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImmichBrowserSelections", x => new { x.ConnectionId, x.AssetId });
                    table.ForeignKey(
                        name: "FK_ImmichBrowserSelections_ImmichConnections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "ImmichConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImmichBrowserSelections");
        }
    }
}
