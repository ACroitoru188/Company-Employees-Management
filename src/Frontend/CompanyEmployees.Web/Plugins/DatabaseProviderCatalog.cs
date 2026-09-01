using CompanyEmployees.Persistence.Contracts;

namespace CompanyEmployees.Web.Plugins;

/// <summary>
/// Catalog of discovered <see cref="IDbProviderPlugin"/> instances.
/// </summary>
public sealed class DatabaseProviderCatalog
{
    private readonly IReadOnlyList<IDbProviderPlugin> _plugins;

    public DatabaseProviderCatalog(
        IReadOnlyList<IDbProviderPlugin> plugins,
        IConfiguration configuration)
    {
        var allowedRaw = configuration["DatabaseCatalog:AllowedProviders"];
        if (string.IsNullOrWhiteSpace(allowedRaw))
        {
            _plugins = plugins;
        }
        else
        {
            var allowed = allowedRaw
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            _plugins = plugins
                .Where(p => allowed.Contains(p.Id))
                .ToList()
                .AsReadOnly();
        }
    }

    public IReadOnlyList<IDbProviderPlugin> GetAvailable() => _plugins;

    public IDbProviderPlugin GetById(string id) =>
        _plugins.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException(
            $"Database provider '{id}' is not available. " +
            $"Available providers: {string.Join(", ", _plugins.Select(p => p.Id))}");

    public IDbProviderPlugin? FindById(string? id) =>
        id is null ? null
        : _plugins.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
}
