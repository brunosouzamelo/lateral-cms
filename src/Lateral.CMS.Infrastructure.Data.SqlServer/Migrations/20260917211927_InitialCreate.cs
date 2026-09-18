using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lateral.CMS.Infrastructure.Data.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "Content");

            migrationBuilder.EnsureSchema(
                name: "Ingestion");

            migrationBuilder.CreateTable(
                name: "CmsEntity",
                schema: "Content",
                columns: table => new
                {
                    CmsEntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    LastPublishedVersion = table.Column<int>(type: "int", nullable: true),
                    CmsEntityStatusId = table.Column<int>(type: "int", nullable: false),
                    LastEventTimestamp = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDisabledByAdmin = table.Column<bool>(type: "bit", nullable: false),
                    DisabledByAdminDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DisabledByAdminUser = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddedDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifiedDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CmsEntity", x => x.CmsEntityId);
                    table.CheckConstraint("CK_CmsEntity_Payload_IsJson", "ISJSON([Payload]) > 0");
                });

            migrationBuilder.CreateTable(
                name: "CmsEntityTombstone",
                schema: "Content",
                columns: table => new
                {
                    ExternalId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DeletedTimestamp = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AddedDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CmsEntityTombstone", x => x.ExternalId);
                });

            migrationBuilder.CreateTable(
                name: "CmsEvent",
                schema: "Ingestion",
                columns: table => new
                {
                    CmsEventId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchIndex = table.Column<int>(type: "int", nullable: false),
                    CmsEventTypeId = table.Column<int>(type: "int", nullable: true),
                    ExternalId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Version = table.Column<int>(type: "int", nullable: true),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EventTimestamp = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CmsEventStatusId = table.Column<int>(type: "int", nullable: false),
                    StatusReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    NextAttemptDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ProcessedDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReceivedDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReceivedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CmsEvent", x => x.CmsEventId);
                    table.CheckConstraint("CK_CmsEvent_Payload_IsJson", "ISJSON([Payload]) > 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CmsEntity_CmsEntityStatusId_IsDisabledByAdmin_ExternalId",
                schema: "Content",
                table: "CmsEntity",
                columns: new[] { "CmsEntityStatusId", "IsDisabledByAdmin", "ExternalId" });

            migrationBuilder.CreateIndex(
                name: "IX_CmsEntity_ExternalId",
                schema: "Content",
                table: "CmsEntity",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CmsEvent_BatchId",
                schema: "Ingestion",
                table: "CmsEvent",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_CmsEvent_CmsEventStatusId_NextAttemptDate_EventTimestamp",
                schema: "Ingestion",
                table: "CmsEvent",
                columns: new[] { "CmsEventStatusId", "NextAttemptDate", "EventTimestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_CmsEvent_ExternalId",
                schema: "Ingestion",
                table: "CmsEvent",
                column: "ExternalId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CmsEntity",
                schema: "Content");

            migrationBuilder.DropTable(
                name: "CmsEntityTombstone",
                schema: "Content");

            migrationBuilder.DropTable(
                name: "CmsEvent",
                schema: "Ingestion");
        }
    }
}
