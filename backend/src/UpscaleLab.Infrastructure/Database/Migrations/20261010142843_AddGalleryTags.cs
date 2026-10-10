using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UpscaleLab.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddGalleryTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Tag",
                table: "gallery_posts",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "장르 설정되지 않음");

            migrationBuilder.CreateIndex(
                name: "IX_gallery_posts_Tag_CreatedAt",
                table: "gallery_posts",
                columns: new[] { "Tag", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_gallery_posts_Tag_CreatedAt",
                table: "gallery_posts");

            migrationBuilder.DropColumn(
                name: "Tag",
                table: "gallery_posts");
        }
    }
}
