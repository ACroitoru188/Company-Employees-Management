namespace CompanyEmployees.Web.Setup;

/// <summary>State persisted after the setup wizard completes.</summary>
public sealed record SetupState(
    bool IsComplete,
    string? PrimaryProviderId,
    string? PrimaryConnectionString,
    string? SecondaryProviderId,
    string? SecondaryConnectionString);

public interface ISetupStateStore
{
    SetupState Load();
    Task SaveAsync(SetupState state, CancellationToken ct = default);
}
