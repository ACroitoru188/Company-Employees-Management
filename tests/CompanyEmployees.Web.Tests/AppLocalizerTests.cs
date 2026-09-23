using System.Globalization;
using System.Text.Json;
using CompanyEmployees.Web.Services;
using Microsoft.AspNetCore.Hosting;
using NSubstitute;

namespace CompanyEmployees.Web.Tests;

// AppLocalizer is a singleton built at startup, so anything wrong with the language files is
// not a missing label — it throws while the container is being built and every page answers
// 500. These run against the real Web/Languages folder for exactly that reason: a fixture of
// invented files would prove nothing about the ones that ship.
public class AppLocalizerTests
{
    private static readonly string LanguagesRoot = WebContentRoot();

    [Fact]
    public void Every_supported_language_has_a_file_that_loads()
    {
        // The constructor throws on a missing or malformed file, so simply building it is the
        // assertion — this is the startup failure, reproduced in a test instead of in prod.
        var localizer = new AppLocalizer(Environment());

        Assert.NotNull(localizer);
    }

    [Fact]
    public void No_language_file_is_missing_a_key_that_English_has()
    {
        var english = Keys(SupportedLanguages.DefaultCulture);

        var gaps = new List<string>();
        foreach (var language in SupportedLanguages.All)
        {
            var missing = english.Except(Keys(language.Culture), StringComparer.OrdinalIgnoreCase)
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToList();

            if (missing.Count > 0)
                gaps.Add($"{language.Culture}: {missing.Count} missing, first is \"{missing[0]}\"");
        }

        Assert.True(gaps.Count == 0,
            "Adding UI text means adding the key to every language file:\n" + string.Join("\n", gaps));
    }

    [Fact]
    public void No_language_file_carries_a_key_English_does_not_have()
    {
        // A stale key is how a rename half-lands: the old text keeps working in one language
        // and silently falls back everywhere else.
        var english = Keys(SupportedLanguages.DefaultCulture);

        var strays = new List<string>();
        foreach (var language in SupportedLanguages.All)
        {
            var extra = Keys(language.Culture).Except(english, StringComparer.OrdinalIgnoreCase).ToList();
            if (extra.Count > 0)
                strays.Add($"{language.Culture}: {extra.Count} unknown, first is \"{extra[0]}\"");
        }

        Assert.True(strays.Count == 0, string.Join("\n", strays));
    }

    [Fact]
    public void Placeholders_survive_every_translation()
    {
        // A translation that drops {0} does not render wrong — string.Format throws, and the
        // page dies on whatever row happened to use it.
        var english = Load(SupportedLanguages.DefaultCulture);
        var placeholder = new System.Text.RegularExpressions.Regex(@"\{\d+\}");

        var broken = new List<string>();
        foreach (var language in SupportedLanguages.All.Where(l => l.Culture != SupportedLanguages.DefaultCulture))
        {
            var translated = Load(language.Culture);
            foreach (var (key, source) in english)
            {
                var expected = placeholder.Matches(source).Select(m => m.Value).Distinct().OrderBy(v => v);
                if (!translated.TryGetValue(key, out var value))
                    continue;

                var actual = placeholder.Matches(value).Select(m => m.Value).Distinct().OrderBy(v => v);
                if (!expected.SequenceEqual(actual))
                    broken.Add($"{language.Culture}: \"{key}\"");
            }
        }

        Assert.True(broken.Count == 0,
            "These translations changed their placeholders:\n" + string.Join("\n", broken));
    }

    [Fact]
    public void An_unknown_key_falls_back_to_the_key_itself()
    {
        // Untranslated UI must still read as English rather than as an empty box.
        var localizer = new AppLocalizer(Environment());

        Assert.Equal("Definitely not a key", localizer["Definitely not a key"]);
    }

    [Fact]
    public void A_key_missing_from_a_culture_falls_back_to_English()
    {
        var localizer = new AppLocalizer(Environment());
        var previous = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("ja");
            // Not a real key in any file, so Japanese cannot answer it either.
            Assert.Equal("No such label", localizer["No such label"]);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void Format_fills_the_placeholders()
    {
        var localizer = new AppLocalizer(Environment());

        Assert.Equal("7 people", localizer.Format("{0} people", 7));
    }

    // --- fixture ----------------------------------------------------------------------

    private static IWebHostEnvironment Environment()
    {
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.ContentRootPath.Returns(LanguagesRoot);
        return environment;
    }

    private static Dictionary<string, string> Load(string culture)
    {
        using var stream = File.OpenRead(Path.Combine(LanguagesRoot, "Languages", $"{culture}.json"));
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }

    private static HashSet<string> Keys(string culture) =>
        new(Load(culture).Keys, StringComparer.OrdinalIgnoreCase);

    // Walks up from the test binaries to the repo root rather than hard-coding a depth, so the
    // path survives a change of target framework or output layout.
    private static string WebContentRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CompanyEmployees.slnx")))
            directory = directory.Parent;

        Assert.True(directory is not null, "Could not find the repository root from the test output folder.");
        return Path.Combine(directory!.FullName, "src", "Frontend", "CompanyEmployees.Web");
    }
}
