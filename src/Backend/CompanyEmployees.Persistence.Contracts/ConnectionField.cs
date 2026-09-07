namespace CompanyEmployees.Persistence.Contracts;

/// <summary>
/// Data type of a connection field for input rendering and validation.
/// </summary>
public enum ConnectionFieldType
{
    Text,
    Password,
    Integer,
    Boolean
}

/// <summary>
/// Describes a single field the setup wizard must collect from the admin.
/// The wizard renders fields in <see cref="IDbProviderPlugin.RequiredFields"/> order.
/// </summary>
/// <param name="Key">Config key used when assembling the connection string (e.g. "Server", "Password").</param>
/// <param name="Label">Human-readable label displayed in the wizard form.</param>
/// <param name="IsSecret">When true the wizard renders a password-style masked input.</param>
/// <param name="DefaultValue">Optional pre-filled value shown when the wizard first renders this field.</param>
/// <param name="FieldType">Semantic data type for validation and input formatting.</param>
/// <param name="MaxLength">Maximum allowed string length.</param>
/// <param name="MinValue">Minimum numeric value for integer fields.</param>
/// <param name="MaxValue">Maximum numeric value for integer fields.</param>
/// <param name="IsRequired">Whether a non-empty value must be provided.</param>
public sealed record ConnectionField(
    string Key,
    string Label,
    bool IsSecret,
    string? DefaultValue = null,
    ConnectionFieldType FieldType = ConnectionFieldType.Text,
    int? MaxLength = null,
    int? MinValue = null,
    int? MaxValue = null,
    bool IsRequired = true)
{
    /// <summary>
    /// Validates the given input value against the field's constraints.
    /// Returns an error message string if invalid, or null if valid.
    /// </summary>
    public string? Validate(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            if (IsRequired)
                return $"{Label} is required.";
            return null;
        }

        if (MaxLength.HasValue && trimmed.Length > MaxLength.Value)
            return $"{Label} must not exceed {MaxLength.Value} characters.";

        if (FieldType == ConnectionFieldType.Integer)
        {
            if (!int.TryParse(trimmed, out var intVal))
                return $"{Label} must be a valid integer.";
            if (MinValue.HasValue && intVal < MinValue.Value)
                return $"{Label} must be at least {MinValue.Value}.";
            if (MaxValue.HasValue && intVal > MaxValue.Value)
                return $"{Label} must be at most {MaxValue.Value}.";
        }
        else if (FieldType == ConnectionFieldType.Boolean)
        {
            if (!bool.TryParse(trimmed, out _))
                return $"{Label} must be 'true' or 'false'.";
        }

        return null;
    }
}
