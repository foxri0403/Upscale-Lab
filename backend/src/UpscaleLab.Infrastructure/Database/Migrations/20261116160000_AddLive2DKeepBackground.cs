using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace UpscaleLab.Infrastructure.Database.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261116160000_AddLive2DKeepBackground")]
public sealed class AddLive2DKeepBackground : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<bool>("KeepBackground", "processing_jobs", type: "boolean", nullable: false, defaultValue: false);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn("KeepBackground", "processing_jobs");
}
