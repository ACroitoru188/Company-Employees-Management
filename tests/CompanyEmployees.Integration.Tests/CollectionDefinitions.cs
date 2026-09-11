using CompanyEmployees.Integration.Tests.Fixtures;
using Xunit;

namespace CompanyEmployees.Integration.Tests;

[CollectionDefinition("SqlServer")]
public class SqlServerCollection : ICollectionFixture<SqlServerDatabaseFixture>
{
}

[CollectionDefinition("PostgreSql")]
public class PostgreSqlCollection : ICollectionFixture<PostgreSqlDatabaseFixture>
{
}
