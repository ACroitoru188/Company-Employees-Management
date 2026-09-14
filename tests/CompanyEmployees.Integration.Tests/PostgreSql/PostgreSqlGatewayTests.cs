using CompanyEmployees.Integration.Tests.Fixtures;
using CompanyEmployees.Integration.Tests.Gateway;
using Xunit;

namespace CompanyEmployees.Integration.Tests.PostgreSql;

[Collection("PostgreSql")]
public class PostgreSqlUserRepositoryTests : UserRepositoryTestsBase
{
    public PostgreSqlUserRepositoryTests(PostgreSqlDatabaseFixture fixture) : base(fixture)
    {
    }
}

[Collection("PostgreSql")]
public class PostgreSqlLeaveRequestRepositoryTests : LeaveRequestRepositoryTestsBase
{
    public PostgreSqlLeaveRequestRepositoryTests(PostgreSqlDatabaseFixture fixture) : base(fixture)
    {
    }
}
