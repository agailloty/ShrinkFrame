using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShrinkFrame.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ShrinkFrameDbContext))]
[Migration("20260821133000_AddVideoCodec")]
public sealed class AddVideoCodec : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "DefaultVideoCodec",
            table: "Batches",
            type: "TEXT",
            maxLength: 16,
            nullable: false,
            defaultValue: "H264");

        migrationBuilder.AddColumn<string>(
            name: "VideoCodec",
            table: "Jobs",
            type: "TEXT",
            maxLength: 16,
            nullable: false,
            defaultValue: "H264");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "DefaultVideoCodec", table: "Batches");
        migrationBuilder.DropColumn(name: "VideoCodec", table: "Jobs");
    }
}
