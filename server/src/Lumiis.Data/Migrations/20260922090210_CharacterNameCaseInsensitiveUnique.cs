using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lumiis.Data.Migrations
{
    /// <inheritdoc />
    public partial class CharacterNameCaseInsensitiveUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Characters_Name",
                table: "Characters");

            migrationBuilder.AddColumn<string>(
                name: "NameNormalized",
                table: "Characters",
                type: "text",
                nullable: true,
                computedColumnSql: "lower(\"Name\")",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "IX_Characters_NameNormalized",
                table: "Characters",
                column: "NameNormalized",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Characters_NameNormalized",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "NameNormalized",
                table: "Characters");

            migrationBuilder.CreateIndex(
                name: "IX_Characters_Name",
                table: "Characters",
                column: "Name",
                unique: true);
        }
    }
}
