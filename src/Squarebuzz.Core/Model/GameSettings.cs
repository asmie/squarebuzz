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

/// <summary>Languages the game ships in.</summary>
public enum AppLanguage
{
    English,
    Polish,
    Spanish,
}

/// <summary>Maps <see cref="AppLanguage"/> to and from the culture codes .NET resources use.</summary>
public static class AppLanguages
{
    public static string ToCultureCode(this AppLanguage language) => language switch
    {
        AppLanguage.Polish => "pl",
        AppLanguage.Spanish => "es",
        _ => "en",
    };

    public static AppLanguage FromCultureCode(string? cultureCode)
    {
        // Match on the language part only, so "pl-PL" and "es-MX" still resolve.
        var prefix = cultureCode?.Split('-')[0];

        return prefix?.ToLowerInvariant() switch
        {
            "pl" => AppLanguage.Polish,
            "es" => AppLanguage.Spanish,
            _ => AppLanguage.English,
        };
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

    public AppLanguage Language { get; init; } = AppLanguage.English;

    public HelperSettings Helpers { get; init; } = HelperSettings.Default;

    public int LastSize { get; init; } = GridSize.Tiny;

    public int LastDifficulty { get; init; } = 2;

    public string LastPackId { get; init; } = "animals";

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
        LastPackId = string.IsNullOrWhiteSpace(LastPackId) ? "animals" : LastPackId,
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
