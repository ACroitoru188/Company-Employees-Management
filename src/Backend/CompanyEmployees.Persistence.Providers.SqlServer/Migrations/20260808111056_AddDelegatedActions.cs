using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompanyEmployees.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDelegatedActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DelegatedActions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DelegationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RealUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActedAsUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActionType = table.Column<int>(type: "int", nullable: false),
                    TargetEntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Details = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DelegatedActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DelegatedActions_AspNetUsers_ActedAsUserId",
                        column: x => x.ActedAsUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DelegatedActions_AspNetUsers_RealUserId",
                        column: x => x.RealUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DelegatedActions_AspNetUsers_TargetUserId",
                        column: x => x.TargetUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DelegatedActions_ManagerDelegations_DelegationId",
                        column: x => x.DelegationId,
                        principalTable: "ManagerDelegations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_DelegatedActions_ActedAsUserId_CreatedAt",
                table: "DelegatedActions",
                columns: new[] { "ActedAsUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DelegatedActions_DelegationId",
                table: "DelegatedActions",
                column: "DelegationId");

            migrationBuilder.CreateIndex(
                name: "IX_DelegatedActions_RealUserId_CreatedAt",
                table: "DelegatedActions",
                columns: new[] { "RealUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DelegatedActions_TargetUserId",
                table: "DelegatedActions",
                column: "TargetUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DelegatedActions");
        }
    }
}
