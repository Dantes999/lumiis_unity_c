using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lumiis.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryStackCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Count",
                table: "CharacterInventoryItems",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Count",
                table: "CharacterInventoryItems");
        }
    }
}
