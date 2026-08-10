using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // EF-generated migration uses inline column arrays.

namespace ShrinkFrame.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Batches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    SourceKind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ConnectionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    DefaultCrf = table.Column<int>(type: "INTEGER", nullable: false),
                    DefaultEncoderPreset = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    DefaultMaximumResolution = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    DefaultAudioMode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    DefaultSuffix = table.Column<string>(type: "TEXT", maxLength: 33, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Batches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImmichConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    BaseUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    AllowInvalidCertificate = table.Column<bool>(type: "INTEGER", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastTestedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    DetectedVersion = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Compatibility = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    LastTestError = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    EncryptedApiKey = table.Column<byte[]>(type: "BLOB", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImmichConnections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceKind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SourceId = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    SourceConnectionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PresetId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Crf = table.Column<int>(type: "INTEGER", nullable: false),
                    EncoderPreset = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    MaximumResolution = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    AudioMode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Suffix = table.Column<string>(type: "TEXT", maxLength: 33, nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    PublicationState = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    NotBeneficialPublicationOverride = table.Column<bool>(type: "INTEGER", nullable: false),
                    PublishedAssetId = table.Column<string>(type: "TEXT", nullable: true),
                    SourceArtifactKey = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    OutputArtifactKey = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    MetadataFileName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    MetadataMimeType = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    MetadataSizeBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    MetadataDurationTicks = table.Column<long>(type: "INTEGER", nullable: true),
                    MetadataWidth = table.Column<int>(type: "INTEGER", nullable: true),
                    MetadataHeight = table.Column<int>(type: "INTEGER", nullable: true),
                    MetadataVideoCodec = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    MetadataCaptureTime = table.Column<long>(type: "INTEGER", nullable: true),
                    MetadataEffectiveRotation = table.Column<int>(type: "INTEGER", nullable: true),
                    MetadataDescription = table.Column<string>(type: "TEXT", nullable: true),
                    MetadataLatitude = table.Column<double>(type: "REAL", nullable: true),
                    MetadataLongitude = table.Column<double>(type: "REAL", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Jobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Jobs_Batches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "Batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JobAlbums",
                columns: table => new
                {
                    JobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    AlbumId = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobAlbums", x => new { x.JobId, x.Position });
                    table.ForeignKey(
                        name: "FK_JobAlbums_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JobAudioCodecs",
                columns: table => new
                {
                    JobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Codec = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobAudioCodecs", x => new { x.JobId, x.Position });
                    table.ForeignKey(
                        name: "FK_JobAudioCodecs_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JobProgress",
                columns: table => new
                {
                    JobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TransferBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    TransferTotalBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    CompressionPercentage = table.Column<double>(type: "REAL", nullable: true),
                    ProcessedTicks = table.Column<long>(type: "INTEGER", nullable: true),
                    Speed = table.Column<double>(type: "REAL", nullable: true),
                    ElapsedTicks = table.Column<long>(type: "INTEGER", nullable: true),
                    EstimatedRemainingTicks = table.Column<long>(type: "INTEGER", nullable: true),
                    FramesPerSecond = table.Column<double>(type: "REAL", nullable: true),
                    BitrateBitsPerSecond = table.Column<long>(type: "INTEGER", nullable: true),
                    OutputBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobProgress", x => x.JobId);
                    table.ForeignKey(
                        name: "FK_JobProgress_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PublicationAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    JobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CompletedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    Result = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ErrorSummary = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublicationAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PublicationAttempts_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ValidationFindings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    JobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ValidationFindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ValidationFindings_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Batches_History",
                table: "Batches",
                columns: new[] { "UpdatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ImmichConnections_IsDefault",
                table: "ImmichConnections",
                column: "IsDefault",
                unique: true,
                filter: "IsDefault = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_BatchHistory",
                table: "Jobs",
                columns: new[] { "BatchId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_Queue",
                table: "Jobs",
                columns: new[] { "State", "UpdatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_SourceDuplicate",
                table: "Jobs",
                columns: new[] { "SourceKind", "SourceConnectionId", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_PublicationAttempts_JobId_StartedAt",
                table: "PublicationAttempts",
                columns: new[] { "JobId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ValidationFindings_JobId",
                table: "ValidationFindings",
                column: "JobId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImmichConnections");

            migrationBuilder.DropTable(
                name: "JobAlbums");

            migrationBuilder.DropTable(
                name: "JobAudioCodecs");

            migrationBuilder.DropTable(
                name: "JobProgress");

            migrationBuilder.DropTable(
                name: "PublicationAttempts");

            migrationBuilder.DropTable(
                name: "ValidationFindings");

            migrationBuilder.DropTable(
                name: "Jobs");

            migrationBuilder.DropTable(
                name: "Batches");
        }
    }
}
