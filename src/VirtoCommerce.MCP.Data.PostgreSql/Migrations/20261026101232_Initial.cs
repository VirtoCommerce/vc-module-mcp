using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtoCommerce.MCP.Data.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "McpAuditEntry",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ScopeName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ToolName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ClientId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Arguments = table.Column<string>(type: "text", nullable: true),
                    Outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    PendingChangeId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_McpAuditEntry", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "McpPendingChange",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ScopeName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ToolName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Arguments = table.Column<string>(type: "text", nullable: true),
                    SensitiveArgumentPaths = table.Column<string>(type: "text", nullable: true),
                    Preview = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RequestedByUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    RequestedByUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ReviewedByUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ReviewedByUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ReviewedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewComment = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ExpirationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Result = table.Column<string>(type: "text", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_McpPendingChange", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_McpAuditEntry_CreatedDate",
                table: "McpAuditEntry",
                column: "CreatedDate");

            migrationBuilder.CreateIndex(
                name: "IX_McpAuditEntry_PendingChangeId",
                table: "McpAuditEntry",
                column: "PendingChangeId");

            migrationBuilder.CreateIndex(
                name: "IX_McpAuditEntry_UserId_CreatedDate",
                table: "McpAuditEntry",
                columns: new[] { "UserId", "CreatedDate" });

            migrationBuilder.CreateIndex(
                name: "IX_McpPendingChange_RequestedByUserId",
                table: "McpPendingChange",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_McpPendingChange_Status_ExpirationDate",
                table: "McpPendingChange",
                columns: new[] { "Status", "ExpirationDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "McpAuditEntry");

            migrationBuilder.DropTable(
                name: "McpPendingChange");
        }
    }
}
