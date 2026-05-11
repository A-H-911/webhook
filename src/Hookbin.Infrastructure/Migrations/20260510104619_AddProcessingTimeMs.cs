using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hookbin.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessingTimeMs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "WebhookRequests",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ProcessingTimeMs",
                table: "WebhookRequests",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Note",
                table: "WebhookRequests");

            migrationBuilder.DropColumn(
                name: "ProcessingTimeMs",
                table: "WebhookRequests");
        }
    }
}
