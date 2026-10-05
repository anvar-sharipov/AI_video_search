using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VMS.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCountingLinePoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add PointsJson first and backfill it from the existing X1/Y1/X2/Y2 columns (a
            // 2-point line becomes a 2-point polyline) before dropping those columns, so any
            // camera's already-configured counting line survives the upgrade instead of silently
            // reverting to "no line configured". Postgres always renders double precision -> text
            // with '.' as the decimal separator regardless of server locale, so this string
            // concatenation produces valid JSON.
            migrationBuilder.AddColumn<string>(
                name: "PointsJson",
                table: "CameraCountingLines",
                type: "text",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "CameraCountingLines"
                SET "PointsJson" = '[[' || "X1"::text || ',' || "Y1"::text || '],[' || "X2"::text || ',' || "Y2"::text || ']]'
                """);

            migrationBuilder.AlterColumn<string>(
                name: "PointsJson",
                table: "CameraCountingLines",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "X1",
                table: "CameraCountingLines");

            migrationBuilder.DropColumn(
                name: "X2",
                table: "CameraCountingLines");

            migrationBuilder.DropColumn(
                name: "Y1",
                table: "CameraCountingLines");

            migrationBuilder.DropColumn(
                name: "Y2",
                table: "CameraCountingLines");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PointsJson",
                table: "CameraCountingLines");

            migrationBuilder.AddColumn<double>(
                name: "X1",
                table: "CameraCountingLines",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "X2",
                table: "CameraCountingLines",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "Y1",
                table: "CameraCountingLines",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "Y2",
                table: "CameraCountingLines",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);
        }
    }
}
