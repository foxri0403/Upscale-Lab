using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UpscaleLab.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionPaymentLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "BillingPeriodStartUtc",
                table: "payment_orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SubscriptionId",
                table: "payment_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_orders_SubscriptionId_BillingPeriodStartUtc",
                table: "payment_orders",
                columns: new[] { "SubscriptionId", "BillingPeriodStartUtc" },
                unique: true,
                filter: "\"SubscriptionId\" IS NOT NULL AND \"BillingPeriodStartUtc\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_payment_orders_user_subscriptions_SubscriptionId",
                table: "payment_orders",
                column: "SubscriptionId",
                principalTable: "user_subscriptions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_payment_orders_user_subscriptions_SubscriptionId",
                table: "payment_orders");

            migrationBuilder.DropIndex(
                name: "IX_payment_orders_SubscriptionId_BillingPeriodStartUtc",
                table: "payment_orders");

            migrationBuilder.DropColumn(
                name: "BillingPeriodStartUtc",
                table: "payment_orders");

            migrationBuilder.DropColumn(
                name: "SubscriptionId",
                table: "payment_orders");
        }
    }
}
