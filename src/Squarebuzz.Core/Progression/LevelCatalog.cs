using Squarebuzz.Core.Model;

namespace Squarebuzz.Core.Progression;

/// <summary>Where a level sits relative to the player's progress.</summary>
public enum LevelNodeState
{
    /// <summary>Already finished. Replayable - a child who liked a level should be able to do it again.</summary>
    Done,

    /// <summary>The next one to play. Exactly one level is ever current.</summary>
    Current,

    /// <summary>Not reached yet. Shown, numbered and dimmed rather than hidden.</summary>
    Locked,
}

/// <summary>What one campaign level plays.</summary>
/// <param name="Level">The level number, 1-based.</param>
/// <param name="Size">Grid width and height in cells.</param>
/// <param name="Difficulty">Generator difficulty, 1-5.</param>
/// <param name="PuzzleId">Authored picture this level reveals, or null for a generated board.</param>
/// <param name="Seed">Deterministic seed, so every player gets the same level.</param>
public sealed record LevelSpec(int Level, int Size, int Difficulty, string? PuzzleId, int Seed)
{
    /// <summary>True when this level reveals an authored picture rather than a generated board.</summary>
    public bool IsMilestone => PuzzleId is not null;

    /// <summary>The new-game options that produce this level.</summary>
    public NewGameOptions ToOptions(HelperSettings helpers)
    {
        ArgumentNullException.ThrowIfNull(helpers);

        return new NewGameOptions(Size, Difficulty, LevelCatalog.GeneratedPackId, ChallengeLevel.Relaxed)
        {
            Helpers = helpers,
            Level = Level,
            PuzzleId = PuzzleId,
            Seed = Seed,

            // A generated level within the authored size range must not grab a shipped picture -
            // those are reserved for the milestone stops that reveal them deliberately.
            ForceGenerated = PuzzleId is null && GridSize.IsAuthored(Size),
        };
    }
}

/// <summary>
/// The 600-level campaign behind the Play button: every level's size, difficulty and picture,
/// as a pure function of the level number and the shipped content.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here is stored. The player's only campaign state is
/// <see cref="PlayerProgress.HighestLevelCompleted"/>; everything else - what level 317 looks
/// like, where the authored pictures sit - is recomputed from this catalog, so it cannot
/// disagree with itself across screens.
/// </para>
/// <para>
/// Levels climb through the board sizes in bands, with difficulty ramping 1-5 inside each band.
/// The authored pictures are woven into their size's band as evenly spaced milestones, in
/// content-file order - the same "the content file is the single place that decides" rule the
/// old Puzzle Path followed. Locked packs are deliberately included: meeting a fairy-tale
/// picture on the path is how that pack is earned.
/// </para>
/// </remarks>
public static class LevelCatalog
{
    /// <summary>How many levels the campaign has.</summary>
    public const int LevelCount = 600;

    /// <summary>
    /// Pack requested for generated levels. The wildcard pack, by the same convention the daily
    /// puzzle and timed trials use - the generator only takes it as a colour/flavour hint.
    /// </summary>
    public const string GeneratedPackId = "surprise";

    /// <summary>Salt mixed into level seeds so they can never collide with the daily puzzle's.</summary>
    private const uint SeedSalt = 0x4C564Cu;

    /// <summary>
    /// Earliest level within a band that may reveal a picture, as an offset from its first level.
    /// </summary>
    /// <remarks>
    /// Two, so the first level of the campaign is a plain generated board and the first picture
    /// arrives as a reward rather than as the opening move. This was always the stated intent, but
    /// <c>Math.Max(1, ...)</c> guaranteed the opposite whenever a band held nearly as many pictures
    /// as levels: the 5x5 band has 35 pictures across 40 levels, so level 1 revealed the first
    /// shipped picture - and named it in the header, since authored puzzles show their name.
    /// </remarks>
    private const int FirstMilestoneOffset = 2;

    private static readonly LevelBand[] Bands =
    [
        new(1, 40, GridSize.Tiny),
        new(41, 200, GridSize.Normal),
        new(201, 400, GridSize.Big),

        // The campaign tops out at 20x20 on purpose: 25x25 is tablet-only, and a campaign that
        // walls off its last stretch on a phone would punish the players who got there.
        new(401, 600, GridSize.Huge),
    ];

