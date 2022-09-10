using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SharpMUSH.Migrations
{
    public partial class Tweak : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Attributes_ThingId_Name",
                table: "Attributes");

            migrationBuilder.DropColumn(
                name: "ThingID",
                table: "Flags");

            migrationBuilder.CreateIndex(
                name: "IX_Attributes_ThingId",
                table: "Attributes",
                column: "ThingId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Attributes_ThingId",
                table: "Attributes");

            migrationBuilder.AddColumn<int>(
                name: "ThingID",
                table: "Flags",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Attributes_ThingId_Name",
                table: "Attributes",
                columns: new[] { "ThingId", "Name" },
                unique: true);
        }
    }
}
