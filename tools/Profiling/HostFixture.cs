using System.Xml.Linq;
using Squarebuzz.App.Services;
using Squarebuzz.Core.Content;
using Squarebuzz.Core.Generation;
using Squarebuzz.Core.Model;
using Squarebuzz.Presentation.Services;
using Squarebuzz.Presentation.Tests.Fakes;
using Squarebuzz.Presentation.ViewModels;

namespace Squarebuzz.Profiling;

internal sealed class HostFixture : IDisposable
{
    private readonly Application _app = new();
    private readonly FakeSaveGameRepository _saves = new();
    private readonly FakeClock _clock = new();
    private readonly LocalizationServiceAdapter _strings = new();

    public HostFixture()
    {
        if (!ReferenceEquals(Application.Current, _app)) throw new InvalidOperationException("No host Application resources.");
        AddColors("src/Squarebuzz.App/Resources/Themes/Light.xaml");
        AddColors("src/Squarebuzz.App/Resources/Themes/Accents/Tangerine.xaml");
        if (!_app.Resources.TryGetValue("CellFill", out _)) throw new InvalidOperationException("Missing palette colors.");
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("en");
        if (_strings.GetString("a11yCell") == "a11yCell") throw new InvalidOperationException("Missing real localization resources.");
        var puzzles = new EmbeddedPuzzleRepository();
        Factory = new GameSessionFactory(puzzles, Generator);
        var progress = new FakeProgressRepository();
        var navigation = new FakeNavigationService();
        var diagnostics = new FakePersistenceDiagnostics();
        Game = new GameViewModel(Factory, new FakeSettingsRepository(), progress,
            new GameSaveService(_saves, _clock, diagnostics), puzzles, _strings, navigation, _clock,
            new GameTimeTracker(_clock, new FakeScreenTimeMonitor()), new FakeAudioService(),
            new FakeNarrationService(), new FakeAccessibilityState { IsScreenReaderActive = true },
            new FakeThemeService(), new FakeUiThread(), new FakeGameTimerFactory(), new FakeScreenReader(),
            new GameCompletionService(new FakeGameCompletionRepository(progress, _saves), progress, diagnostics), diagnostics);
        Continue = new ContinueViewModel(_strings, _saves, Factory, navigation, _clock, diagnostics);
    }
    public UniqueSolutionGenerator Generator { get; } = new(new BlobPuzzleGenerator());
    public GameSessionFactory Factory { get; }
    public GameViewModel Game { get; }
    public ContinueViewModel Continue { get; }

    public void StartAccessibleGame()
    {
        CompleteSynchronously(Game.StartAsync(NewGameOptions.Default with { Size = 25, ForceGenerated = true, Seed = 42 }));
        if (Game.Session?.Puzzle.CellCount != 625) throw new InvalidOperationException("Expected a 25x25 game.");
    }
    public void PrepareSaves(int count)
    {
        _saves.Saves.Clear();
        for (var seed = 0; seed < count; seed++)
        {
            var session = Factory.Create(NewGameOptions.Default with { Size = 25, Difficulty = 3, ForceGenerated = true, Seed = seed });
            var save = SavedGame.FromSession(session, Guid.NewGuid(), _clock.Now);
            _saves.Saves.Add(save.Id, save);
        }
    }
    public static void CompleteSynchronously(Task task)
    {
        if (!task.IsCompleted) throw new InvalidOperationException("Async work would invalidate current-thread allocation measurements.");
        task.GetAwaiter().GetResult();
    }
    private void AddColors(string path)
    {
        var dictionary = new ResourceDictionary();
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2009/xaml";
        foreach (var color in XDocument.Load(path).Root!.Elements().Where(e => e.Name.LocalName == "Color"))
            dictionary.Add((string)color.Attribute(xaml + "Key")!, Color.FromArgb(color.Value.Trim()));
        // Tangerine intentionally inherits every color from the theme and can be empty.
        _app.Resources.MergedDictionaries.Add(dictionary);
    }
    public void Dispose()
    {
        Continue.Dispose();
        Game.Dispose();
    }
}
