using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShrinkFrame.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddImmichPublication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "MetadataFileModifiedTime",
                table: "Jobs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PublicationCheckpoints",
                columns: table => new
                {
                    JobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DestinationConnectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAttemptId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Sha1Checksum = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    UploadAmbiguous = table.Column<bool>(type: "INTEGER", nullable: false),
                    PendingAlbumIdsJson = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    WarningsJson = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublicationCheckpoints", x => x.JobId);
                    table.ForeignKey(
                        name: "FK_PublicationCheckpoints_ImmichConnections_DestinationConnectionId",
                        column: x => x.DestinationConnectionId,
                        principalTable: "ImmichConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PublicationCheckpoints_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PublicationCheckpoints_DestinationConnectionId",
                table: "PublicationCheckpoints",
                column: "DestinationConnectionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PublicationCheckpoints");

            migrationBuilder.DropColumn(
                name: "MetadataFileModifiedTime",
                table: "Jobs");
        }
    }
}
