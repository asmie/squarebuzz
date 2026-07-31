using Squarebuzz.Core.Model;
using Squarebuzz.Core.Progression;
using Xunit;

namespace Squarebuzz.Core.Tests.Progression;

public class PackUnlocksTests
{
    private static Puzzle InPack(string id, string pack) =>
        Puzzle.FromRows(id, pack, "#FF8A3D",
        [
            "#.",
            ".#",
        ]);

    private static readonly PackDefinition Open = new("animals", "🦊", Locked: false, IsWildcard: false);
    private static readonly PackDefinition Fairy = new("fairy", "👑", Locked: true, IsWildcard: false);

    private static readonly IReadOnlyList<Puzzle> Puzzles =
    [
        InPack("fox", "animals"),
        InPack("ghost", "fairy"),
        InPack("crown", "fairy"),
    ];

    [Fact]
    public void AnOpenPack_IsAlwaysUnlocked()
    {
        Assert.True(PackUnlocks.IsUnlocked(Open, Puzzles, []));
    }

    [Fact]
    public void ALockedPack_StartsLocked()
    {
        Assert.False(PackUnlocks.IsUnlocked(Fairy, Puzzles, []));
    }

    [Fact]
    public void ALockedPack_StaysLockedWhileAnyOfItsPicturesIsUnfound()
    {
        // Solving pictures from other packs changes nothing either.
        Assert.False(PackUnlocks.IsUnlocked(Fairy, Puzzles, ["ghost", "fox"]));
    }

    [Fact]
    public void ALockedPack_OpensWhenEveryOneOfItsPicturesHasBeenSolved()
    {
        Assert.True(PackUnlocks.IsUnlocked(Fairy, Puzzles, ["ghost", "crown"]));
    }

    [Fact]
    public void ALockedPackWithNoPictures_CannotSpringOpen()
    {
        var empty = new PackDefinition("mystery", "❓", Locked: true, IsWildcard: false);

        Assert.False(PackUnlocks.IsUnlocked(empty, Puzzles, ["fox", "ghost", "crown"]));
    }

    [Fact]
    public void UnlockedPackIds_ListsOpenAndEarnedPacksTogether()
    {
        var unlocked = PackUnlocks.UnlockedPackIds([Open, Fairy], Puzzles, ["ghost", "crown"]);

        Assert.Contains("animals", unlocked);
        Assert.Contains("fairy", unlocked);
    }

    [Fact]
    public void UnlockedPackIds_LeavesUnearnedPacksOut()
    {
        var unlocked = PackUnlocks.UnlockedPackIds([Open, Fairy], Puzzles, ["ghost"]);

        Assert.Contains("animals", unlocked);
        Assert.DoesNotContain("fairy", unlocked);
    }
}
