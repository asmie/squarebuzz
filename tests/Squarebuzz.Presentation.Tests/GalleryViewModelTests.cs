using Squarebuzz.Core.Model;
using Squarebuzz.Presentation.Navigation;
using Squarebuzz.Presentation.Tests.Fakes;
using Squarebuzz.Presentation.ViewModels;
using Xunit;

namespace Squarebuzz.Presentation.Tests;

/// <summary>
/// The Gallery is a trophy wall that doubles as a picture picker. Nothing on it may leak an
/// unsolved answer, a found picture is the player's whatever its pack says, and the count at
/// the top has to be a number that can actually be true.
/// </summary>
public class GalleryViewModelTests : IDisposable
{
    private static readonly DateOnly Solved = new(2026, 8, 1);

    private readonly FakePuzzleRepository _puzzles = new();
    private readonly FakeProgressRepository _progress = new();
    private readonly FakeNavigationService _navigation = new();
    private readonly GalleryViewModel _vm;

    public GalleryViewModelTests()
    {
        // One open pack, one shipped locked with three pictures, and the wildcard.
        _puzzles.PacksList.Add(new PackDefinition("animals", "🐱", Locked: false, IsWildcard: false));
        _puzzles.PacksList.Add(new PackDefinition("fairy", "👑", Locked: true, IsWildcard: false));
        _puzzles.PacksList.Add(new PackDefinition("surprise", "🎁", Locked: false, IsWildcard: true));

        _puzzles.PuzzlesList.Add(Puzzle.FromRows("heart", "animals", "#FF6B8A", [".#.#.", "#####", "#####", ".###.", "..#.."]));
        _puzzles.PuzzlesList.Add(Puzzle.FromRows("crown", "fairy", "#FFC244", ["#.#.#", "#####", "#####", ".###.", "....."]));
        _puzzles.PuzzlesList.Add(Puzzle.FromRows("wand", "fairy", "#8B5CF6", ["....#", "...#.", "..#..", ".#...", "#...."]));
        _puzzles.PuzzlesList.Add(Puzzle.FromRows("castle", "fairy", "#4FA8F5", ["#.#.#", "#####", "#.#.#", "#####", "#####"]));

        _vm = new GalleryViewModel(new FakeLocalizationService(), _puzzles, _progress, _navigation);
    }

    public void Dispose()
    {
        _vm.Dispose();
        GC.SuppressFinalize(this);
    }

    private GalleryCard Card(string id) => _vm.Cards.Single(c => c.Puzzle.Id == id);

    private void MarkSolved(string id, int stars = 3, int seconds = 65) =>
        _progress.Solved.Add(new SolvedPuzzle(id, Solved, stars, TimeSpan.FromSeconds(seconds), 1));

    // ---- What a card shows ----

    [Fact]
    // Withholding the name is what makes finding one feel like a discovery - and the card must
    // carry nothing a child could read the answer from.
    public async Task AnUnfoundPicture_IsMaskedAndNameless()
    {
        await _vm.OnAppearingAsync();

        var heart = Card("heart");

        Assert.False(heart.IsFound);
        Assert.True(heart.IsMasked);
        Assert.False(heart.IsLocked);
        Assert.Equal("???", heart.Name);
        Assert.Equal("—", heart.FoundOn);
        Assert.Equal(string.Empty, heart.Stars);
        Assert.Equal("a11yNotFound 5×5", heart.Description);
    }

    [Fact]
    public async Task AFoundPicture_ShowsItsNameAndStars()
    {
        MarkSolved("heart", stars: 2, seconds: 65);

        await _vm.OnAppearingAsync();

        var heart = Card("heart");

        Assert.True(heart.IsFound);
        Assert.False(heart.IsMasked);
        Assert.Equal("Puzzle_heart", heart.Name);
        Assert.Equal("★★☆", heart.Stars);
        Assert.Contains("2026", heart.FoundOn, StringComparison.Ordinal);
        Assert.Equal("a11yFound:Puzzle_heart,2, 5×5", heart.Description);
        Assert.Equal(1, heart.Opacity);
    }

    // ---- Locks ----

    [Fact]
    public async Task APictureInALockedPack_IsLockedUntilThePackOpens()
    {
        await _vm.OnAppearingAsync();

        var crown = Card("crown");

        Assert.True(crown.IsLocked);
        Assert.True(crown.IsMasked);
        Assert.Equal("a11yLockedCard, 5×5", crown.Description);
        Assert.Equal("locked", crown.LockedLabel);
    }

    [Fact]
    // The campaign plays locked-pack pictures at their milestone levels, so a child can have
    // legitimately finished one before the pack opens. What they finished stays theirs.
    public async Task AFoundPicture_IsNeverLocked_WhateverItsPackSays()
    {
        MarkSolved("crown");

        await _vm.OnAppearingAsync();

        Assert.False(Card("crown").IsLocked);
        Assert.True(Card("crown").IsFound);

        // Its siblings are still behind the lock: one of three is not the whole pack.
        Assert.True(Card("wand").IsLocked);
        Assert.True(Card("castle").IsLocked);
    }

    [Fact]
    public async Task Play_OpensAFoundOrOpenPicture_ButNotALockedOne()
    {
        MarkSolved("crown");
        await _vm.OnAppearingAsync();

        await _vm.PlayCommand.ExecuteAsync(Card("wand"));
        Assert.Null(_navigation.Last);

        await _vm.PlayCommand.ExecuteAsync(Card("heart"));
        Assert.Equal(Routes.Game, _navigation.Last?.Route);
        Assert.Equal("heart", _navigation.Last?.Parameters?[GameViewModel.PuzzleIdParameter]);

        // A found picture in a locked pack replays too.
        await _vm.PlayCommand.ExecuteAsync(Card("crown"));
        Assert.Equal("crown", _navigation.Last?.Parameters?[GameViewModel.PuzzleIdParameter]);
    }

    // ---- The count at the top ----

    [Fact]
    public async Task TheSummary_CountsFoundPicturesOverShippedOnes()
    {
        MarkSolved("heart");
        MarkSolved("wand");

        await _vm.OnAppearingAsync();

        Assert.Equal("galleryFound:2,4", _vm.FoundSummary);
    }

    [Fact]
    // A solved row can outlive its picture when content is removed between releases. Counting
    // rows rather than cards read "5 of 4 found" - an impossible number a child notices and a
    // parent cannot explain.
    public async Task ASolvedRowWhosePictureNoLongerShips_DoesNotInflateTheSummary()
    {
        MarkSolved("heart");
        MarkSolved("dragon");

        await _vm.OnAppearingAsync();

        Assert.Equal(4, _vm.Cards.Count);
        Assert.Equal("galleryFound:1,4", _vm.FoundSummary);
    }

    // ---- Order and resilience ----

    [Fact]
    public async Task Cards_FollowTheContentFileOrder()
    {
        await _vm.OnAppearingAsync();

        Assert.Equal(["heart", "crown", "wand", "castle"], _vm.Cards.Select(c => c.Puzzle.Id));
    }

    [Fact]
    public async Task Appearing_ClearsBusyEvenAfterAReload()
    {
        await _vm.OnAppearingAsync();
        await _vm.OnAppearingAsync();

        Assert.False(_vm.IsBusy);
        Assert.Equal(4, _vm.Cards.Count);
    }
}
