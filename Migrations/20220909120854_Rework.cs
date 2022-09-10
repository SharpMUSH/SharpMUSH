using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SharpMUSH.Migrations
{
    public partial class Rework : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BaseObject",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    LocationId = table.Column<int>(type: "INTEGER", nullable: false),
                    GlobalTypeParent = table.Column<bool>(type: "INTEGER", nullable: false),
                    OwnerId = table.Column<int>(type: "INTEGER", nullable: false),
                    type = table.Column<string>(type: "TEXT", nullable: false),
                    Connected = table.Column<bool>(type: "INTEGER", nullable: true),
                    LastOn = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Session = table.Column<Guid>(type: "TEXT", nullable: true),
                    EditMode = table.Column<bool>(type: "INTEGER", nullable: true),
                    Password = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BaseObject", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BaseObject_BaseObject_LocationId",
                        column: x => x.LocationId,
                        principalTable: "BaseObject",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BaseObject_BaseObject_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "BaseObject",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Flags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    ThingID = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Permission",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsSet = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Permission", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AncestorLink",
                columns: table => new
                {
                    ChildrenId = table.Column<int>(type: "INTEGER", nullable: false),
                    ParentsId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AncestorLink", x => new { x.ChildrenId, x.ParentsId });
                    table.ForeignKey(
                        name: "FK_AncestorLink_BaseObject_ChildrenId",
                        column: x => x.ChildrenId,
                        principalTable: "BaseObject",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AncestorLink_BaseObject_ParentsId",
                        column: x => x.ParentsId,
                        principalTable: "BaseObject",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Attributes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false),
                    ThingId = table.Column<int>(type: "INTEGER", nullable: false),
                    type = table.Column<string>(type: "TEXT", nullable: false),
                    Cmd = table.Column<string>(type: "TEXT", nullable: true),
                    Executable = table.Column<bool>(type: "INTEGER", nullable: true),
                    Callable = table.Column<bool>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attributes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Attributes_BaseObject_ThingId",
                        column: x => x.ThingId,
                        principalTable: "BaseObject",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ThingFlagsLink",
                columns: table => new
                {
                    FlagsId = table.Column<int>(type: "INTEGER", nullable: false),
                    ThingsId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ThingFlagsLink", x => new { x.FlagsId, x.ThingsId });
                    table.ForeignKey(
                        name: "FK_ThingFlagsLink_BaseObject_ThingsId",
                        column: x => x.ThingsId,
                        principalTable: "BaseObject",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ThingFlagsLink_Flags_FlagsId",
                        column: x => x.FlagsId,
                        principalTable: "Flags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BaseObjectPermission",
                columns: table => new
                {
                    PermissionsId = table.Column<int>(type: "INTEGER", nullable: false),
                    ThingsId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BaseObjectPermission", x => new { x.PermissionsId, x.ThingsId });
                    table.ForeignKey(
                        name: "FK_BaseObjectPermission_BaseObject_ThingsId",
                        column: x => x.ThingsId,
                        principalTable: "BaseObject",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BaseObjectPermission_Permission_PermissionsId",
                        column: x => x.PermissionsId,
                        principalTable: "Permission",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FlagPermission",
                columns: table => new
                {
                    FlagsId = table.Column<int>(type: "INTEGER", nullable: false),
                    PermissionsId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlagPermission", x => new { x.FlagsId, x.PermissionsId });
                    table.ForeignKey(
                        name: "FK_FlagPermission_Flags_FlagsId",
                        column: x => x.FlagsId,
                        principalTable: "Flags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FlagPermission_Permission_PermissionsId",
                        column: x => x.PermissionsId,
                        principalTable: "Permission",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Argument",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    CmdId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Argument", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Argument_Attributes_CmdId",
                        column: x => x.CmdId,
                        principalTable: "Attributes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CommandFlagsLink",
                columns: table => new
                {
                    CommandsId = table.Column<int>(type: "INTEGER", nullable: false),
                    FlagsId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommandFlagsLink", x => new { x.CommandsId, x.FlagsId });
                    table.ForeignKey(
                        name: "FK_CommandFlagsLink_Attributes_CommandsId",
                        column: x => x.CommandsId,
                        principalTable: "Attributes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CommandFlagsLink_Flags_FlagsId",
                        column: x => x.FlagsId,
                        principalTable: "Flags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AncestorLink_ParentsId",
                table: "AncestorLink",
                column: "ParentsId");

            migrationBuilder.CreateIndex(
                name: "IX_Argument_CmdId",
                table: "Argument",
                column: "CmdId");

            migrationBuilder.CreateIndex(
                name: "IX_Attributes_ThingId_Name",
                table: "Attributes",
                columns: new[] { "ThingId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BaseObject_LocationId",
                table: "BaseObject",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_BaseObject_Name",
                table: "BaseObject",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BaseObject_OwnerId",
                table: "BaseObject",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_BaseObjectPermission_ThingsId",
                table: "BaseObjectPermission",
                column: "ThingsId");

            migrationBuilder.CreateIndex(
                name: "IX_CommandFlagsLink_FlagsId",
                table: "CommandFlagsLink",
                column: "FlagsId");

            migrationBuilder.CreateIndex(
                name: "IX_FlagPermission_PermissionsId",
                table: "FlagPermission",
                column: "PermissionsId");

            migrationBuilder.CreateIndex(
                name: "IX_Flags_Name",
                table: "Flags",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Permission_Name",
                table: "Permission",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ThingFlagsLink_ThingsId",
                table: "ThingFlagsLink",
                column: "ThingsId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AncestorLink");

            migrationBuilder.DropTable(
                name: "Argument");

            migrationBuilder.DropTable(
                name: "BaseObjectPermission");

            migrationBuilder.DropTable(
                name: "CommandFlagsLink");

            migrationBuilder.DropTable(
                name: "FlagPermission");

            migrationBuilder.DropTable(
                name: "ThingFlagsLink");

            migrationBuilder.DropTable(
                name: "Attributes");

            migrationBuilder.DropTable(
                name: "Permission");

            migrationBuilder.DropTable(
                name: "Flags");

            migrationBuilder.DropTable(
                name: "BaseObject");
        }
    }
}
