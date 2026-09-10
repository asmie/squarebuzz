using Squarebuzz.Core.Abstractions;

namespace Squarebuzz.Core.Progression;

/// <inheritdoc cref="IScreenTimeMonitor"/>
public sealed class ScreenTimeMonitor : IScreenTimeMonitor
{
    private readonly object _gate = new();

    private TimeSpan _played;
    private int? _limitMinutes;
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

    /// <remarks>
    /// Read under the same lock that guards the write. It is set from the Options screen and read
    /// from the game's timer tick, which are the same thread today - but a nullable int is two
    /// fields wide, and the lock is what makes "today" not matter.
    /// </remarks>
    public int? LimitMinutes
    {
        get
        {
            lock (_gate)
            {
                return _limitMinutes;
            }
        }
    }

    public void Configure(int? limitMinutes)
    {
        lock (_gate)
        {
            // A limit of zero or less is meaningless and would fire on the first tick, so it is
            // treated as "off" rather than as an instant reminder.
            _limitMinutes = limitMinutes is > 0 ? limitMinutes : null;

            // Raising the limit past the time already played has to re-arm the reminder,
            // otherwise a parent who extends it gets no further warning at the new figure.
            if (_limitMinutes is { } minutes && _played < TimeSpan.FromMinutes(minutes))
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
            // Saturate before adding so even a very large duration reaches the reminder check.
            _played = elapsed >= TimeSpan.MaxValue - _played ? TimeSpan.MaxValue : _played + elapsed;

            if (_hasFired || _limitMinutes is not { } minutes)
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
