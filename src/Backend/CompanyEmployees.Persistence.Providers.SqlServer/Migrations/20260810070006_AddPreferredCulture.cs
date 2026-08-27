using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompanyEmployees.Persistence.Migrations
{
    /// <inheritdoc />
    // Deliberately carries main's migration id: it is the same schema change, and a database
    // that already ran it on main has that id in __EFMigrationsHistory. A freshly generated id
    // would try to add the column a second time and fail on startup.
    public partial class AddPreferredCulture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PreferredCulture",
                table: "AspNetUsers",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PreferredCulture",
                table: "AspNetUsers");
        }
    }
}
