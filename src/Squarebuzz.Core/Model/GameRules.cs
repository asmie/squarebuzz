namespace Squarebuzz.Core.Model;

/// <summary>
/// The challenge dial, deliberately separate from grid size and clue difficulty so a player
/// can have a big easy puzzle or a small strict one.
/// </summary>
public enum ChallengeLevel
{
    /// <summary>Relaxed challenge identity; assists follow the player's preferences.</summary>
    Relaxed,

    /// <summary>One hint, also used for timed trials.</summary>
    Sharp,
}

/// <summary>Which assists the player has switched on in Options.</summary>
public sealed record HelperSettings
{
    public bool AutoCross { get; init; } = true;

    public bool WarnOnMistakes { get; init; } = true;

    public bool ShowTimer { get; init; } = true;

    public bool AllowHints { get; init; } = true;

    /// <summary>Budget for new games. Existing sessions keep their initial budget.</summary>
    public HintBudget HintBudget { get; init; } = HintBudget.Default;

    public static HelperSettings Default { get; } = new();
}

/// <summary>
/// The rules in force for one puzzle, resolved from the challenge level and the player's
/// helper preferences. An Options-pattern value object: <see cref="GameSession"/> reads it
/// and never re-derives any of this itself.
/// </summary>
public sealed record GameRules
{
    /// <summary>Cross off the rest of a line automatically once its clue is satisfied.</summary>
    public bool AutoCrossCompletedLines { get; init; }

    /// <summary>Refuse a fill that contradicts the picture, instead of letting it stand.</summary>
    public bool WarnOnMistakes { get; init; }

    public HintBudget HintBudget { get; init; } = HintBudget.Default;

    public bool AllowHints { get; init; } = true;

    /// <summary>Finite allowance; zero when disabled or unlimited. See HasUnlimitedHints.</summary>
    public int HintAllowance => AllowHints ? HintBudget.Limit ?? 0 : 0;

    public bool HasUnlimitedHints => AllowHints && HintBudget.Limit is null;

    public bool ShowTimer { get; init; }

    /// <summary>
    /// Resolves the rules.
    /// </summary>
    /// <remarks>
    /// Auto-crossing follows the helper alone, on every challenge level. It used to need a
    /// relaxed challenge as well, which made the Options switch look broken to anyone playing
    /// Sharp: the toggle said one thing and the board did another. Crossing off blanks a
    /// completed clue has already proved is bookkeeping, not a hint - it reveals nothing the
    /// player has not deduced. Sharp keeps one hint; Relaxed follows the configured budget.
    /// A supplied budget preserves the identity of a saved or restarted game.
    /// </remarks>
    public static GameRules Create(ChallengeLevel level, HelperSettings helpers, HintBudget? budget = null)
    {
        ArgumentNullException.ThrowIfNull(helpers);

        return new GameRules
        {
            AutoCrossCompletedLines = helpers.AutoCross,
            WarnOnMistakes = helpers.WarnOnMistakes,
            ShowTimer = helpers.ShowTimer,
            HintBudget = budget ?? (level == ChallengeLevel.Sharp ? HintBudget.LegacyFor(level) : helpers.HintBudget),
            AllowHints = helpers.AllowHints,
        };
    }

    /// <summary>The default relaxed ruleset, as a new player first meets the game.</summary>
    public static GameRules Relaxed { get; } = Create(ChallengeLevel.Relaxed, HelperSettings.Default);

    public static GameRules Sharp { get; } = Create(ChallengeLevel.Sharp, HelperSettings.Default);
}
