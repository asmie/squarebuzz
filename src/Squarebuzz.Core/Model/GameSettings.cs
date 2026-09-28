namespace Squarebuzz.Core.Model;

/// <summary>What a plain tap on the board does.</summary>
public enum TapBehaviour
{
    /// <summary>A mode button switches between filling and crossing. The prototype's default.</summary>
    ModeButton,

    /// <summary>Tap fills, press and hold crosses.</summary>
    HoldToCross,
}

/// <summary>Which side the action buttons sit on.</summary>
public enum Handedness
{
    Left,
    Right,
}

/// <summary>
/// Languages the game ships in.
/// </summary>
/// <remarks>
/// Persisted by name rather than ordinal (see the settings repository), so members can be added
/// in any position without resetting anyone's choice. Each one needs a matching
/// <c>AppStrings.&lt;code&gt;.resx</c> satellite, or it silently falls back to English.
/// </remarks>
public enum AppLanguage
{
    English,
    Polish,
    Spanish,
    German,
    French,
    Italian,
    Portuguese,
    Dutch,
    Czech,
    Slovak,
    Slovenian,
    Romanian,
    Bulgarian,
    Russian,
    Ukrainian,
    Lithuanian,
    Latvian,
    Estonian,
    Finnish,
    Turkish,
    Japanese,
    Arabic,
    ChineseSimplified,
    ChineseTraditional,
    Norwegian,
    Icelandic,
    Swedish,
    Georgian,
    Armenian,
    Swahili,
    Zulu,
    Albanian,
    Greek,
    Persian,
    Maltese,
    Hindi,
    Bengali,
    Hebrew,
    Croatian,
}

/// <summary>
/// One shippable language: the enum member, its culture code, and the name it calls itself.
/// </summary>
/// <param name="Language">The enum member.</param>
/// <param name="CultureCode">
/// The culture code, matching the resx satellite's suffix. Two letters for all but Chinese, which
/// needs its script to tell the two written forms apart - see <see cref="AppLanguages.FromCultureCode"/>.
/// </param>
/// <param name="Endonym">
/// What the language calls itself - "Deutsch", not "German". Deliberately not translated: a
/// player hunting for their own language recognises it written its own way, whatever the app is
/// currently showing.
/// </param>
/// <param name="IsRightToLeft">True for scripts that read right to left.</param>
/// <param name="DisplayFontCovers">
/// False when the bundled display face has no glyphs for this language, so headings must use the
/// body face instead. See <see cref="AppLanguages.All"/> for why this is data rather than a guess.
/// </param>
public sealed record LanguageInfo(
    AppLanguage Language,
    string CultureCode,
    string Endonym,
    bool IsRightToLeft = false,
    bool DisplayFontCovers = true);

