using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UpscaleLab.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddInpaintingRunPodJobId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RunPodJobId",
                table: "inpainting_jobs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RunPodJobId",
                table: "inpainting_jobs");
        }
    }
}
