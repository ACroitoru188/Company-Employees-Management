using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompanyEmployees.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddImpersonationSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ImpersonationSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DelegationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RealUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActedAsUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImpersonationSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImpersonationSessions_AspNetUsers_ActedAsUserId",
                        column: x => x.ActedAsUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ImpersonationSessions_AspNetUsers_RealUserId",
                        column: x => x.RealUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ImpersonationSessions_ManagerDelegations_DelegationId",
                        column: x => x.DelegationId,
                        principalTable: "ManagerDelegations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImpersonationSessions_ActedAsUserId",
                table: "ImpersonationSessions",
                column: "ActedAsUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ImpersonationSessions_DelegationId",
                table: "ImpersonationSessions",
                column: "DelegationId");

            migrationBuilder.CreateIndex(
                name: "IX_ImpersonationSessions_RealUserId_EndedAt",
                table: "ImpersonationSessions",
                columns: new[] { "RealUserId", "EndedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImpersonationSessions");
        }
    }
}
