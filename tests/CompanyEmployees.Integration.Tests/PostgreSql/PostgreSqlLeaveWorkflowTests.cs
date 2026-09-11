using CompanyEmployees.Integration.Tests.Application;
using CompanyEmployees.Integration.Tests.Fixtures;
using Xunit;

namespace CompanyEmployees.Integration.Tests.PostgreSql;

[Collection("PostgreSql")]
public class PostgreSqlLeaveWorkflowTests : LeaveWorkflowTestsBase
{
    public PostgreSqlLeaveWorkflowTests(PostgreSqlDatabaseFixture fixture) : base(fixture)
    {
    }
}