/// <summary>Maps <see cref="AppLanguage"/> to and from the culture codes .NET resources use.</summary>
public static class AppLanguages
{
    /// <summary>Supported languages in picker order: English first, followed by the established language groups.</summary>
    /// <remarks>
    /// DisplayFontCovers records coverage of the bundled display font. Recheck it when fonts
    /// change; see Resources/Fonts/README.md.
    /// </remarks>
    public static IReadOnlyList<LanguageInfo> All { get; } =
    [
        new(AppLanguage.English, "en", "English"),
        new(AppLanguage.Arabic, "ar", "العربية", IsRightToLeft: true, DisplayFontCovers: false),
        new(AppLanguage.Bulgarian, "bg", "Български", DisplayFontCovers: false),
        new(AppLanguage.Czech, "cs", "Čeština", DisplayFontCovers: false),
        new(AppLanguage.German, "de", "Deutsch"),
        new(AppLanguage.Estonian, "et", "Eesti"),
        new(AppLanguage.Spanish, "es", "Español"),
        new(AppLanguage.French, "fr", "Français"),
        new(AppLanguage.Croatian, "hr", "Hrvatski", DisplayFontCovers: false),
        new(AppLanguage.Zulu, "zu", "isiZulu"),
        new(AppLanguage.Icelandic, "is", "Íslenska"),
        new(AppLanguage.Italian, "it", "Italiano"),
        new(AppLanguage.Swahili, "sw", "Kiswahili"),
        new(AppLanguage.Latvian, "lv", "Latviešu", DisplayFontCovers: false),
        new(AppLanguage.Lithuanian, "lt", "Lietuvių", DisplayFontCovers: false),
        new(AppLanguage.Maltese, "mt", "Malti", DisplayFontCovers: false),
        new(AppLanguage.Dutch, "nl", "Nederlands"),
        new(AppLanguage.Norwegian, "no", "Norsk"),
        new(AppLanguage.Polish, "pl", "Polski"),
        new(AppLanguage.Portuguese, "pt", "Português"),
        new(AppLanguage.Romanian, "ro", "Română", DisplayFontCovers: false),
        new(AppLanguage.Albanian, "sq", "Shqip"),
        new(AppLanguage.Slovak, "sk", "Slovenčina", DisplayFontCovers: false),
        new(AppLanguage.Slovenian, "sl", "Slovenščina", DisplayFontCovers: false),
        new(AppLanguage.Finnish, "fi", "Suomi"),
        new(AppLanguage.Swedish, "sv", "Svenska"),
        new(AppLanguage.Turkish, "tr", "Türkçe"),

        // Group these entries by script while preserving the established picker order.
        new(AppLanguage.Ukrainian, "uk", "Українська", DisplayFontCovers: false),
        new(AppLanguage.Russian, "ru", "Русский", DisplayFontCovers: false),
        new(AppLanguage.Greek, "el", "Ελληνικά", DisplayFontCovers: false),
        new(AppLanguage.Armenian, "hy", "Հայերեն", DisplayFontCovers: false),
        new(AppLanguage.Georgian, "ka", "ქართული", DisplayFontCovers: false),

        // Fredoka includes Hebrew glyphs; Quicksand uses platform fallback for Hebrew body text.
        new(AppLanguage.Hebrew, "he", "עברית", IsRightToLeft: true),

        new(AppLanguage.Persian, "fa", "فارسی", IsRightToLeft: true, DisplayFontCovers: false),
        new(AppLanguage.Hindi, "hi", "हिन्दी", DisplayFontCovers: false),
        new(AppLanguage.Bengali, "bn", "বাংলা", DisplayFontCovers: false),
        new(AppLanguage.ChineseSimplified, "zh-Hans", "中文（简体）", DisplayFontCovers: false),
        new(AppLanguage.ChineseTraditional, "zh-Hant", "中文（繁體）", DisplayFontCovers: false),
        new(AppLanguage.Japanese, "ja", "日本語", DisplayFontCovers: false),
    ];

    public static string ToCultureCode(this AppLanguage language)
    {
        foreach (var info in All)
        {
            if (info.Language == language)
            {
                return info.CultureCode;
            }
        }

        return "en";
    }

    /// <summary>True when the language's script reads right to left, so the UI must mirror.</summary>
    public static bool IsRightToLeft(this AppLanguage language)
    {
        foreach (var info in All)
        {
            if (info.Language == language)
            {
                return info.IsRightToLeft;
            }
        }

        return false;
    }

    /// <summary>
    /// True when headings can use the display face. False means the body face has to stand in -
    /// see the remarks on <see cref="All"/>.
    /// </summary>
    public static bool DisplayFontCovers(this AppLanguage language)
    {
        foreach (var info in All)
        {
            if (info.Language == language)
            {
                return info.DisplayFontCovers;
            }
        }

        return true;
    }

    /// <summary>Regions that write Chinese in traditional characters.</summary>
    private static readonly string[] TraditionalChineseRegions = ["TW", "HK", "MO"];

    /// <summary>
    /// The shipped language a platform culture code asks for, or English when the game does not
    /// have it.
    /// </summary>
    /// <remarks>
    /// Matching is by language subtag, so "pl-PL" and "pt-BR" resolve to Polish and Portuguese.
    /// Chinese is the one language where that is not enough: Simplified and Traditional share the
    /// subtag "zh" and differ only by script, so it is resolved from the script when the code
    /// carries one ("zh-Hant", "zh-Hant-TW") and from the region when it does not ("zh-TW").
    /// A bare "zh" says nothing either way and takes Simplified, the larger audience.
    /// </remarks>
    public static AppLanguage FromCultureCode(string? cultureCode)
    {
        if (string.IsNullOrEmpty(cultureCode))
        {
            return AppLanguage.English;
        }

        // An exact hit first, so a code that already names its script keeps that script.
        foreach (var info in All)
        {
            if (string.Equals(info.CultureCode, cultureCode, StringComparison.OrdinalIgnoreCase))
            {
                return info.Language;
            }
        }

        var parts = cultureCode.Split('-');
        var prefix = parts[0];

        if (string.Equals(prefix, "zh", StringComparison.OrdinalIgnoreCase))
        {
            return IsTraditionalChinese(parts)
                ? AppLanguage.ChineseTraditional
                : AppLanguage.ChineseSimplified;
        }

        foreach (var info in All)
        {
            if (string.Equals(info.CultureCode, prefix, StringComparison.OrdinalIgnoreCase))
            {
                return info.Language;
            }
        }

        return AppLanguage.English;
    }

