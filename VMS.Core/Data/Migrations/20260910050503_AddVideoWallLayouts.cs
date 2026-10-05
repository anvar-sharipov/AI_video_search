using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VMS.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVideoWallLayouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VideoWallLayouts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Rows = table.Column<int>(type: "integer", nullable: false),
                    Columns = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoWallLayouts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VideoWallCells",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VideoWallLayoutId = table.Column<Guid>(type: "uuid", nullable: false),
                    Row = table.Column<int>(type: "integer", nullable: false),
                    Column = table.Column<int>(type: "integer", nullable: false),
                    RowSpan = table.Column<int>(type: "integer", nullable: false),
                    ColumnSpan = table.Column<int>(type: "integer", nullable: false),
                    CameraId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoWallCells", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VideoWallCells_Cameras_CameraId",
                        column: x => x.CameraId,
                        principalTable: "Cameras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_VideoWallCells_VideoWallLayouts_VideoWallLayoutId",
                        column: x => x.VideoWallLayoutId,
                        principalTable: "VideoWallLayouts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VideoWallCells_CameraId",
                table: "VideoWallCells",
                column: "CameraId");

            migrationBuilder.CreateIndex(
                name: "IX_VideoWallCells_VideoWallLayoutId",
                table: "VideoWallCells",
                column: "VideoWallLayoutId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VideoWallCells");

            migrationBuilder.DropTable(
                name: "VideoWallLayouts");
        }
    }
}
