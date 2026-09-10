using Squarebuzz.Core.Progression;

namespace Squarebuzz.Core.Model;

public enum SessionMode
{
    QuickGame,
    Campaign,
    Daily,
    TimedTrial,
}

/// <summary>Resolved challenge identity and the request needed to replay or continue it.</summary>
public sealed class SessionOrigin
{
    public SessionOrigin(Puzzle puzzle, NewGameOptions options, int seed)
    {
        ArgumentNullException.ThrowIfNull(puzzle);
        ArgumentNullException.ThrowIfNull(options);
        var modes = (options.Level is not null ? 1 : 0)
                    + (options.DailyDate is not null ? 1 : 0)
                    + (options.TimeLimit is not null ? 1 : 0);
        if (modes > 1)
        {
            throw new ArgumentException("A session can belong to only one challenge mode.", nameof(options));
        }

        Options = options with { Seed = seed };
        Mode = options.TimeLimit is not null ? SessionMode.TimedTrial
            : options.Level is not null ? SessionMode.Campaign
            : options.DailyDate is not null ? SessionMode.Daily
            : SessionMode.QuickGame;
        PuzzleId = puzzle.IsGenerated ? null : puzzle.Id;
        GeneratorVersion = puzzle.IsGenerated ? Generation.GeneratorVersion.Current : Generation.GeneratorVersion.Unknown;
    }

    public NewGameOptions Options { get; }
    public SessionMode Mode { get; }
    public string? PuzzleId { get; }
    public int GeneratorVersion { get; }
    public int Seed => Options.Seed!.Value;
    public int? Level => Options.Level;
    public DateOnly? DailyDate => Options.DailyDate;
    public TimeSpan? TimeLimit => Options.TimeLimit;
    public int Difficulty => Options.Difficulty;
    public string PackId => Options.PackId;
    public ChallengeLevel Challenge => Options.Challenge;
    public bool ForceGenerated => Options.ForceGenerated;

    /// <summary>Campaign and daily restarts preserve identity; ordinary and timed games draw afresh.</summary>
    public NewGameOptions Replay(HelperSettings helpers, bool restartDaily = true)
    {
        ArgumentNullException.ThrowIfNull(helpers);
        if (Mode == SessionMode.Daily && restartDaily)
        {
            return DailyPuzzle.OptionsFor(DailyDate!.Value, helpers);
        }

        if (Mode == SessionMode.Campaign)
        {
            return Options with { Helpers = helpers, PuzzleId = PuzzleId, ForceGenerated = PuzzleId is null };
        }

        return Options with
        {
            Helpers = helpers,
            Seed = null,
            DailyDate = null,
            PuzzleId = null,
            ExcludePuzzleId = Mode == SessionMode.TimedTrial ? null : PuzzleId,
        };
    }
}
