using Squarebuzz.Core.Model;

namespace Squarebuzz.Presentation.Services;

/// <summary>Matches platform voice codes, including Android's separate language and country.</summary>
public static class SpeechLocaleMatcher
{
    public static bool Matches(AppLanguage requested, string? language, string? country)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return false;
        }

        var code = language.Trim().Replace('_', '-');
        var separator = code.IndexOf('-');
        var prefix = separator < 0 ? code : code[..separator];
        var wanted = requested.ToCultureCode();
        var wantedSeparator = wanted.IndexOf('-');
        var wantedPrefix = wantedSeparator < 0 ? wanted : wanted[..wantedSeparator];
        if (!string.Equals(prefix, wantedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (requested is not (AppLanguage.ChineseSimplified or AppLanguage.ChineseTraditional))
        {
            return true;
        }

        // Explicit script/region subtags take precedence over a separate country field.
        // A bare zh follows the same Simplified default as the application's language picker.
        if (separator < 0 && !string.IsNullOrWhiteSpace(country))
        {
            code = $"{code}-{country.Trim()}";
        }
        return AppLanguages.FromCultureCode(code) == requested;
    }
}
