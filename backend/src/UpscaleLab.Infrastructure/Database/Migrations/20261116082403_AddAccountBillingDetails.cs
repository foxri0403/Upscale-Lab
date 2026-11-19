using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UpscaleLab.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountBillingDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CardIssuerCode",
                table: "user_subscriptions",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CardNumberSuffix",
                table: "user_subscriptions",
                type: "character varying(4)",
                maxLength: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RefundAmount",
                table: "payment_orders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RefundDeclined",
                table: "payment_orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "RefundTokens",
                table: "payment_orders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RefundedAt",
                table: "payment_orders",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CardIssuerCode",
                table: "user_subscriptions");

            migrationBuilder.DropColumn(
                name: "CardNumberSuffix",
                table: "user_subscriptions");

            migrationBuilder.DropColumn(
                name: "RefundAmount",
                table: "payment_orders");

            migrationBuilder.DropColumn(
                name: "RefundDeclined",
                table: "payment_orders");

            migrationBuilder.DropColumn(
                name: "RefundTokens",
                table: "payment_orders");

            migrationBuilder.DropColumn(
                name: "RefundedAt",
                table: "payment_orders");
        }
    }
}
