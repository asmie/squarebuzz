namespace Squarebuzz.Core.Model;

/// <summary>
/// The challenge dial, deliberately separate from grid size and clue difficulty so a player
/// can have a big easy puzzle or a small strict one.
/// </summary>
public enum ChallengeLevel
{
    /// <summary>Helpers on, no limits.</summary>
    Relaxed,

    /// <summary>Mistakes counted, no auto-crossing, one hint.</summary>
    Sharp,
}

/// <summary>Which assists the player has switched on in Options.</summary>
public sealed record HelperSettings
{
    public bool AutoCross { get; init; } = true;

    public bool WarnOnMistakes { get; init; } = true;

    public bool ShowTimer { get; init; } = true;

    public bool AllowHints { get; init; } = true;

    public static HelperSettings Default { get; } = new();
}

/// <summary>
/// The rules in force for one puzzle, resolved from the challenge level and the player's
/// helper preferences. An Options-pattern value object: <see cref="GameSession"/> reads it
/// and never re-derives any of this itself.
/// </summary>
public sealed record GameRules
{
    private const int RelaxedHintAllowance = 3;
    private const int SharpHintAllowance = 1;

    /// <summary>Cross off the rest of a line automatically once its clue is satisfied.</summary>
    public bool AutoCrossCompletedLines { get; init; }

    /// <summary>Refuse a fill that contradicts the picture, instead of letting it stand.</summary>
    public bool WarnOnMistakes { get; init; }

    /// <summary>Hints available for this puzzle. Zero when hints are switched off entirely.</summary>
    public int HintAllowance { get; init; }

    public bool ShowTimer { get; init; }

    /// <summary>
    /// Resolves the rules. Auto-crossing needs both the helper enabled and a relaxed
    /// challenge - Sharp explicitly withholds it.
    /// </summary>
    public static GameRules Create(ChallengeLevel level, HelperSettings helpers)
    {
        ArgumentNullException.ThrowIfNull(helpers);

        return new GameRules
        {
            AutoCrossCompletedLines = helpers.AutoCross && level == ChallengeLevel.Relaxed,
            WarnOnMistakes = helpers.WarnOnMistakes,
            ShowTimer = helpers.ShowTimer,
            HintAllowance = helpers.AllowHints
                ? level == ChallengeLevel.Sharp ? SharpHintAllowance : RelaxedHintAllowance
                : 0,
        };
    }

    /// <summary>The default relaxed ruleset, as a new player first meets the game.</summary>
    public static GameRules Relaxed { get; } = Create(ChallengeLevel.Relaxed, HelperSettings.Default);

    public static GameRules Sharp { get; } = Create(ChallengeLevel.Sharp, HelperSettings.Default);
}
