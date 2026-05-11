using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hookbin.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PinReceivedAtPrecision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No-op on SQL Server: datetimeoffset defaults to scale 7.
            // Explicit precision documents the invariant and prevents future provider drift.
            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "ReceivedAt",
                table: "WebhookRequests",
                type: "datetimeoffset(7)",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "ReceivedAt",
                table: "WebhookRequests",
                type: "datetimeoffset",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset(7)");
        }
    }
}
