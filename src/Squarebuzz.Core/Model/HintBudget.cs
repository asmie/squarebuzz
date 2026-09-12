namespace Squarebuzz.Core.Model;

/// <summary>A game's initial hint limit. Null means unlimited, independently of the helper switch.</summary>
public sealed record HintBudget
{
    public HintBudget(int? limit)
    {
        if (limit is { } count) ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        Limit = limit;
    }

    public int? Limit { get; }

    public static HintBudget Default { get; } = new(3);

    public static HintBudget Unlimited { get; } = new((int?)null);

    /// <summary>Limits used by saves written before configurable budgets existed.</summary>
    public static HintBudget LegacyFor(ChallengeLevel challenge) =>
        challenge == ChallengeLevel.Sharp ? new(1) : Default;
}
