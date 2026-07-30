using Squarebuzz.Core.Model;

namespace Squarebuzz.Core.Progression;

/// <summary>
/// Decides which puzzle "today's puzzle" is.
/// </summary>
/// <remarks>
/// <para>
/// The daily is generated rather than drawn from the twelve authored pictures. With only twelve,
/// a rotation would repeat every twelve days and every child would soon have seen them all;
/// generating gives a genuinely new picture each morning, and the uniqueness-checking generator
/// guarantees it is still fair.
/// </para>
/// <para>
/// The seed comes from the date alone, so every player gets the same puzzle on the same day and
/// a player who closes the app returns to the identical board.
/// </para>
/// </remarks>
public static class DailyPuzzle
{
    /// <summary>Big enough to feel like an occasion, small enough to finish in a sitting.</summary>
    public const int Size = GridSize.Normal;

    /// <summary>Middle of the range - the daily should suit any player, not just the keen ones.</summary>
    public const int Difficulty = 3;

    /// <summary>
    /// Mixed into the date so the daily seeds do not collide with the sequential seeds a
    /// normal game would produce, and so consecutive days look unrelated rather than adjacent.
    /// </summary>
    private const uint SeedSalt = 0x5B_02_01;

    /// <summary>Stable seed for a given day.</summary>
    public static int SeedFor(DateOnly date)
    {
        // Multiplying the day number by a large odd constant scatters neighbouring days, so
        // Monday and Tuesday do not produce near-identical pictures.
        unchecked
        {
            var mixed = ((uint)date.DayNumber * 2654435761u) ^ SeedSalt;
            return (int)(mixed & 0x7FFFFFFF);
        }
    }

    /// <summary>The new-game options that produce today's puzzle.</summary>
    public static NewGameOptions OptionsFor(DateOnly date, HelperSettings helpers)
    {
        ArgumentNullException.ThrowIfNull(helpers);

        return new NewGameOptions(Size, Difficulty, "surprise", ChallengeLevel.Relaxed)
        {
            Helpers = helpers,
            Seed = SeedFor(date),

            // Essential: 10x10 is within the authored range, so without this the factory would
            // serve one of the twelve shipped pictures instead of a fresh one.
            ForceGenerated = true,
        };
    }

    /// <summary>True when the daily for <paramref name="today"/> has not been finished yet.</summary>
    public static bool IsAvailable(PlayerProgress progress, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(progress);

        return progress.LastDailyCompletedOn != today;
    }
}
