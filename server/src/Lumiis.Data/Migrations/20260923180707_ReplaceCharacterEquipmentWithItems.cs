using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Lumiis.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceCharacterEquipmentWithItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // M5a: ArmorId/HelmetId/WeaponId/ShieldId enthielten LookIds (direkte Verweise auf looks.json),
            // die neuen EquippedXItemId-Spalten enthalten stattdessen Item-IDs (Verweise auf items.json,
            // nullable - null = Slot leer). LookId und ItemId sind unterschiedliche Wertebereiche ohne
            // sinnvolle automatische Umrechnung - bestehende Werte werden hier deshalb bewusst verworfen statt
            // migriert (Alpha-Betrieb, noch keine produktiven Spielerdaten zu erhalten).
            migrationBuilder.DropColumn(
                name: "ArmorId",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "HelmetId",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "ShieldId",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "WeaponId",
                table: "Characters");

            migrationBuilder.AddColumn<int>(
                name: "EquippedArmorItemId",
                table: "Characters",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EquippedHelmetItemId",
                table: "Characters",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EquippedShieldItemId",
                table: "Characters",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EquippedWeaponItemId",
                table: "Characters",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CharacterInventoryItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CharacterId = table.Column<int>(type: "integer", nullable: false),
                    SlotIndex = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterInventoryItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CharacterInventoryItems_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CharacterInventoryItems_CharacterId_SlotIndex",
                table: "CharacterInventoryItems",
                columns: new[] { "CharacterId", "SlotIndex" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CharacterInventoryItems");

            migrationBuilder.DropColumn(
                name: "EquippedArmorItemId",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "EquippedHelmetItemId",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "EquippedShieldItemId",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "EquippedWeaponItemId",
                table: "Characters");

            migrationBuilder.AddColumn<int>(
                name: "ArmorId",
                table: "Characters",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "HelmetId",
                table: "Characters",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ShieldId",
                table: "Characters",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WeaponId",
                table: "Characters",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
