using CompanyEmployees.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace CompanyEmployees.Web.Services;

public sealed class LanguagePreferenceService(UserManager<User> userManager)
{
    public async Task SaveAsync(string? email, string culture)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new InvalidOperationException("The signed-in account could not be identified.");

        var normalizedCulture = SupportedLanguages.Normalize(culture);
        if (!string.Equals(normalizedCulture, culture, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Select a supported language.");

        var user = await userManager.FindByEmailAsync(email)
            ?? throw new InvalidOperationException("The signed-in account no longer exists.");

        user.PreferredCulture = normalizedCulture == SupportedLanguages.DefaultCulture
            ? null
            : normalizedCulture;
        user.UpdatedAt = DateTime.UtcNow;

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
            throw new InvalidOperationException("The language preference could not be saved.");
    }
}
