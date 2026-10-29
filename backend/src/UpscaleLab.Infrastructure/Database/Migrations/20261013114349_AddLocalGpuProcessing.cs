using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UpscaleLab.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddLocalGpuProcessing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LayerEngine",
                table: "processing_jobs",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "SeeThrough");

            migrationBuilder.AddColumn<string>(
                name: "Operation",
                table: "processing_jobs",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Layers");

            migrationBuilder.AddColumn<int>(
                name: "OutputHeight",
                table: "processing_jobs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OutputWidth",
                table: "processing_jobs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UpscaleFactor",
                table: "processing_jobs",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<string>(
                name: "UpscaledObjectKey",
                table: "processing_jobs",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_processing_jobs_ProjectId",
                table: "processing_jobs",
                column: "ProjectId",
                unique: true,
                filter: "\"Status\" IN ('Queued', 'Processing')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_processing_jobs_ProjectId",
                table: "processing_jobs");

            migrationBuilder.DropColumn(
                name: "LayerEngine",
                table: "processing_jobs");

            migrationBuilder.DropColumn(
                name: "Operation",
                table: "processing_jobs");

            migrationBuilder.DropColumn(
                name: "OutputHeight",
                table: "processing_jobs");

            migrationBuilder.DropColumn(
                name: "OutputWidth",
                table: "processing_jobs");

            migrationBuilder.DropColumn(
                name: "UpscaleFactor",
                table: "processing_jobs");

            migrationBuilder.DropColumn(
                name: "UpscaledObjectKey",
                table: "processing_jobs");
        }
    }
}
