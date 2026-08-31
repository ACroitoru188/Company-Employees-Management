using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompanyEmployees.Persistence.Migrations
{
    // ProjectManager (Role=2) is folded into LineManager (Role=3): the app never treated the
    // two differently (same nav/routing/team gating everywhere), and the only place it did
    // treat them differently — the department-manager assignment dropdown — already excluded
    // ProjectManager. UserRole.ProjectManager is removed from the enum without renumbering
    // LineManager/Admin, since Role is a raw int column with no name mapping: shifting their
    // values would silently corrupt every other row.
    public partial class RemoveProjectManagerRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE [AspNetUsers] SET [Role] = 3 WHERE [Role] = 2;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Lossy: which Role=3 rows were originally ProjectManager (2) vs already
            // LineManager (3) is no longer known, so this can't be un-done precisely.
        }
    }
}
