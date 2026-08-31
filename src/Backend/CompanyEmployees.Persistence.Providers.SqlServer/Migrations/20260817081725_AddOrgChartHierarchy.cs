using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompanyEmployees.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrgChartHierarchy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AdminId",
                table: "Departments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Site",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Departments_AdminId",
                table: "Departments",
                column: "AdminId");

            migrationBuilder.AddForeignKey(
                name: "FK_Departments_AspNetUsers_AdminId",
                table: "Departments",
                column: "AdminId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");

            // Populate initial values for City and Site for existing users
            migrationBuilder.Sql(@"
                UPDATE AspNetUsers SET City = N'București', Site = N'Siemens HQ';
                UPDATE AspNetUsers SET City = N'Brașov', Site = N'Siemens R&D' WHERE Id IN ('11111111-0000-0000-0000-000000000002', '11111111-0000-0000-0000-000000000005', '11111111-0000-0000-0000-000000000008', '11111111-0000-0000-0000-000000000011', '11111111-0000-0000-0000-000000000029', '11111111-0000-0000-0000-000000000032', '11111111-0000-0000-0000-000000000035');
                UPDATE AspNetUsers SET City = N'Brașov', Site = N'Siemens Digital Industry Software' WHERE Id IN ('11111111-0000-0000-0000-000000000014', '11111111-0000-0000-0000-000000000017', '11111111-0000-0000-0000-000000000020', '11111111-0000-0000-0000-000000000023', '11111111-0000-0000-0000-000000000026', '11111111-0000-0000-0000-000000000260', '11111111-0000-0000-0000-000000000261', '11111111-0000-0000-0000-000000000262', '11111111-0000-0000-0000-000000000263', '11111111-0000-0000-0000-000000000264', '11111111-0000-0000-0000-000000000265');
                UPDATE AspNetUsers SET City = N'Cluj-Napoca', Site = N'Siemens Advanta' WHERE Id IN ('11111111-0000-0000-0000-000000000003', '11111111-0000-0000-0000-000000000006', '11111111-0000-0000-0000-000000000009', '11111111-0000-0000-0000-000000000012', '11111111-0000-0000-0000-000000000015', '11111111-0000-0000-0000-000000000018', '11111111-0000-0000-0000-000000000021', '11111111-0000-0000-0000-000000000024', '11111111-0000-0000-0000-000000000027', '11111111-0000-0000-0000-000000000030', '11111111-0000-0000-0000-000000000033', '11111111-0000-0000-0000-000000000036');
                
                -- Assign admins to departments
                EXEC('UPDATE [Departments] SET [AdminId] = ''11111111-0000-0000-0000-000000000006'' WHERE [Id] IN (''22222222-0000-0000-0000-000000000001'', ''22222222-0000-0000-0000-000000000002'')');
                EXEC('UPDATE [Departments] SET [AdminId] = ''11111111-0000-0000-0000-000000000007'' WHERE [Id] IN (''22222222-0000-0000-0000-000000000003'', ''22222222-0000-0000-0000-000000000004'')');
                EXEC('UPDATE [Departments] SET [AdminId] = ''11111111-0000-0000-0000-000000000008'' WHERE [Id] IN (''22222222-0000-0000-0000-000000000005'', ''22222222-0000-0000-0000-000000000006'', ''22222222-0000-0000-0000-000000000007'')');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Departments_AspNetUsers_AdminId",
                table: "Departments");

            migrationBuilder.DropIndex(
                name: "IX_Departments_AdminId",
                table: "Departments");

            migrationBuilder.DropColumn(
                name: "AdminId",
                table: "Departments");

            migrationBuilder.DropColumn(
                name: "City",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "Site",
                table: "AspNetUsers");
        }
    }
}
