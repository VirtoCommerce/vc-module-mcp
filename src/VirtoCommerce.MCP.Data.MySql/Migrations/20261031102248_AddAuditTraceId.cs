using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtoCommerce.MCP.Data.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditTraceId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TraceId",
                table: "McpAuditEntry",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_McpAuditEntry_TraceId",
                table: "McpAuditEntry",
                column: "TraceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_McpAuditEntry_TraceId",
                table: "McpAuditEntry");

            migrationBuilder.DropColumn(
                name: "TraceId",
                table: "McpAuditEntry");
        }
    }
}
