using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamPrep.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExamGroupsEssaysLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Remark",
                table: "Questions",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Group",
                table: "Exams",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "PaperExamLinks",
                columns: table => new
                {
                    PaperId = table.Column<int>(type: "int", nullable: false),
                    ExamId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaperExamLinks", x => new { x.ExamId, x.PaperId });
                    table.ForeignKey(
                        name: "FK_PaperExamLinks_Exams_ExamId",
                        column: x => x.ExamId,
                        principalTable: "Exams",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PaperExamLinks_Papers_PaperId",
                        column: x => x.PaperId,
                        principalTable: "Papers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Questions_Type",
                table: "Questions",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_Papers_RocYear",
                table: "Papers",
                column: "RocYear");

            migrationBuilder.CreateIndex(
                name: "IX_PaperExamLinks_PaperId",
                table: "PaperExamLinks",
                column: "PaperId");

            // 既有試卷：把主類別寫進連結表
            migrationBuilder.Sql("INSERT INTO PaperExamLinks (ExamId, PaperId) SELECT ExamId, Id FROM Papers WHERE ExamId IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaperExamLinks");

            migrationBuilder.DropIndex(
                name: "IX_Questions_Type",
                table: "Questions");

            migrationBuilder.DropIndex(
                name: "IX_Papers_RocYear",
                table: "Papers");

            migrationBuilder.DropColumn(
                name: "Remark",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "Group",
                table: "Exams");
        }
    }
}
