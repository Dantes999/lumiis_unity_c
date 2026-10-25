using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lumiis.Data.Migrations
{
    /// <inheritdoc />
    public partial class Map1Default : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "MapId",
                table: "Characters",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "map1",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldDefaultValue: "village");

            // Bestehende Alpha-Charaktere aus dem alten Platzhalterdorf auf die neue Map1 heben.
            migrationBuilder.Sql("UPDATE \"Characters\" SET \"MapId\" = 'map1', \"PositionX\" = NULL, \"PositionY\" = NULL WHERE \"MapId\" = 'village';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "MapId",
                table: "Characters",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "village",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldDefaultValue: "map1");
        }
    }
}
