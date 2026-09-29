using System.Globalization;
using Squarebuzz.Core.Abstractions;
using Squarebuzz.Core.Model;
using Squarebuzz.Presentation.Services;

namespace Squarebuzz.Presentation.Tests.Fakes;

/// <summary>
/// Returns the key itself (and <c>key:arg,arg</c> from Format), so assertions are culture-free
/// and pin which resource a screen composed rather than any particular translation. Tests of
/// language changes can supply translations to distinguish freshly read text from cached text.
/// </summary>
public sealed class FakeLocalizationService : ILocalizationService
{
    public event EventHandler? LanguageChanged;

    public AppLanguage Language { get; private set; } = AppLanguage.English;

    public Dictionary<(AppLanguage Language, string Key), string> Translations { get; } = [];

    public string GetString(string key) => Translations.GetValueOrDefault((Language, key), key);

    public string Format(string key, params object[] arguments) =>
        Translations.TryGetValue((Language, key), out var format)
            ? string.Format(CultureInfo.InvariantCulture, format, arguments)
            : $"{key}:{string.Join(",", arguments)}";

    public void SetLanguage(AppLanguage language)
    {
        Language = language;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>Records every navigation instead of performing one.</summary>
public sealed class FakeNavigationService : INavigationService
{
    public sealed record Request(string Route, IDictionary<string, object>? Parameters, bool IsReset);

    public List<Request> Requests { get; } = [];

    public Request? Last => Requests.Count == 0 ? null : Requests[^1];

    public Task GoToAsync(string route)
    {
        Requests.Add(new Request(route, null, IsReset: false));
        return Task.CompletedTask;
    }

    public Task GoToAsync(string route, IDictionary<string, object> parameters)
    {
        Requests.Add(new Request(route, parameters, IsReset: false));
        return Task.CompletedTask;
    }

    public Task ResetToAsync(string route)
    {
        Requests.Add(new Request(route, null, IsReset: true));
        return Task.CompletedTask;
    }
}

/// <summary>A clock the test sets by hand.</summary>
public sealed class FakeClock : IClock
{
    public DateTimeOffset Now { get; set; } = new(2026, 8, 5, 10, 0, 0, TimeSpan.Zero);

    public DateOnly Today { get; set; } = new(2026, 8, 5);

    /// <summary>Starts non-zero so a test cannot pass by accident against an unset reading.</summary>
    public TimeSpan Monotonic { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Lets real time pass - the timer's tick alone advances nothing.</summary>
    public void Advance(TimeSpan by)
    {
        Monotonic += by;
        Now += by;
    }
}

/// <summary>Runs posted work inline - tests are single-threaded on purpose.</summary>
public sealed class FakeUiThread : IUiThread
{
    public int Invocations { get; private set; }

    public void BeginInvokeOnMainThread(Action action)
    {
        Invocations++;
        action();
    }
}

/// <summary>A timer the test ticks by hand.</summary>
public sealed class FakeGameTimer : IGameTimer
{
    public event EventHandler? Tick;

    public bool IsRunning { get; private set; }

    public void Start() => IsRunning = true;

    public void Stop() => IsRunning = false;

    /// <summary>One beat of the clock, as the dispatcher timer would deliver it.</summary>
    public void RaiseTick() => Tick?.Invoke(this, EventArgs.Empty);
}

public sealed class FakeGameTimerFactory : IGameTimerFactory
{
    public List<FakeGameTimer> Created { get; } = [];

    public FakeGameTimer? Latest => Created.Count == 0 ? null : Created[^1];

    public IGameTimer CreateSecondTimer()
    {
        var timer = new FakeGameTimer();
        Created.Add(timer);
        return timer;
    }
}

public sealed class FakeScreenReader : IScreenReader
{
    public List<string> Announcements { get; } = [];

    public void Announce(string text) => Announcements.Add(text);
}

public sealed class FakeAudioService : IAudioService
{
    public List<GameSound> Played { get; } = [];

    /// <summary>Makes <see cref="PrimeAsync"/> throw, as a missing or unplayable asset would.</summary>
    public bool PrimeFails { get; set; }

    public Task PrimeAsync() => PrimeFails
        ? Task.FromException(new InvalidOperationException("no audio device"))
        : Task.CompletedTask;

    public void Configure(bool soundEffects, bool music)
    {
    }

    public void Play(GameSound sound) => Played.Add(sound);

    public void SuspendMusic()
    {
    }

    public void ResumeMusic()
    {
    }
}

public sealed class FakeNarrationService : INarrationService
{
    public List<string> Spoken { get; } = [];

    public bool IsAvailable { get; set; } = true;

    public Task PrepareAsync(AppLanguage language) => Task.CompletedTask;

    public void Configure(bool enabled)
    {
    }

    public void Speak(string text) => Spoken.Add(text);

    /// <summary>How often speech was cut off - leaving a screen must do it exactly once.</summary>
    public int StopCalls { get; private set; }

    public void StopSpeaking() => StopCalls++;
}

public sealed class FakeAccessibilityState : IAccessibilityState
{
    public bool IsScreenReaderActive { get; set; }

    public event EventHandler? ScreenReaderStateChanged;

    public int RefreshCalls { get; private set; }

    public void Refresh()
    {
        RefreshCalls++;
    }

    public void RaiseChanged() => ScreenReaderStateChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class FakeThemeService : IThemeService
{
    public event EventHandler? Changed;

    public GameTheme Theme { get; private set; } = GameTheme.Light;

    public GameAccent Accent { get; private set; } = GameAccent.Tangerine;

    public bool FollowsSystem { get; private set; }

    public int ApplyCount { get; private set; }

    public void Apply(GameTheme theme, GameAccent accent, bool followSystem = false)
    {
        // Raised when something actually changes, as the real ThemeService does - it is how a
        // view knows to re-read the palette. The fake used to swallow it, which is why nothing
        // caught the Options chips keeping their old colours.
        var moved = theme != Theme || accent != Accent || followSystem != FollowsSystem;

        Theme = theme;
        Accent = accent;
        FollowsSystem = followSystem;
        ApplyCount++;

        if (moved)
        {
            RaiseChanged();
        }
    }

    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}

public sealed class FakeScreenTimeMonitor : IScreenTimeMonitor
{
    public TimeSpan Played { get; private set; }

    public int? LimitMinutes { get; private set; }

    /// <summary>Makes the next <see cref="Add"/> report the limit as newly reached.</summary>
    public bool RemindOnNextAdd { get; set; }

    public void Configure(int? limitMinutes) => LimitMinutes = limitMinutes;

    public bool Add(TimeSpan elapsed)
    {
        Played += elapsed;

        if (!RemindOnNextAdd)
        {
            return false;
        }

        RemindOnNextAdd = false;
        return true;
    }

    public void Restart() => Played = TimeSpan.Zero;
}

public sealed class FakeDeviceScreen : IDeviceScreen
{
    public bool IsLargeScreen { get; set; }
}
