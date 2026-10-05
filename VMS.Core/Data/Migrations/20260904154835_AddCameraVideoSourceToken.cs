using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VMS.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCameraVideoSourceToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OnvifVideoSourceToken",
                table: "Cameras",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OnvifVideoSourceToken",
                table: "Cameras");
        }
    }
}
