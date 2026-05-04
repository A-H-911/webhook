using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebhookService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WebhookTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Token = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CustomResponse_StatusCode = table.Column<int>(type: "int", nullable: true),
                    CustomResponse_ContentType = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CustomResponse_Body = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CustomResponse_Headers = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookTokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WebhookRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Method = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Path = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    QueryString = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Headers = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsBodyBase64 = table.Column<bool>(type: "bit", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: false),
                    UserAgent = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebhookRequests_WebhookTokens_TokenId",
                        column: x => x.TokenId,
                        principalTable: "WebhookTokens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WebhookRequests_ReceivedAt",
                table: "WebhookRequests",
                column: "ReceivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookRequests_TokenId",
                table: "WebhookRequests",
                column: "TokenId");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookTokens_Token",
                table: "WebhookTokens",
                column: "Token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WebhookRequests");

            migrationBuilder.DropTable(
                name: "WebhookTokens");
        }
    }
}
