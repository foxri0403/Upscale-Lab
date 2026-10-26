using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UpscaleLab.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddWebDeviceControl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Operation",
                table: "processing_jobs",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Layers");

            migrationBuilder.AddColumn<int>(
                name: "ResultHeight",
                table: "processing_jobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ResultObjectKey",
                table: "processing_jobs",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ResultWidth",
                table: "processing_jobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UpscaleFactor",
                table: "processing_jobs",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<string>(
                name: "AgentTokenHash",
                table: "devices",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgentVersion",
                table: "devices",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "CommandSequence",
                table: "devices",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "ControlVersion",
                table: "devices",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<long>(
                name: "InventoryRevision",
                table: "devices",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastSeenAt",
                table: "devices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OsVersion",
                table: "devices",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "SupportsLive2D",
                table: "devices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SupportsLockScreen",
                table: "devices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "device_displays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    NativeId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: false),
                    Height = table.Column<int>(type: "integer", nullable: false),
                    Scale = table.Column<double>(type: "double precision", nullable: false),
                    Rotation = table.Column<int>(type: "integer", nullable: false),
                    OffsetX = table.Column<int>(type: "integer", nullable: false),
                    OffsetY = table.Column<int>(type: "integer", nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    IsConnected = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_displays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_device_displays_devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wallpaper_assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ManifestJson = table.Column<string>(type: "jsonb", nullable: false),
                    IsPublic = table.Column<bool>(type: "boolean", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wallpaper_assets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_wallpaper_assets_live_layer_projects_SourceProjectId",
                        column: x => x.SourceProjectId,
                        principalTable: "live_layer_projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_wallpaper_assets_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "device_commands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceDisplayId = table.Column<Guid>(type: "uuid", nullable: false),
                    WallpaperAssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Target = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_commands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_device_commands_device_displays_DeviceDisplayId",
                        column: x => x.DeviceDisplayId,
                        principalTable: "device_displays",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_device_commands_devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_device_commands_wallpaper_assets_WallpaperAssetId",
                        column: x => x.WallpaperAssetId,
                        principalTable: "wallpaper_assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "wallpaper_assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceDisplayId = table.Column<Guid>(type: "uuid", nullable: false),
                    Target = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DesiredAssetId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReportedAssetId = table.Column<Guid>(type: "uuid", nullable: true),
                    Observation = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    LastReportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wallpaper_assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_wallpaper_assignments_device_displays_DeviceDisplayId",
                        column: x => x.DeviceDisplayId,
                        principalTable: "device_displays",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_wallpaper_assignments_wallpaper_assets_DesiredAssetId",
                        column: x => x.DesiredAssetId,
                        principalTable: "wallpaper_assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_wallpaper_assignments_wallpaper_assets_ReportedAssetId",
                        column: x => x.ReportedAssetId,
                        principalTable: "wallpaper_assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_processing_jobs_ProjectId",
                table: "processing_jobs",
                column: "ProjectId",
                unique: true,
                filter: "\"Status\" IN ('Queued', 'Processing')");

            migrationBuilder.CreateIndex(
                name: "IX_devices_AgentTokenHash",
                table: "devices",
                column: "AgentTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_device_commands_DeviceDisplayId",
                table: "device_commands",
                column: "DeviceDisplayId");

            migrationBuilder.CreateIndex(
                name: "IX_device_commands_DeviceId_RequestId",
                table: "device_commands",
                columns: new[] { "DeviceId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_device_commands_DeviceId_Sequence",
                table: "device_commands",
                columns: new[] { "DeviceId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_device_commands_DeviceId_Status_ExpiresAt",
                table: "device_commands",
                columns: new[] { "DeviceId", "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_device_commands_WallpaperAssetId",
                table: "device_commands",
                column: "WallpaperAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_device_displays_DeviceId_NativeId",
                table: "device_displays",
                columns: new[] { "DeviceId", "NativeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wallpaper_assets_IsPublic_IsArchived_CreatedAt",
                table: "wallpaper_assets",
                columns: new[] { "IsPublic", "IsArchived", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_wallpaper_assets_SourceProjectId",
                table: "wallpaper_assets",
                column: "SourceProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_wallpaper_assets_UserId_CreatedAt",
                table: "wallpaper_assets",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_wallpaper_assignments_DesiredAssetId",
                table: "wallpaper_assignments",
                column: "DesiredAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_wallpaper_assignments_DeviceDisplayId_Target",
                table: "wallpaper_assignments",
                columns: new[] { "DeviceDisplayId", "Target" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wallpaper_assignments_ReportedAssetId",
                table: "wallpaper_assignments",
                column: "ReportedAssetId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "device_commands");

            migrationBuilder.DropTable(
                name: "wallpaper_assignments");

            migrationBuilder.DropTable(
                name: "device_displays");

            migrationBuilder.DropTable(
                name: "wallpaper_assets");

            migrationBuilder.DropIndex(
                name: "IX_processing_jobs_ProjectId",
                table: "processing_jobs");

            migrationBuilder.DropIndex(
                name: "IX_devices_AgentTokenHash",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "Operation",
                table: "processing_jobs");

            migrationBuilder.DropColumn(
                name: "ResultHeight",
                table: "processing_jobs");

            migrationBuilder.DropColumn(
                name: "ResultObjectKey",
                table: "processing_jobs");

            migrationBuilder.DropColumn(
                name: "ResultWidth",
                table: "processing_jobs");

            migrationBuilder.DropColumn(
                name: "UpscaleFactor",
                table: "processing_jobs");

            migrationBuilder.DropColumn(
                name: "AgentTokenHash",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "AgentVersion",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "CommandSequence",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "ControlVersion",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "InventoryRevision",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "LastSeenAt",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "OsVersion",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "SupportsLive2D",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "SupportsLockScreen",
                table: "devices");
        }
    }
}
