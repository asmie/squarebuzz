using Squarebuzz.Core.Abstractions;
using Squarebuzz.Core.Model;

namespace Squarebuzz.Presentation.Services;

/// <summary>Accounts each active monotonic interval once, independently of dispatcher tick frequency.</summary>
public sealed class GameTimeTracker(IClock clock, IScreenTimeMonitor screenTime)
{
    private static readonly TimeSpan AutosaveEvery = TimeSpan.FromSeconds(15);
    private TimeSpan _lastAccountedAt;
    private TimeSpan _sinceSave;

    public bool IsSaveDue => _sinceSave >= AutosaveEvery;
    public TimeSpan Played => screenTime.Played;
    public void Rebase() => _lastAccountedAt = clock.Monotonic;
    public void Saved() => _sinceSave = TimeSpan.Zero;

    public bool Account(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var now = clock.Monotonic;
        var delta = now - _lastAccountedAt;
        if (delta <= TimeSpan.Zero)
        {
            return false;
        }

        _lastAccountedAt = now;
        var before = session.Elapsed;
        session.Advance(delta);
        // Dispatcher delay beyond a trial's deadline is not time spent playing.
        var played = session.Elapsed - before;
        _sinceSave = played >= TimeSpan.MaxValue - _sinceSave ? TimeSpan.MaxValue : _sinceSave + played;
        return screenTime.Add(played);
    }
}
