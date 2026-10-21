using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamPrep.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExpandQuestionBank : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PaperId",
                table: "PracticeSessions",
                type: "int",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Categories",
                table: "Papers",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500);

            migrationBuilder.AddColumn<string>(
                name: "SourceNote",
                table: "Papers",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaperId",
                table: "PracticeSessions");

            migrationBuilder.DropColumn(
                name: "SourceNote",
                table: "Papers");

            migrationBuilder.AlterColumn<string>(
                name: "Categories",
                table: "Papers",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(2000)",
                oldMaxLength: 2000);
        }
    }
}
