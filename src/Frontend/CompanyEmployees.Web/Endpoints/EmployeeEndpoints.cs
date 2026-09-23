using CompanyEmployees.Application;
using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.Enums;
using CompanyEmployees.Web.Security;
using CompanyEmployees.Web.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace CompanyEmployees.Web.Endpoints;

public static class EmployeeEndpoints
{
    public static IEndpointRouteBuilder MapEmployeeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/employees/export.csv", async (
            HttpContext httpContext,
            EmployeeCsvExportService csvExporter,
            UserManager<User> userManager,
            CancellationToken cancellationToken) =>
        {
            // Export scope always comes from the authenticated account in the database.
            // A UI preview selection must never expose another region's employee data.
            var email = httpContext.User.Identity?.Name;
            var regionId = await userManager.Users
                .Where(user => user.Email == email)
                .Select(user => (Guid?)user.RegionId)
                .FirstOrDefaultAsync(cancellationToken);

            if (!regionId.HasValue)
                return Results.NotFound();

            var export = await csvExporter.GenerateAsync(regionId.Value, cancellationToken);
            return Results.File(export.Content, "text/csv; charset=utf-8", export.FileName);
        }).RequireAuthorization(policy => policy.RequireAssertion(context =>
            context.User.IsInRole(UserRole.Admin.ToString())
            || context.User.HasClaim("Department", HomeRouteResolver.HrDepartmentName)));

        return app;
    }
}
