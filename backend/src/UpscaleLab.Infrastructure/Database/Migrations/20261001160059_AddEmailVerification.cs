using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UpscaleLab.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EmailVerificationCodeExpiresAt",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailVerificationCodeHash",
                table: "users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EmailVerificationCodeSentAt",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EmailVerificationFailedAttempts",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "EmailVerifiedAt",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsEmailVerified",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Preserve access for accounts created before email verification was introduced.
            migrationBuilder.Sql("UPDATE users SET \"IsEmailVerified\" = TRUE;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmailVerificationCodeExpiresAt",
                table: "users");

            migrationBuilder.DropColumn(
                name: "EmailVerificationCodeHash",
                table: "users");

            migrationBuilder.DropColumn(
                name: "EmailVerificationCodeSentAt",
                table: "users");

            migrationBuilder.DropColumn(
                name: "EmailVerificationFailedAttempts",
                table: "users");

            migrationBuilder.DropColumn(
                name: "EmailVerifiedAt",
                table: "users");

            migrationBuilder.DropColumn(
                name: "IsEmailVerified",
                table: "users");
        }
    }
}
