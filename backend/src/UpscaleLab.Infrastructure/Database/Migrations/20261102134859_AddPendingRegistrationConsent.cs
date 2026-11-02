using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UpscaleLab.Infrastructure.Database.Migrations;

public partial class AddPendingRegistrationConsent : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(name: "CopyrightTermsAcceptedAt", table: "users", type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<string>(name: "CopyrightTermsVersion", table: "users", type: "character varying(32)", maxLength: 32, nullable: true);
        migrationBuilder.AddColumn<string>(name: "CopyrightTermsDocumentHash", table: "users", type: "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>(name: "CopyrightSignature", table: "users", type: "character varying(50)", maxLength: 50, nullable: true);
        migrationBuilder.CreateTable(
            name: "pending_registrations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                Username = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                PasswordHash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                VerificationSentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                VerificationExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                EmailVerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ConsentTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                ConsentTokenExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_pending_registrations", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_pending_registrations_Email", table: "pending_registrations", column: "Email", unique: true);
        migrationBuilder.CreateIndex(name: "IX_pending_registrations_Username", table: "pending_registrations", column: "Username", unique: true);
        migrationBuilder.CreateIndex(name: "IX_pending_registrations_ExpiresAt", table: "pending_registrations", column: "ExpiresAt");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "pending_registrations");
        migrationBuilder.DropColumn(name: "CopyrightTermsAcceptedAt", table: "users");
        migrationBuilder.DropColumn(name: "CopyrightTermsVersion", table: "users");
        migrationBuilder.DropColumn(name: "CopyrightTermsDocumentHash", table: "users");
        migrationBuilder.DropColumn(name: "CopyrightSignature", table: "users");
    }
}