namespace Squarebuzz.Core.Model;

/// <summary>
/// The grid sizes the game offers, and the rules about which are usable where.
/// </summary>
public static class GridSize
{
    public const int Tiny = 5;
    public const int Normal = 10;
    public const int Big = 15;
    public const int Huge = 20;
    public const int Giant = 25;

    /// <summary>
    /// Above this size there is no authored artwork, so puzzles are generated instead.
    /// </summary>
    public const int LargestAuthoredSize = Normal;

    /// <summary>
    /// A 25x25 grid cannot show legible cells and clue gutters on a phone, so it is offered
    /// only on tablets and desktop. The prototype gated this on a 760px viewport.
    /// </summary>
    public const int SmallestTabletOnlySize = Giant;

    public static IReadOnlyList<int> All { get; } = [Tiny, Normal, Big, Huge, Giant];

    public static bool RequiresLargeScreen(int size) => size >= SmallestTabletOnlySize;

    public static bool IsAuthored(int size) => size <= LargestAuthoredSize;

    public static bool IsSupported(int size) => All.Contains(size);
}
