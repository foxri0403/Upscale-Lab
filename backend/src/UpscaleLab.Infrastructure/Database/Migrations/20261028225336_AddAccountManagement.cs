using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UpscaleLab.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeletionRequestedAt",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfileImageObjectKey",
                table: "users",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "account_cleanup_tasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeleteAccount = table.Column<bool>(type: "boolean", nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    IdentitySubject = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ObjectKey = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ObjectKeysJson = table.Column<string>(type: "jsonb", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FirstSweepAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_cleanup_tasks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_account_cleanup_tasks_NextAttemptAt",
                table: "account_cleanup_tasks",
                column: "NextAttemptAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_cleanup_tasks");

            migrationBuilder.DropColumn(
                name: "DeletionRequestedAt",
                table: "users");

            migrationBuilder.DropColumn(
                name: "ProfileImageObjectKey",
                table: "users");
        }
    }
}
