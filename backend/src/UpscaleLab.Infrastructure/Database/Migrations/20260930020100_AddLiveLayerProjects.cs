using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UpscaleLab.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddLiveLayerProjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "SensorSensitivity",
                table: "user_settings",
                type: "double precision",
                nullable: false,
                defaultValue: 1.0);

            migrationBuilder.AddColumn<bool>(
                name: "IsPublic",
                table: "gallery_posts",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "gallery_posts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "live_layer_projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalImageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_live_layer_projects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_live_layer_projects_images_OriginalImageId",
                        column: x => x.OriginalImageId,
                        principalTable: "images",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_live_layer_projects_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "image_layers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    LayerType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    LayerOrder = table.Column<int>(type: "integer", nullable: false),
                    ImageUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    ObjectKey = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Depth = table.Column<double>(type: "double precision", nullable: false),
                    PositionX = table.Column<double>(type: "double precision", nullable: false),
                    PositionY = table.Column<double>(type: "double precision", nullable: false),
                    Rotation = table.Column<double>(type: "double precision", nullable: false),
                    Scale = table.Column<double>(type: "double precision", nullable: false),
                    MovementX = table.Column<double>(type: "double precision", nullable: false),
                    MovementY = table.Column<double>(type: "double precision", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_image_layers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_image_layers_live_layer_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "live_layer_projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "processing_jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Progress = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processing_jobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_processing_jobs_live_layer_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "live_layer_projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_gallery_posts_IsPublic_CreatedAt",
                table: "gallery_posts",
                columns: new[] { "IsPublic", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_gallery_posts_ProjectId",
                table: "gallery_posts",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_image_layers_ProjectId_LayerOrder",
                table: "image_layers",
                columns: new[] { "ProjectId", "LayerOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_live_layer_projects_OriginalImageId",
                table: "live_layer_projects",
                column: "OriginalImageId");

            migrationBuilder.CreateIndex(
                name: "IX_live_layer_projects_UserId_CreatedAt",
                table: "live_layer_projects",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_processing_jobs_ProjectId_CreatedAt",
                table: "processing_jobs",
                columns: new[] { "ProjectId", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_gallery_posts_live_layer_projects_ProjectId",
                table: "gallery_posts",
                column: "ProjectId",
                principalTable: "live_layer_projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_gallery_posts_live_layer_projects_ProjectId",
                table: "gallery_posts");

            migrationBuilder.DropTable(
                name: "image_layers");

            migrationBuilder.DropTable(
                name: "processing_jobs");

            migrationBuilder.DropTable(
                name: "live_layer_projects");

            migrationBuilder.DropIndex(
                name: "IX_gallery_posts_IsPublic_CreatedAt",
                table: "gallery_posts");

            migrationBuilder.DropIndex(
                name: "IX_gallery_posts_ProjectId",
                table: "gallery_posts");

            migrationBuilder.DropColumn(
                name: "SensorSensitivity",
                table: "user_settings");

            migrationBuilder.DropColumn(
                name: "IsPublic",
                table: "gallery_posts");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "gallery_posts");
        }
    }
}