    private static bool IsTraditionalChinese(string[] parts)
    {
        foreach (var part in parts)
        {
            if (string.Equals(part, "Hant", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(part, "Hans", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            foreach (var region in TraditionalChineseRegions)
            {
                if (string.Equals(part, region, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }
}

/// <summary>
/// Everything the player can change in Options, plus the choices they last made on the New
/// Game screen so the game reopens where they left off.
/// </summary>
public sealed record GameSettings
{
    public const int MinCellZoomPercent = 70;
    public const int MaxCellZoomPercent = 160;
    public const int DefaultCellZoomPercent = 100;

    public bool SoundEffects { get; init; } = true;

    public bool Music { get; init; }

    public bool VoiceNarration { get; init; }

    public bool Haptics { get; init; } = true;

    public GameTheme Theme { get; init; } = GameTheme.Light;

    public GameAccent Accent { get; init; } = GameAccent.Tangerine;

    /// <summary>
    /// Track the OS light/dark setting instead of using <see cref="Theme"/> directly.
    /// The prototype offered this as a third "Auto" option next to Light and Dark.
    /// </summary>
    public bool FollowSystemTheme { get; init; }

    /// <summary>Larger clue numerals, for younger players and poorer eyesight.</summary>
    public bool BigNumbers { get; init; }

    public TapBehaviour TapBehaviour { get; init; } = TapBehaviour.ModeButton;

    public Handedness Handedness { get; init; } = Handedness.Right;

    /// <summary>Board zoom as a percentage, clamped to 70-160.</summary>
    public int CellZoomPercent { get; init; } = DefaultCellZoomPercent;

    /// <summary>Optional 3x3 magnifier around the touched cell. Disabled by default.</summary>
    public bool ShowMagnifier { get; init; }

    public AppLanguage Language { get; init; } = AppLanguage.English;

    public HelperSettings Helpers { get; init; } = HelperSettings.Default;

    public int LastSize { get; init; } = GridSize.Tiny;

    public int LastDifficulty { get; init; } = 2;

    /// <summary>Initial Quick Game pack. Surprise draws from all unlocked packs.</summary>
    public const string DefaultPackId = "surprise";

    public string LastPackId { get; init; } = DefaultPackId;

    public ChallengeLevel LastChallenge { get; init; } = ChallengeLevel.Relaxed;

    /// <summary>Screen-time reminder in minutes, set in the parent zone. Null when off.</summary>
    public int? ScreenTimeLimitMinutes { get; init; }

    /// <summary>
    /// False until the player has been through (or skipped) the three onboarding cards, which
    /// is how the app knows to show them only on a genuine first run.
    /// </summary>
    public bool HasSeenOnboarding { get; init; }

    public static GameSettings Default { get; } = new();

    /// <summary>
    /// Brings out-of-range values back into range. Applied after loading, so a hand-edited or
    /// downgraded database can never put the UI into an impossible state.
    /// </summary>
    public GameSettings Sanitised() => this with
    {
        CellZoomPercent = Math.Clamp(CellZoomPercent, MinCellZoomPercent, MaxCellZoomPercent),
        LastDifficulty = Math.Clamp(LastDifficulty, PuzzleRequest_MinDifficulty, PuzzleRequest_MaxDifficulty),
        LastSize = GridSize.IsSupported(LastSize) ? LastSize : GridSize.Tiny,
        LastPackId = string.IsNullOrWhiteSpace(LastPackId) ? DefaultPackId : LastPackId,
    };

    // Mirrors Generation.PuzzleRequest's bounds without taking a dependency on it, since
    // this type is also used by screens that never touch generation.
    private const int PuzzleRequest_MinDifficulty = 1;
    private const int PuzzleRequest_MaxDifficulty = 5;

    /// <summary>The new-game options implied by the player's last choices.</summary>
    public NewGameOptions ToNewGameOptions() => new(LastSize, LastDifficulty, LastPackId, LastChallenge)
    {
        Helpers = Helpers,
    };
}
