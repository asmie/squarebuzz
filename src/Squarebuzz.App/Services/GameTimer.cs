using Squarebuzz.Presentation.Services;

namespace Squarebuzz.App.Services;

/// <summary>Creates <see cref="IGameTimer"/>s backed by the application dispatcher.</summary>
public sealed class DispatcherGameTimerFactory : IGameTimerFactory
{
    public IGameTimer CreateSecondTimer() => new DispatcherGameTimer(TimeSpan.FromSeconds(1));
}

/// <summary>
/// An <see cref="IGameTimer"/> over MAUI's <see cref="IDispatcherTimer"/>, so ticks arrive on
/// the UI thread.
/// </summary>
/// <remarks>
/// When no dispatcher exists yet - the window is not up - the timer is a silent no-op rather
/// than a throw, matching the old in-ViewModel behaviour of skipping the clock entirely.
/// </remarks>
internal sealed class DispatcherGameTimer : IGameTimer
{
    private readonly IDispatcherTimer? _timer;

    public DispatcherGameTimer(TimeSpan interval)
    {
        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null)
        {
            return;
        }

        _timer = dispatcher.CreateTimer();
        _timer.Interval = interval;
        _timer.IsRepeating = true;
        _timer.Tick += (_, _) => Tick?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Tick;

    public bool IsRunning => _timer?.IsRunning ?? false;

    public void Start() => _timer?.Start();

    public void Stop() => _timer?.Stop();
}
