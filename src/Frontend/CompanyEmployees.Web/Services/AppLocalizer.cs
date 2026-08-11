using System.Globalization;
using System.Text.Json;

namespace CompanyEmployees.Web.Services;

public sealed class AppLocalizer
{
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _translations;

    public AppLocalizer(IWebHostEnvironment environment)
    {
        var languagesPath = Path.Combine(environment.ContentRootPath, "Languages");
        var loaded = new Dictionary<string, IReadOnlyDictionary<string, string>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var language in SupportedLanguages.All)
        {
            var filePath = Path.Combine(languagesPath, $"{language.Culture}.json");
            if (!File.Exists(filePath))
                throw new InvalidOperationException($"Missing language file: {filePath}");

            using var stream = File.OpenRead(filePath);
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
                ?? throw new InvalidOperationException(
                    $"Language file '{filePath}' does not contain a JSON object.");

            loaded[language.Culture] = new Dictionary<string, string>(
                values,
                StringComparer.OrdinalIgnoreCase);
        }

        if (loaded[SupportedLanguages.DefaultCulture].Count == 0)
            throw new InvalidOperationException("The English language file cannot be empty.");

        _translations = loaded;
    }

    public string this[string key]
    {
        get
        {
            var culture = SupportedLanguages.Normalize(CultureInfo.CurrentUICulture.Name);
            return _translations.TryGetValue(culture, out var values)
                && values.TryGetValue(key, out var translated)
                    ? translated
                    : _translations[SupportedLanguages.DefaultCulture].GetValueOrDefault(key, key);
        }
    }

    public string Format(string key, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, this[key], arguments);

}
