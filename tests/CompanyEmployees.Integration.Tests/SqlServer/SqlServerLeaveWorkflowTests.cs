using CompanyEmployees.Integration.Tests.Application;
using CompanyEmployees.Integration.Tests.Fixtures;
using Xunit;

namespace CompanyEmployees.Integration.Tests.SqlServer;

[Collection("SqlServer")]
public class SqlServerLeaveWorkflowTests : LeaveWorkflowTestsBase
{
    public SqlServerLeaveWorkflowTests(SqlServerDatabaseFixture fixture) : base(fixture)
    {
    }
}
