using CompanyEmployees.Persistence.Contracts;
using CompanyEmployees.Persistence.Providers.PostgreSql;
using CompanyEmployees.Persistence.Providers.SqlServer;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace CompanyEmployees.Application.Tests;

public class ConnectionFieldValidationTests
{
    [Fact]
    public void Validate_RequiredField_ReturnsErrorWhenEmpty()
    {
        var field = new ConnectionField("Username", "Username", IsSecret: false, IsRequired: true);

        Assert.NotNull(field.Validate(null));
        Assert.NotNull(field.Validate(""));
        Assert.NotNull(field.Validate("   "));
        Assert.Null(field.Validate("admin"));
    }

    [Fact]
    public void Validate_MaxLength_EnforcesLength()
    {
        var field = new ConnectionField("Database", "Database name", IsSecret: false, MaxLength: 10);

        Assert.Null(field.Validate("1234567890"));
        var error = field.Validate("12345678901");
        Assert.NotNull(error);
        Assert.Contains("must not exceed 10 characters", error);
    }

    [Fact]
    public void Validate_Integer_ValidatesNumericAndRange()
    {
        var portField = new ConnectionField(
            "Port",
            "Port",
            IsSecret: false,
            FieldType: ConnectionFieldType.Integer,
            MinValue: 1,
            MaxValue: 65535);

        Assert.Null(portField.Validate("5432"));
        Assert.Null(portField.Validate("1"));
        Assert.Null(portField.Validate("65535"));

        var notNumberError = portField.Validate("abc");
        Assert.NotNull(notNumberError);
        Assert.Contains("must be a valid integer", notNumberError);

        var tooSmallError = portField.Validate("0");
        Assert.NotNull(tooSmallError);
        Assert.Contains("must be at least 1", tooSmallError);

        var tooLargeError = portField.Validate("70000");
        Assert.NotNull(tooLargeError);
        Assert.Contains("must be at most 65535", tooLargeError);
    }

    [Fact]
    public void Validate_Boolean_AcceptsOnlyBooleans()
    {
        var boolField = new ConnectionField(
            "TrustServerCertificate",
            "Trust server certificate",
            IsSecret: false,
            FieldType: ConnectionFieldType.Boolean);

        Assert.Null(boolField.Validate("True"));
        Assert.Null(boolField.Validate("false"));
        Assert.NotNull(boolField.Validate("invalid_bool"));
    }

    [Fact]
    public void PostgreSqlProvider_BuildConnectionString_EscapesDelimitersAndPreventsInjection()
    {
        var plugin = new PostgreSqlProviderPlugin();
        var fields = new Dictionary<string, string>
        {
            ["Host"] = "localhost",
            ["Port"] = "5432",
            ["Database"] = "company_test",
            ["Username"] = "user",
            // Attempt to inject SSL Mode and Trust Server Certificate via Password
            ["Password"] = "secret_pass;SSL Mode=Disable;Trust Server Certificate=true"
        };

        var connectionString = plugin.BuildConnectionString(fields);

        // Verify the resulting connection string escapes password and doesn't inject SSL Mode as a top-level setting
        var parsed = new NpgsqlConnectionStringBuilder(connectionString);
        Assert.Equal("secret_pass;SSL Mode=Disable;Trust Server Certificate=true", parsed.Password);
        Assert.Equal("localhost", parsed.Host);
        Assert.Equal(5432, parsed.Port);
        Assert.Equal("company_test", parsed.Database);
        Assert.Equal("user", parsed.Username);
        // Default SslMode should not have been overridden by injection
        Assert.Equal(SslMode.Prefer, parsed.SslMode);
    }

    [Fact]
    public void SqlServerProvider_BuildConnectionString_EscapesDelimitersAndPreventsInjection()
    {
        var plugin = new SqlServerProviderPlugin();
        var fields = new Dictionary<string, string>
        {
            ["Server"] = "localhost,1433",
            ["Database"] = "CompanyTest",
            ["User Id"] = "sa",
            // Attempt to inject TrustServerCertificate via Password
            ["Password"] = "secret_pass;TrustServerCertificate=false;",
            ["TrustServerCertificate"] = "True"
        };

        var connectionString = plugin.BuildConnectionString(fields);

        var parsed = new SqlConnectionStringBuilder(connectionString);
        Assert.Equal("secret_pass;TrustServerCertificate=false;", parsed.Password);
        Assert.Equal("localhost,1433", parsed.DataSource);
        Assert.Equal("CompanyTest", parsed.InitialCatalog);
        Assert.True(parsed.TrustServerCertificate);
    }
}
