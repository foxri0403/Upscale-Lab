using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UpscaleLab.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceWallpaperRemoval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SupportsWallpaperRemoval",
                table: "devices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<Guid>(
                name: "WallpaperAssetId",
                table: "device_commands",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "Action",
                table: "device_commands",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Apply");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $guard$ BEGIN
                    IF EXISTS (SELECT 1 FROM device_commands WHERE "WallpaperAssetId" IS NULL) THEN
                        RAISE EXCEPTION 'Wallpaper removal commands exist; migrate their history before reverting this schema.';
                    END IF;
                END $guard$;
                """);
            migrationBuilder.DropColumn(
                name: "SupportsWallpaperRemoval",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "Action",
                table: "device_commands");

            migrationBuilder.AlterColumn<Guid>(
                name: "WallpaperAssetId",
                table: "device_commands",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
