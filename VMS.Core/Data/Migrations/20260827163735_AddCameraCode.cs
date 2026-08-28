using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VMS.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCameraCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Cameras",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Cameras_Code",
                table: "Cameras",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Cameras_Code",
                table: "Cameras");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Cameras");
        }
    }
}
