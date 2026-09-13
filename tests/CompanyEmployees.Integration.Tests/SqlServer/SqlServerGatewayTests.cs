using CompanyEmployees.Integration.Tests.Fixtures;
using CompanyEmployees.Integration.Tests.Gateway;
using Xunit;

namespace CompanyEmployees.Integration.Tests.SqlServer;

[Collection("SqlServer")]
public class SqlServerUserRepositoryTests : UserRepositoryTestsBase
{
    public SqlServerUserRepositoryTests(SqlServerDatabaseFixture fixture) : base(fixture)
    {
    }
}

[Collection("SqlServer")]
public class SqlServerLeaveRequestRepositoryTests : LeaveRequestRepositoryTestsBase
{
    public SqlServerLeaveRequestRepositoryTests(SqlServerDatabaseFixture fixture) : base(fixture)
    {
    }
}
