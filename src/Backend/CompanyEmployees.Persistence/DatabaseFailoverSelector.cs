using CompanyEmployees.Persistence.Contracts;
using Microsoft.Extensions.Configuration;

namespace CompanyEmployees.Persistence;

public static class DatabaseFailoverSelector
{
    public static async Task<DatabaseRuntimeState> SelectAsync(
        IDbProviderPlugin primaryPlugin,
        string primaryConnectionString,
        IDbProviderPlugin? secondaryPlugin,
        string? secondaryConnectionString,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        var supportContact = configuration["DatabaseFailover:SupportContact"] ?? "1234-23124";

        var forcedProvider = configuration["DatabaseFailover:ForceProvider"];
        if (secondaryPlugin != null
            && string.Equals(forcedProvider, secondaryPlugin.Id, StringComparison.OrdinalIgnoreCase))
        {
            await secondaryPlugin.TestConnectionAsync(secondaryConnectionString!, cancellationToken);
            return new DatabaseRuntimeState(
                primaryProviderId: primaryPlugin.Id,
                activeProviderId: secondaryPlugin.Id,
                primaryAvailable: false,
                supportContact: supportContact,
                secondaryProviderId: secondaryPlugin.Id,
                failoverReason: $"Provider forced via DatabaseFailover:ForceProvider.",
                secondaryAvailable: true);
        }

        if (string.Equals(forcedProvider, primaryPlugin.Id, StringComparison.OrdinalIgnoreCase))
        {
            await primaryPlugin.TestConnectionAsync(primaryConnectionString, cancellationToken);
            return new DatabaseRuntimeState(
                primaryProviderId: primaryPlugin.Id,
                activeProviderId: primaryPlugin.Id,
                primaryAvailable: true,
                supportContact: supportContact,
                secondaryProviderId: secondaryPlugin?.Id);
        }

        try
        {
            await primaryPlugin.TestConnectionAsync(primaryConnectionString, cancellationToken);
            return new DatabaseRuntimeState(
                primaryProviderId: primaryPlugin.Id,
                activeProviderId: primaryPlugin.Id,
                primaryAvailable: true,
                supportContact: supportContact,
                secondaryProviderId: secondaryPlugin?.Id);
        }
        catch (Exception primaryException) when (primaryException is not OperationCanceledException)
        {
            if (secondaryPlugin == null || string.IsNullOrEmpty(secondaryConnectionString))
                throw new InvalidOperationException(
                    $"Primary database ({primaryPlugin.DisplayName}) is unavailable and no secondary is configured.",
                    primaryException);

            if (!bool.TryParse(configuration["DatabaseFailover:Enabled"], out var enabled) || !enabled)
                throw new InvalidOperationException(
                    $"{primaryPlugin.DisplayName} is unavailable and failover is disabled.", primaryException);

            try
            {
                await secondaryPlugin.TestConnectionAsync(secondaryConnectionString, cancellationToken);
            }
            catch (Exception fallbackException) when (fallbackException is not OperationCanceledException)
            {
                throw new InvalidOperationException(
                    $"Both {primaryPlugin.DisplayName} and the {secondaryPlugin.DisplayName} fallback are unavailable.",
                    new AggregateException(primaryException, fallbackException));
            }

            return new DatabaseRuntimeState(
                primaryProviderId: primaryPlugin.Id,
                activeProviderId: secondaryPlugin.Id,
                primaryAvailable: false,
                supportContact: supportContact,
                secondaryProviderId: secondaryPlugin.Id,
                failoverReason: $"{primaryException.GetType().Name}: {primaryException.Message}",
                secondaryAvailable: true);
        }
    }
}
