using Squarebuzz.Core.Abstractions;

namespace Squarebuzz.Core.Progression;

/// <inheritdoc cref="IScreenTimeMonitor"/>
public sealed class ScreenTimeMonitor : IScreenTimeMonitor
{
    private readonly object _gate = new();

    private TimeSpan _played;
    private bool _hasFired;

    public TimeSpan Played
    {
        get
        {
            lock (_gate)
            {
                return _played;
            }
        }
    }

    public int? LimitMinutes { get; private set; }

    public void Configure(int? limitMinutes)
    {
        lock (_gate)
        {
            // A limit of zero or less is meaningless and would fire on the first tick, so it is
            // treated as "off" rather than as an instant reminder.
            LimitMinutes = limitMinutes is > 0 ? limitMinutes : null;

            // Raising the limit past the time already played has to re-arm the reminder,
            // otherwise a parent who extends it gets no further warning at the new figure.
            if (LimitMinutes is { } minutes && _played < TimeSpan.FromMinutes(minutes))
            {
                _hasFired = false;
            }
        }
    }

    public bool Add(TimeSpan elapsed)
    {
        if (elapsed <= TimeSpan.Zero)
        {
            return false;
        }

        lock (_gate)
        {
            _played += elapsed;

            if (_hasFired || LimitMinutes is not { } minutes)
            {
                return false;
            }

            if (_played < TimeSpan.FromMinutes(minutes))
            {
                return false;
            }

            _hasFired = true;
            return true;
        }
    }

    public void Restart()
    {
        lock (_gate)
        {
            _played = TimeSpan.Zero;
            _hasFired = false;
        }
    }
}
