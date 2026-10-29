using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UpscaleLab.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddUpscaleTargetResolution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TargetLongEdge",
                table: "processing_jobs",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TargetLongEdge",
                table: "processing_jobs");
        }
    }
}
