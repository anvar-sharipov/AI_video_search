using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VMS.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEmapCountingAlarm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AlarmAcknowledgements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DetectionEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcknowledgedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcknowledgedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlarmAcknowledgements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CameraCountingLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CameraCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    X1 = table.Column<double>(type: "double precision", nullable: false),
                    Y1 = table.Column<double>(type: "double precision", nullable: false),
                    X2 = table.Column<double>(type: "double precision", nullable: false),
                    Y2 = table.Column<double>(type: "double precision", nullable: false),
                    LeftToRightIsIn = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CameraCountingLines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EMapPins",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EMapId = table.Column<Guid>(type: "uuid", nullable: false),
                    CameraId = table.Column<Guid>(type: "uuid", nullable: false),
                    X = table.Column<double>(type: "double precision", nullable: false),
                    Y = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EMapPins", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EMaps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ImageFileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EMaps", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AlarmAcknowledgements_DetectionEventId",
                table: "AlarmAcknowledgements",
                column: "DetectionEventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CameraCountingLines_CameraCode",
                table: "CameraCountingLines",
                column: "CameraCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EMapPins_EMapId",
                table: "EMapPins",
                column: "EMapId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AlarmAcknowledgements");

            migrationBuilder.DropTable(
                name: "CameraCountingLines");

            migrationBuilder.DropTable(
                name: "EMapPins");

            migrationBuilder.DropTable(
                name: "EMaps");
        }
    }
}
