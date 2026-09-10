using Squarebuzz.Presentation.ViewModels;

namespace Squarebuzz.Presentation.Services;

/// <summary>
/// Connects window visibility and page navigation to the currently visible game.
/// Called on the UI thread, like the page and window events it coordinates.
/// </summary>
public sealed class GameLifecycle
{
    private readonly GameCompletionService _completions;
    private GameViewModel? _visibleGame;
    private bool _isForeground = true;

    public GameLifecycle(GameCompletionService completions)
    {
        ArgumentNullException.ThrowIfNull(completions);
        _completions = completions;
    }

    public void Show(GameViewModel game)
    {
        ArgumentNullException.ThrowIfNull(game);

        _visibleGame = game;
        game.RefreshAccessibilityState();

        if (_isForeground)
        {
            game.ResumeClock();
        }
        else
        {
            game.SuspendClock();
        }
    }

    public Task HideAsync(GameViewModel game)
    {
        ArgumentNullException.ThrowIfNull(game);

        // Navigation events can overlap: an old page leaving must not detach a new one.
        if (ReferenceEquals(_visibleGame, game))
        {
            _visibleGame = null;
        }

        game.SuspendClock();
        return game.AutosaveAsync();
    }

    public Task SuspendAsync()
    {
        _isForeground = false;
        _visibleGame?.SuspendClock();
        return _visibleGame?.AutosaveAsync() ?? Task.CompletedTask;
    }

    public void Resume()
    {
        if (_isForeground)
        {
            return;
        }

        _isForeground = true;
        _ = _completions.RetryAsync();
        _visibleGame?.RefreshAccessibilityState();
        _visibleGame?.ResumeClock();
    }
}
