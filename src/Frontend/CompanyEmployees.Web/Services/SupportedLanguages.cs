using System.Globalization;

namespace CompanyEmployees.Web.Services;

public sealed record SupportedLanguage(string Culture, string NativeName);

public static class SupportedLanguages
{
    public const string DefaultCulture = "en";

    // One primary UI language for every seeded region, with shared languages
    // deduplicated. Language remains a user choice and is never region-locked.
    public static readonly IReadOnlyList<SupportedLanguage> All =
    [
        new("en", "English"),
        new("ar", "العربية"),
        new("cs", "Čeština"),
        new("da", "Dansk"),
        new("de", "Deutsch"),
        new("es", "Español"),
        new("fi", "Suomi"),
        new("fr", "Français"),
        new("hi", "हिन्दी"),
        new("hu", "Magyar"),
        new("it", "Italiano"),
        new("ja", "日本語"),
        new("nb", "Norsk bokmål"),
        new("nl", "Nederlands"),
        new("pl", "Polski"),
        new("pt", "Português"),
        new("ro", "Română"),
        new("sv", "Svenska"),
        new("tr", "Türkçe"),
        new("ur", "اردو"),
        new("zh-Hans", "简体中文")
    ];

    public static string Normalize(string? culture)
    {
        if (string.IsNullOrWhiteSpace(culture))
            return DefaultCulture;

        var exact = All.FirstOrDefault(language =>
            string.Equals(language.Culture, culture, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return exact.Culture;

        try
        {
            var neutralName = CultureInfo.GetCultureInfo(culture).TwoLetterISOLanguageName;
            return All.FirstOrDefault(language => language.Culture == neutralName)?.Culture
                ?? DefaultCulture;
        }
        catch (CultureNotFoundException)
        {
            return DefaultCulture;
        }
    }
}
