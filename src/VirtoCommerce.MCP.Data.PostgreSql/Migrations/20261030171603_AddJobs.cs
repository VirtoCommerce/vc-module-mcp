using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtoCommerce.MCP.Data.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "McpJob",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ScopeName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ToolName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EngineJobId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    RequestedByUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    PendingChangeId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ProcessedCount = table.Column<long>(type: "bigint", nullable: true),
                    TotalCount = table.Column<long>(type: "bigint", nullable: true),
                    Message = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Preview = table.Column<string>(type: "text", nullable: true),
                    StartedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinishedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_McpJob", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_McpJob_CreatedDate",
                table: "McpJob",
                column: "CreatedDate");

            migrationBuilder.CreateIndex(
                name: "IX_McpJob_UserId",
                table: "McpJob",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "McpJob");
        }
    }
}
