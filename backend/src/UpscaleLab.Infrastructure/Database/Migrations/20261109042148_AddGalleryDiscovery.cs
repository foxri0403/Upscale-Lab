using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UpscaleLab.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddGalleryDiscovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "SaveCount",
                table: "wallpaper_assets",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string[]>(
                name: "PlatformTags",
                table: "gallery_posts",
                type: "text[]",
                nullable: false,
                defaultValueSql: "ARRAY[]::text[]");

            migrationBuilder.AddColumn<long>(
                name: "SaveCount",
                table: "gallery_posts",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "wallpaper_likes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WallpaperAssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wallpaper_likes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_wallpaper_likes_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_wallpaper_likes_wallpaper_assets_WallpaperAssetId",
                        column: x => x.WallpaperAssetId,
                        principalTable: "wallpaper_assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_gallery_posts_PlatformTags",
                table: "gallery_posts",
                column: "PlatformTags")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_wallpaper_likes_UserId",
                table: "wallpaper_likes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_wallpaper_likes_WallpaperAssetId_UserId",
                table: "wallpaper_likes",
                columns: new[] { "WallpaperAssetId", "UserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wallpaper_likes");

            migrationBuilder.DropIndex(
                name: "IX_gallery_posts_PlatformTags",
                table: "gallery_posts");

            migrationBuilder.DropColumn(
                name: "SaveCount",
                table: "wallpaper_assets");

            migrationBuilder.DropColumn(
                name: "PlatformTags",
                table: "gallery_posts");

            migrationBuilder.DropColumn(
                name: "SaveCount",
                table: "gallery_posts");
        }
    }
}
