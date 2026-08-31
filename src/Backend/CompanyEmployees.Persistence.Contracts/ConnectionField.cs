namespace CompanyEmployees.Persistence.Contracts;

/// <summary>
/// Describes a single field the setup wizard must collect from the admin.
/// The wizard renders fields in <see cref="IDbProviderPlugin.RequiredFields"/> order.
/// </summary>
/// <param name="Key">Config key used when assembling the connection string (e.g. "Server", "Password").</param>
/// <param name="Label">Human-readable label displayed in the wizard form.</param>
/// <param name="IsSecret">When true the wizard renders a password-style masked input.</param>
/// <param name="DefaultValue">Optional pre-filled value shown when the wizard first renders this field.</param>
public sealed record ConnectionField(
    string Key,
    string Label,
    bool IsSecret,
    string? DefaultValue = null);
