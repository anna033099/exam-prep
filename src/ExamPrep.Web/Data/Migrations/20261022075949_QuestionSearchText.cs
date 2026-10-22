using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamPrep.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class QuestionSearchText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SearchText",
                table: "Questions",
                type: "nvarchar(max)",
                nullable: false,
                computedColumnSql: "UPPER(CONCAT([Stem], N' ', [OptionA], N' ', [OptionB], N' ', [OptionC], N' ', [OptionD], N' ', [OptionE], N' ', [Passage]))",
                stored: true,
                collation: "Latin1_General_100_BIN2");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SearchText",
                table: "Questions");
        }
    }
}
