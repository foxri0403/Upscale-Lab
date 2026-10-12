using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UpscaleLab.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddGalleryMultiTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "Tags",
                table: "gallery_posts",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);
            migrationBuilder.Sql("UPDATE gallery_posts SET \"Tags\" = ARRAY[\"Tag\"];");
            migrationBuilder.CreateIndex(
                name: "IX_gallery_posts_Tags",
                table: "gallery_posts",
                column: "Tags")
                .Annotation("Npgsql:IndexMethod", "gin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_gallery_posts_Tags", table: "gallery_posts");
            migrationBuilder.DropColumn(
                name: "Tags",
                table: "gallery_posts");
        }
    }
}
