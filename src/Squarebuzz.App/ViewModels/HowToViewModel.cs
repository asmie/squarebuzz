using System.Collections.ObjectModel;
using Squarebuzz.App.Services;

namespace Squarebuzz.App.ViewModels;

/// <summary>One numbered lesson, with its worked diagram.</summary>
public sealed class HowToSection
{
    public required string Number { get; init; }

    public required string Title { get; init; }

    public required string Body { get; init; }

    /// <summary>Diagram rows: '#' filled, 'x' crossed, 'o' highlighted, '.' untouched.</summary>
    public required IReadOnlyList<string> Rows { get; init; }

    public required IReadOnlyList<string> RowClues { get; init; }
}

/// <summary>
/// How to Play: five lessons, each with a worked example.
/// </summary>
/// <remarks>
/// The diagrams are the prototype's, character for character. They matter more than the prose -
/// the overlap lesson in particular is much easier to see than to read.
/// </remarks>
public partial class HowToViewModel : LocalizedViewModel
{
    public HowToViewModel(ILocalizationService strings)
        : base(strings)
    {
        Build();
    }

    public ObservableCollection<HowToSection> Sections { get; } = [];

    public string Heading => T("howToTitle");

    protected override void OnLanguageChangedCore() => Build();

    private void Build()
    {
        Sections.Clear();

        // Read the clues
        Sections.Add(new HowToSection
        {
            Number = "1",
            Title = T("ht1"),
            Body = T("ht1b"),
            Rows = ["##.#.", ".....", "....."],
            RowClues = ["2 1", string.Empty, string.Empty],
        });

        // Fill and cross
        Sections.Add(new HowToSection
        {
            Number = "2",
            Title = T("ht2"),
            Body = T("ht2b"),
            Rows = ["##x#x", "xx###", "#xx#x"],
            RowClues = ["2 1", "3", "1 1"],
        });

        // Overlap on long clues - the highlighted cells are the ones a run of 8 must cover
        // wherever it starts.
        Sections.Add(new HowToSection
        {
            Number = "3",
            Title = T("ht3"),
            Body = T("ht3b"),
            Rows = ["##oooooo##", "..oooooo.."],
            RowClues = ["8", "8"],
        });

        // Cross-reference
        Sections.Add(new HowToSection
        {
            Number = "4",
            Title = T("ht4"),
            Body = T("ht4b"),
            Rows = [".#o..", "x#o#x", ".#o.."],
            RowClues = ["1", "3", "1"],
        });

        // Cross out finished lines
        Sections.Add(new HowToSection
        {
            Number = "5",
            Title = T("ht5"),
            Body = T("ht5b"),
            Rows = ["x###x", "xx##x"],
            RowClues = ["3", "2"],
        });
    }
}
