namespace Squarebuzz.Presentation.Services;

/// <summary>
/// A repeating timer, as the game clock consumes one: subscribe, start, stop.
/// </summary>
/// <remarks>
/// The seam that keeps <c>IDispatcherTimer</c> and <c>Application.Current</c> out of the
/// ViewModels. The MAUI head wraps a dispatcher timer, so ticks arrive on the UI thread; a
/// test fake raises <see cref="Tick"/> by hand to advance the clock deterministically.
/// </remarks>
public interface IGameTimer
{
    event EventHandler? Tick;

    bool IsRunning { get; }

    void Start();

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Naming",
        "CA1716:Identifiers should not match keywords",
        Justification = "Start/Stop is the natural pair for a timer, and no VB caller exists.")]
    void Stop();
}

/// <summary>Creates the game's timers, so a ViewModel never touches the dispatcher itself.</summary>
public interface IGameTimerFactory
{
    /// <summary>A repeating one-second timer - the game clock's beat.</summary>
    IGameTimer CreateSecondTimer();
}
