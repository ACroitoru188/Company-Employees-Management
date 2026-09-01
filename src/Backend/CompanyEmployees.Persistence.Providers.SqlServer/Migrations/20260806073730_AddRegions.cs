using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompanyEmployees.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRegions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Regions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Regions", x => x.Id);
                });

            var romaniaId = new Guid("44444444-4444-4444-4444-444444444401");
            var pakistanId = new Guid("44444444-4444-4444-4444-444444444402");

            migrationBuilder.InsertData(
                table: "Regions",
                columns: new[] { "Id", "Name", "Code", "IsActive" },
                values: new object[,]
                {
                    { romaniaId, "Romania", "RO", true },
                    { pakistanId, "Pakistan", "PK", true }
                });

            // Existing accounts belong to Romania. New accounts must explicitly
            // choose their region through the admin account-creation flow.
            migrationBuilder.AddColumn<Guid>(
                name: "RegionId",
                table: "AspNetUsers",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: romaniaId);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_RegionId",
                table: "AspNetUsers",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_Regions_Code",
                table: "Regions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Regions_Name",
                table: "Regions",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_Regions_RegionId",
                table: "AspNetUsers",
                column: "RegionId",
                principalTable: "Regions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_Regions_RegionId",
                table: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "Regions");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_RegionId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "RegionId",
                table: "AspNetUsers");
        }
    }
}
