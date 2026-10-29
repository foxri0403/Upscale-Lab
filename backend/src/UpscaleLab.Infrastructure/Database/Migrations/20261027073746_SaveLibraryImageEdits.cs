using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UpscaleLab.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class SaveLibraryImageEdits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SavedEditJson",
                table: "wallpaper_assets",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SavedEditJson",
                table: "live_layer_projects",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SavedEditJson",
                table: "wallpaper_assets");

            migrationBuilder.DropColumn(
                name: "SavedEditJson",
                table: "live_layer_projects");
        }
    }
}
