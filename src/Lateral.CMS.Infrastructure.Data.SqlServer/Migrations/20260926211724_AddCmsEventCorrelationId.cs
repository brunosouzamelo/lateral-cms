using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lateral.CMS.Infrastructure.Data.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddCmsEventCorrelationId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CorrelationId",
                schema: "Ingestion",
                table: "CmsEvent",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CmsEvent_CorrelationId",
                schema: "Ingestion",
                table: "CmsEvent",
                column: "CorrelationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CmsEvent_CorrelationId",
                schema: "Ingestion",
                table: "CmsEvent");

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                schema: "Ingestion",
                table: "CmsEvent");
        }
    }
}