    /// <summary>Grid size for a level.</summary>
    public static int SizeFor(int level) => BandFor(level).Size;

    /// <summary>Generator difficulty for a level: 1 at its band's start, 5 by the end.</summary>
    public static int DifficultyFor(int level)
    {
        var band = BandFor(level);
        var offset = level - band.First;

        // Integer ramp: the band is split into five equal runs, one per difficulty step.
        return Math.Min(5, 1 + (offset * 5 / band.Length));
    }

    /// <summary>
    /// Deterministic seed for a level, so every player sees the same board at level N.
    /// </summary>
    public static int SeedFor(int level)
    {
        _ = BandFor(level);

        // Multiplying by a large odd constant scatters neighbouring levels, the same mixing the
        // daily puzzle uses; the salt keeps the two seed families apart.
        unchecked
        {
            var mixed = ((uint)level * 2654435761u) ^ SeedSalt;
            return (int)(mixed & 0x7FFFFFFF);
        }
    }

    /// <summary>What a single level plays, given the shipped pictures.</summary>
    public static LevelSpec Get(int level, IReadOnlyList<Puzzle> puzzles)
    {
        ArgumentNullException.ThrowIfNull(puzzles);

        var band = BandFor(level);
        var milestones = MilestonesFor(band, puzzles);
        milestones.TryGetValue(level, out var puzzleId);

        return new LevelSpec(level, band.Size, DifficultyFor(level), puzzleId, SeedFor(level));
    }

    /// <summary>Every level in order, for the level map.</summary>
    public static IReadOnlyList<LevelSpec> All(IReadOnlyList<Puzzle> puzzles)
    {
        ArgumentNullException.ThrowIfNull(puzzles);

        var specs = new List<LevelSpec>(LevelCount);

        foreach (var band in Bands)
        {
            var milestones = MilestonesFor(band, puzzles);

            for (var level = band.First; level <= band.Last; level++)
            {
                milestones.TryGetValue(level, out var puzzleId);
                specs.Add(new LevelSpec(level, band.Size, DifficultyFor(level), puzzleId, SeedFor(level)));
            }
        }

        return specs;
    }

    private static LevelBand BandFor(int level)
    {
        foreach (var band in Bands)
        {
            if (level >= band.First && level <= band.Last)
            {
                return band;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(level), level, $"Levels run 1 to {LevelCount}.");
    }

    /// <summary>
    /// Which levels of a band reveal which authored pictures: content-file order, spread evenly
    /// through the band.
    /// </summary>
    private static Dictionary<int, string> MilestonesFor(LevelBand band, IReadOnlyList<Puzzle> puzzles)
    {
        var milestones = new Dictionary<int, string>();

        if (!GridSize.IsAuthored(band.Size))
        {
            return milestones;
        }

        var authored = new List<Puzzle>();

        foreach (var puzzle in puzzles)
        {
            if (puzzle.Width == band.Size && puzzle.Height == band.Size)
            {
                authored.Add(puzzle);
            }
        }

        // One short of the band, because offset 1 is reserved - see FirstMilestoneOffset. That
        // keeps a free slot available for the collision walk below, so it always terminates.
        var count = Math.Min(authored.Count, band.Length - (FirstMilestoneOffset - 1));
        var taken = new HashSet<int>();

        for (var k = 0; k < count; k++)
        {
            // Picture k of M sits (k+1)/(M+1) of the way through the band, so the milestones
            // divide it evenly.
            var offset = Math.Max(FirstMilestoneOffset, (k + 1) * band.Length / (count + 1));

            // Integer division can land two pictures on the same level when they pack tightly;
            // the later one walks forward to the next free slot, wrapping if it must - back to
            // the first *allowed* offset, never over level one.
            while (!taken.Add(offset))
            {
                offset = offset >= band.Length ? FirstMilestoneOffset : offset + 1;
            }

            milestones[band.First + offset - 1] = authored[k].Id;
        }

        return milestones;
    }

    /// <param name="First">First level of the band, 1-based, inclusive.</param>
    /// <param name="Last">Last level, inclusive.</param>
    /// <param name="Size">Grid size every level in the band plays.</param>
    private sealed record LevelBand(int First, int Last, int Size)
    {
        public int Length => Last - First + 1;
    }
}
