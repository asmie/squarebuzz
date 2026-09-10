using Squarebuzz.Core.Clues;

namespace Squarebuzz.Core.Model;

/// <summary>
/// A nonogram: the finished picture plus the clues derived from it.
/// Immutable - the player's progress lives in <see cref="GameSession"/>, never here.
/// </summary>
public sealed class Puzzle
{
    private readonly bool[] _solution;

    private Puzzle(
        string id,
        string pack,
        int width,
        int height,
        string colorHex,
        bool[] solution,
        bool isGenerated)
    {
        Id = id;
        Pack = pack;
        Width = width;
        Height = height;
        ColorHex = colorHex;
        IsGenerated = isGenerated;
        _solution = solution;

        var rowClues = new LineClues[height];
        var pictureCells = 0;

        for (var y = 0; y < height; y++)
        {
            rowClues[y] = ClueCalculator.FromSolution(_solution.AsSpan(y * width, width));

            // Free here, and it saves the board screen recounting an immutable value on every
            // painted cell just to work out how far along the player is.
            pictureCells += rowClues[y].Sum;
        }

        PictureCellCount = pictureCells;

        var columnClues = new LineClues[width];
        Span<bool> column = height <= 64 ? stackalloc bool[height] : new bool[height];
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                column[y] = _solution[(y * width) + x];
            }

            columnClues[x] = ClueCalculator.FromSolution(column);
        }

        RowClues = Array.AsReadOnly(rowClues);
        ColumnClues = Array.AsReadOnly(columnClues);

        // Computed once with the clues, so the layout and the renderer size their gutters and
        // buffers from the same answer instead of each re-deriving it - or, worse, one of them
        // reading it off a layout that was built for a different puzzle.
        MaxRowClueCount = MaxDisplayRuns(rowClues);
        MaxColumnClueCount = MaxDisplayRuns(columnClues);
    }

    private static int MaxDisplayRuns(LineClues[] clues)
    {
        // At least one: a blank line still displays a "0", so no line ever has fewer than one slot.
        var max = 1;

        foreach (var line in clues)
        {
            max = Math.Max(max, line.DisplayRuns.Count);
        }

        return max;
    }

    public string Id { get; }

    /// <summary>Pack this picture belongs to, e.g. <c>animals</c>.</summary>
    public string Pack { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Accent colour used when the finished picture is revealed.</summary>
    public string ColorHex { get; }

    /// <summary>True when this came from <see cref="Generation.IPuzzleGenerator"/> rather than authored content.</summary>
    public bool IsGenerated { get; }

    public int CellCount => Width * Height;

    /// <summary>How many cells the finished picture fills - the denominator of "how far along".</summary>
    public int PictureCellCount { get; }

    /// <summary>
    /// Most clue numbers any single row displays - what sizes the row gutter, and the buffer a
    /// renderer needs per row. Never less than one, since a blank line shows a "0".
    /// </summary>
    public int MaxRowClueCount { get; }

    /// <summary>The column counterpart of <see cref="MaxRowClueCount"/>.</summary>
    public int MaxColumnClueCount { get; }

    /// <summary>Clue for each row, top to bottom.</summary>
    public IReadOnlyList<LineClues> RowClues { get; }

    /// <summary>Clue for each column, left to right.</summary>
    public IReadOnlyList<LineClues> ColumnClues { get; }

    /// <summary>The finished picture, row-major. <c>true</c> means the cell is part of it.</summary>
    public ReadOnlySpan<bool> Solution => _solution;

    /// <summary>
    /// Builds a puzzle from '#'/'.' row strings - the format used by the authored content
    /// in <c>Content/puzzles.json</c> and by tests, because it is readable in a diff.
    /// </summary>
    public static Puzzle FromRows(
        string id,
        string pack,
        string colorHex,
        IReadOnlyList<string> rows,
        bool isGenerated = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(pack);
        ArgumentException.ThrowIfNullOrWhiteSpace(colorHex);
        ArgumentNullException.ThrowIfNull(rows);

        if (rows.Count == 0)
        {
            throw new ArgumentException("A puzzle needs at least one row.", nameof(rows));
        }

        var height = rows.Count;
        var width = rows[0].Length;

        if (width == 0)
        {
            throw new ArgumentException("A puzzle needs at least one column.", nameof(rows));
        }

        var solution = new bool[width * height];

        for (var y = 0; y < height; y++)
        {
            var row = rows[y];

            if (row.Length != width)
            {
                throw new ArgumentException(
                    $"Row {y} of puzzle '{id}' is {row.Length} cells wide but row 0 is {width}.",
                    nameof(rows));
            }

            for (var x = 0; x < width; x++)
            {
                solution[(y * width) + x] = row[x] switch
                {
                    '#' => true,
                    '.' => false,
                    _ => throw new ArgumentException(
                        $"Puzzle '{id}' row {y} contains '{row[x]}'; only '#' and '.' are allowed.",
                        nameof(rows)),
                };
            }
        }

        return new Puzzle(id, pack, width, height, colorHex, solution, isGenerated);
    }

    /// <summary>Builds a puzzle from a row-major solution grid, as the generator produces.</summary>
    public static Puzzle FromSolution(
        string id,
        string pack,
        string colorHex,
        int width,
        int height,
        ReadOnlySpan<bool> solution,
        bool isGenerated = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(pack);
        ArgumentException.ThrowIfNullOrWhiteSpace(colorHex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        if (solution.Length != width * height)
        {
            throw new ArgumentException(
                $"Solution has {solution.Length} cells but {width}x{height} needs {width * height}.",
                nameof(solution));
        }

        return new Puzzle(id, pack, width, height, colorHex, solution.ToArray(), isGenerated);
    }

    public bool IsFilled(int x, int y) => _solution[(y * Width) + x];

    /// <summary>Row-major index of a coordinate.</summary>
    public int IndexOf(int x, int y) => (y * Width) + x;

    /// <summary>The state a correctly played cell should end up in.</summary>
    public CellState ExpectedState(int index) => _solution[index] ? CellState.Filled : CellState.Crossed;
}
