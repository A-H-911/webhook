using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hookbin.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTokenNameAndRequestResponseAndCountry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "WebhookTokens",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(
                "UPDATE WebhookTokens SET Name = COALESCE(NULLIF(Description,''), 'wh-' + LEFT(CONVERT(NVARCHAR(36), Id), 8))");

            migrationBuilder.AddColumn<string>(
                name: "IpCountry",
                table: "WebhookRequests",
                type: "nvarchar(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ResponseStatusCode",
                table: "WebhookRequests",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebhookRequests_TokenId_Method_ReceivedAt",
                table: "WebhookRequests",
                columns: new[] { "TokenId", "Method", "ReceivedAt" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_WebhookRequests_TokenId_ResponseStatusCode_ReceivedAt",
                table: "WebhookRequests",
                columns: new[] { "TokenId", "ResponseStatusCode", "ReceivedAt" },
                descending: new[] { false, false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WebhookRequests_TokenId_Method_ReceivedAt",
                table: "WebhookRequests");

            migrationBuilder.DropIndex(
                name: "IX_WebhookRequests_TokenId_ResponseStatusCode_ReceivedAt",
                table: "WebhookRequests");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "WebhookTokens");

            migrationBuilder.DropColumn(
                name: "IpCountry",
                table: "WebhookRequests");

            migrationBuilder.DropColumn(
                name: "ResponseStatusCode",
                table: "WebhookRequests");
        }
    }
}
