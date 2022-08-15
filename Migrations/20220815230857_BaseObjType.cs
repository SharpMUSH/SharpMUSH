using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SharpMUSH.Migrations
{
    public partial class BaseObjType : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AncestorLink_Things_ChildrenThingID",
                table: "AncestorLink");

            migrationBuilder.DropForeignKey(
                name: "FK_AncestorLink_Things_ParentsThingID",
                table: "AncestorLink");

            migrationBuilder.DropForeignKey(
                name: "FK_Attributes_Things_ObjThingID",
                table: "Attributes");

            migrationBuilder.DropForeignKey(
                name: "FK_ThingFlagsLink_Things_ThingsThingID",
                table: "ThingFlagsLink");

            migrationBuilder.DropForeignKey(
                name: "FK_Things_Things_LocationThingID",
                table: "Things");

            migrationBuilder.DropForeignKey(
                name: "FK_Things_Things_OwnerThingID",
                table: "Things");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Things",
                table: "Things");

            migrationBuilder.RenameTable(
                name: "Things",
                newName: "MUSHObj");

            migrationBuilder.RenameIndex(
                name: "IX_Things_OwnerThingID",
                table: "MUSHObj",
                newName: "IX_MUSHObj_OwnerThingID");

            migrationBuilder.RenameIndex(
                name: "IX_Things_LocationThingID",
                table: "MUSHObj",
                newName: "IX_MUSHObj_LocationThingID");

            migrationBuilder.AddColumn<int>(
                name: "ExitTypeThingID",
                table: "Attributes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RoomTypeThingID",
                table: "Attributes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UserTypeThingID",
                table: "Attributes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_MUSHObj",
                table: "MUSHObj",
                column: "ThingID");

            migrationBuilder.CreateIndex(
                name: "IX_Attributes_ExitTypeThingID",
                table: "Attributes",
                column: "ExitTypeThingID");

            migrationBuilder.CreateIndex(
                name: "IX_Attributes_RoomTypeThingID",
                table: "Attributes",
                column: "RoomTypeThingID");

            migrationBuilder.CreateIndex(
                name: "IX_Attributes_UserTypeThingID",
                table: "Attributes",
                column: "UserTypeThingID");

            migrationBuilder.AddForeignKey(
                name: "FK_AncestorLink_MUSHObj_ChildrenThingID",
                table: "AncestorLink",
                column: "ChildrenThingID",
                principalTable: "MUSHObj",
                principalColumn: "ThingID",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AncestorLink_MUSHObj_ParentsThingID",
                table: "AncestorLink",
                column: "ParentsThingID",
                principalTable: "MUSHObj",
                principalColumn: "ThingID",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Attributes_MUSHObj_ExitTypeThingID",
                table: "Attributes",
                column: "ExitTypeThingID",
                principalTable: "MUSHObj",
                principalColumn: "ThingID");

            migrationBuilder.AddForeignKey(
                name: "FK_Attributes_MUSHObj_ObjThingID",
                table: "Attributes",
                column: "ObjThingID",
                principalTable: "MUSHObj",
                principalColumn: "ThingID",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Attributes_MUSHObj_RoomTypeThingID",
                table: "Attributes",
                column: "RoomTypeThingID",
                principalTable: "MUSHObj",
                principalColumn: "ThingID");

            migrationBuilder.AddForeignKey(
                name: "FK_Attributes_MUSHObj_UserTypeThingID",
                table: "Attributes",
                column: "UserTypeThingID",
                principalTable: "MUSHObj",
                principalColumn: "ThingID");

            migrationBuilder.AddForeignKey(
                name: "FK_MUSHObj_MUSHObj_LocationThingID",
                table: "MUSHObj",
                column: "LocationThingID",
                principalTable: "MUSHObj",
                principalColumn: "ThingID",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_MUSHObj_MUSHObj_OwnerThingID",
                table: "MUSHObj",
                column: "OwnerThingID",
                principalTable: "MUSHObj",
                principalColumn: "ThingID",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ThingFlagsLink_MUSHObj_ThingsThingID",
                table: "ThingFlagsLink",
                column: "ThingsThingID",
                principalTable: "MUSHObj",
                principalColumn: "ThingID",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AncestorLink_MUSHObj_ChildrenThingID",
                table: "AncestorLink");

            migrationBuilder.DropForeignKey(
                name: "FK_AncestorLink_MUSHObj_ParentsThingID",
                table: "AncestorLink");

            migrationBuilder.DropForeignKey(
                name: "FK_Attributes_MUSHObj_ExitTypeThingID",
                table: "Attributes");

            migrationBuilder.DropForeignKey(
                name: "FK_Attributes_MUSHObj_ObjThingID",
                table: "Attributes");

            migrationBuilder.DropForeignKey(
                name: "FK_Attributes_MUSHObj_RoomTypeThingID",
                table: "Attributes");

            migrationBuilder.DropForeignKey(
                name: "FK_Attributes_MUSHObj_UserTypeThingID",
                table: "Attributes");

            migrationBuilder.DropForeignKey(
                name: "FK_MUSHObj_MUSHObj_LocationThingID",
                table: "MUSHObj");

            migrationBuilder.DropForeignKey(
                name: "FK_MUSHObj_MUSHObj_OwnerThingID",
                table: "MUSHObj");

            migrationBuilder.DropForeignKey(
                name: "FK_ThingFlagsLink_MUSHObj_ThingsThingID",
                table: "ThingFlagsLink");

            migrationBuilder.DropIndex(
                name: "IX_Attributes_ExitTypeThingID",
                table: "Attributes");

            migrationBuilder.DropIndex(
                name: "IX_Attributes_RoomTypeThingID",
                table: "Attributes");

            migrationBuilder.DropIndex(
                name: "IX_Attributes_UserTypeThingID",
                table: "Attributes");

            migrationBuilder.DropPrimaryKey(
                name: "PK_MUSHObj",
                table: "MUSHObj");

            migrationBuilder.DropColumn(
                name: "ExitTypeThingID",
                table: "Attributes");

            migrationBuilder.DropColumn(
                name: "RoomTypeThingID",
                table: "Attributes");

            migrationBuilder.DropColumn(
                name: "UserTypeThingID",
                table: "Attributes");

            migrationBuilder.RenameTable(
                name: "MUSHObj",
                newName: "Things");

            migrationBuilder.RenameIndex(
                name: "IX_MUSHObj_OwnerThingID",
                table: "Things",
                newName: "IX_Things_OwnerThingID");

            migrationBuilder.RenameIndex(
                name: "IX_MUSHObj_LocationThingID",
                table: "Things",
                newName: "IX_Things_LocationThingID");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Things",
                table: "Things",
                column: "ThingID");

            migrationBuilder.AddForeignKey(
                name: "FK_AncestorLink_Things_ChildrenThingID",
                table: "AncestorLink",
                column: "ChildrenThingID",
                principalTable: "Things",
                principalColumn: "ThingID",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AncestorLink_Things_ParentsThingID",
                table: "AncestorLink",
                column: "ParentsThingID",
                principalTable: "Things",
                principalColumn: "ThingID",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Attributes_Things_ObjThingID",
                table: "Attributes",
                column: "ObjThingID",
                principalTable: "Things",
                principalColumn: "ThingID",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ThingFlagsLink_Things_ThingsThingID",
                table: "ThingFlagsLink",
                column: "ThingsThingID",
                principalTable: "Things",
                principalColumn: "ThingID",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Things_Things_LocationThingID",
                table: "Things",
                column: "LocationThingID",
                principalTable: "Things",
                principalColumn: "ThingID",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Things_Things_OwnerThingID",
                table: "Things",
                column: "OwnerThingID",
                principalTable: "Things",
                principalColumn: "ThingID",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
