using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UpscaleLab.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class PreserveTokenTransactionsOnJobDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_token_transactions_processing_jobs_ProcessingJobId",
                table: "token_transactions");

            migrationBuilder.AlterColumn<Guid>(
                name: "ProcessingJobId",
                table: "token_transactions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddForeignKey(
                name: "FK_token_transactions_processing_jobs_ProcessingJobId",
                table: "token_transactions",
                column: "ProcessingJobId",
                principalTable: "processing_jobs",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_token_transactions_processing_jobs_ProcessingJobId",
                table: "token_transactions");

            migrationBuilder.AlterColumn<Guid>(
                name: "ProcessingJobId",
                table: "token_transactions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_token_transactions_processing_jobs_ProcessingJobId",
                table: "token_transactions",
                column: "ProcessingJobId",
                principalTable: "processing_jobs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
