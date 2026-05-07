using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebhookService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCoveringIndexForPaginatedRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WebhookRequests_ReceivedAt",
                table: "WebhookRequests");

            migrationBuilder.DropIndex(
                name: "IX_WebhookRequests_TokenId",
                table: "WebhookRequests");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookRequests_TokenId_ReceivedAt_Id",
                table: "WebhookRequests",
                columns: new[] { "TokenId", "ReceivedAt", "Id" },
                descending: new[] { false, true, true })
                .Annotation("SqlServer:Include", new[] { "Method", "Path", "SizeBytes", "IpAddress", "ContentType" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WebhookRequests_TokenId_ReceivedAt_Id",
                table: "WebhookRequests");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookRequests_ReceivedAt",
                table: "WebhookRequests",
                column: "ReceivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookRequests_TokenId",
                table: "WebhookRequests",
                column: "TokenId");
        }
    }
}
