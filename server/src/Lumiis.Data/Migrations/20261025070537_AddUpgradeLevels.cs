using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lumiis.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUpgradeLevels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EquippedArmorUpgradeLevel",
                table: "Characters",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "EquippedHelmetUpgradeLevel",
                table: "Characters",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "EquippedShieldUpgradeLevel",
                table: "Characters",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "EquippedWeaponUpgradeLevel",
                table: "Characters",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UpgradeLevel",
                table: "CharacterInventoryItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EquippedArmorUpgradeLevel",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "EquippedHelmetUpgradeLevel",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "EquippedShieldUpgradeLevel",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "EquippedWeaponUpgradeLevel",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "UpgradeLevel",
                table: "CharacterInventoryItems");
        }
    }
}
