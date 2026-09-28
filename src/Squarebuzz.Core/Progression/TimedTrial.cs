using Squarebuzz.Core.Model;

namespace Squarebuzz.Core.Progression;

/// <summary>One rung of the Timed Trial ladder.</summary>
/// <param name="Tier">1-based, and the suffix of its <c>timedName</c> / <c>timedSub</c> strings.</param>
/// <param name="Size">Grid edge.</param>
/// <param name="Limit">How long the player gets.</param>
public sealed record TimedTier(int Tier, int Size, TimeSpan Limit)
{
    /// <summary>Clock as the card shows it, e.g. "3:00".</summary>
    public string ClockText => $"{(int)Limit.TotalMinutes}:{Limit.Seconds:00}";

    /// <summary>The options that start this trial.</summary>
    /// <remarks>
    /// Generated rather than authored, and always with a fresh seed: a timed trial the player has
    /// already seen is not a test of anything. <see cref="NewGameOptions.ForceGenerated"/> matters
    /// because 5x5 and 10x10 both fall inside the authored range.
    /// </remarks>
    public NewGameOptions ToOptions(HelperSettings helpers)
    {
        ArgumentNullException.ThrowIfNull(helpers);

        return new NewGameOptions(Size, Difficulty, "surprise", ChallengeLevel.Sharp)
        {
            Helpers = helpers,
            ForceGenerated = true,
            TimeLimit = Limit,
            TimedTier = Tier,
        };
    }

    /// <summary>
    /// Middle of the range for every tier. The ladder's difficulty is the clock, not the clues -
    /// varying both at once would make it impossible to tell which rung beat you.
    /// </summary>
    private const int Difficulty = 3;
}

/// <summary>
/// The timed trials: the design's Warm-up, Steady and Lightning, and three more rungs after them.
/// </summary>
/// <remarks>
/// <para>
/// The first three figures are the prototype's: 5x5 in three minutes, 10x10 in five, then the
/// same 10x10 in two. The jump that matters is the third - the same grid as the second with well
/// under half the time, which is what makes it read as a dare rather than as more of the same.
/// </para>
/// <para>
/// Sprint is Warm-up's 5x5 in half the time, a step before the first 10x10 race. Big Race and
/// Marathon then move up a grid size each, with time to match - a 20x20 is a long
/// puzzle for a child even untimed, so the Marathon is about stamina rather than speed. The tier
/// number is an identity (strings, routes, trophies), so new rungs get new numbers and the list
/// order alone decides where they appear.
/// </para>
/// <para>
/// A trial is played under <see cref="ChallengeLevel.Sharp"/>, so mistakes are counted and there
/// is a single hint - a race decided by three free reveals would not be much of a race. The
/// player's own helper switches still apply, auto-crossing included: see
/// <see cref="Model.GameRules.Create"/> for why that is not the same as handing out help.
/// </para>
/// </remarks>
public static class TimedTrial
{
    /// <summary>The last and longest rung, which has a trophy of its own.</summary>
    public const int MarathonTier = 6;

    /// <summary>Every rung, in display order: easiest first.</summary>
    public static IReadOnlyList<TimedTier> Tiers { get; } =
    [
        new(1, GridSize.Tiny, TimeSpan.FromMinutes(3)),
        new(4, GridSize.Tiny, TimeSpan.FromSeconds(90)),
        new(2, GridSize.Normal, TimeSpan.FromMinutes(5)),
        new(3, GridSize.Normal, TimeSpan.FromMinutes(2)),
        new(5, GridSize.Big, TimeSpan.FromMinutes(10)),
        new(MarathonTier, GridSize.Huge, TimeSpan.FromMinutes(20)),
    ];

    /// <summary>The tier with that number, or null if there is none.</summary>
    public static TimedTier? Find(int tier)
    {
        foreach (var candidate in Tiers)
        {
            if (candidate.Tier == tier)
            {
                return candidate;
            }
        }

        return null;
    }
}
