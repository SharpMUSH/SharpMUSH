using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SharpMUSH.Migrations
{
    public partial class Initial : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Flags",
                columns: table => new
                {
                    FlagID = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flags", x => x.FlagID);
                });

            migrationBuilder.CreateTable(
                name: "Things",
                columns: table => new
                {
                    ThingID = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    LocationThingID = table.Column<int>(type: "INTEGER", nullable: false),
                    OwnerThingID = table.Column<int>(type: "INTEGER", nullable: false),
                    type = table.Column<string>(type: "TEXT", nullable: false),
                    Connected = table.Column<bool>(type: "INTEGER", nullable: true),
                    LastOn = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Session = table.Column<Guid>(type: "TEXT", nullable: true),
                    Password = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Things", x => x.ThingID);
                    table.ForeignKey(
                        name: "FK_Things_Things_LocationThingID",
                        column: x => x.LocationThingID,
                        principalTable: "Things",
                        principalColumn: "ThingID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Things_Things_OwnerThingID",
                        column: x => x.OwnerThingID,
                        principalTable: "Things",
                        principalColumn: "ThingID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AncestorLink",
                columns: table => new
                {
                    ChildrenThingID = table.Column<int>(type: "INTEGER", nullable: false),
                    ParentsThingID = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AncestorLink", x => new { x.ChildrenThingID, x.ParentsThingID });
                    table.ForeignKey(
                        name: "FK_AncestorLink_Things_ChildrenThingID",
                        column: x => x.ChildrenThingID,
                        principalTable: "Things",
                        principalColumn: "ThingID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AncestorLink_Things_ParentsThingID",
                        column: x => x.ParentsThingID,
                        principalTable: "Things",
                        principalColumn: "ThingID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Attributes",
                columns: table => new
                {
                    AttribId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false),
                    Executable = table.Column<bool>(type: "INTEGER", nullable: false),
                    Command = table.Column<string>(type: "TEXT", nullable: false),
                    ObjThingID = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attributes", x => x.AttribId);
                    table.ForeignKey(
                        name: "FK_Attributes_Things_ObjThingID",
                        column: x => x.ObjThingID,
                        principalTable: "Things",
                        principalColumn: "ThingID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ThingFlagsLink",
                columns: table => new
                {
                    FlagsFlagID = table.Column<int>(type: "INTEGER", nullable: false),
                    ThingsThingID = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ThingFlagsLink", x => new { x.FlagsFlagID, x.ThingsThingID });
                    table.ForeignKey(
                        name: "FK_ThingFlagsLink_Flags_FlagsFlagID",
                        column: x => x.FlagsFlagID,
                        principalTable: "Flags",
                        principalColumn: "FlagID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ThingFlagsLink_Things_ThingsThingID",
                        column: x => x.ThingsThingID,
                        principalTable: "Things",
                        principalColumn: "ThingID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AncestorLink_ParentsThingID",
                table: "AncestorLink",
                column: "ParentsThingID");

            migrationBuilder.CreateIndex(
                name: "IX_Attributes_ObjThingID",
                table: "Attributes",
                column: "ObjThingID");

            migrationBuilder.CreateIndex(
                name: "IX_ThingFlagsLink_ThingsThingID",
                table: "ThingFlagsLink",
                column: "ThingsThingID");

            migrationBuilder.CreateIndex(
                name: "IX_Things_LocationThingID",
                table: "Things",
                column: "LocationThingID");

            migrationBuilder.CreateIndex(
                name: "IX_Things_OwnerThingID",
                table: "Things",
                column: "OwnerThingID");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AncestorLink");

            migrationBuilder.DropTable(
                name: "Attributes");

            migrationBuilder.DropTable(
                name: "ThingFlagsLink");

            migrationBuilder.DropTable(
                name: "Flags");

            migrationBuilder.DropTable(
                name: "Things");
        }
    }
}