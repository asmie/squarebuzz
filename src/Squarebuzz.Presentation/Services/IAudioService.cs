namespace Squarebuzz.Presentation.Services;

/// <summary>
/// The sounds the game can make.
/// </summary>
/// <remarks>
/// There is no <c>Mistake</c> member, and that is deliberate. The design doc's motion spec says of
/// the mistake shake: "No sound sting by default." The shake and the warn tint already tell the
/// player; adding a noise on top would make a wrong guess feel like a telling-off.
/// </remarks>
public enum GameSound
{
    /// <summary>A cell was filled in. By far the most frequently heard sound in the game.</summary>
    Fill,

    /// <summary>A cell was crossed out.</summary>
    Cross,

    /// <summary>A mark was cleared.</summary>
    Erase,

    /// <summary>A row or column was completed.</summary>
    LineComplete,

    /// <summary>A hint revealed a cell.</summary>
    Hint,

    /// <summary>The picture is finished.</summary>
    Win,
}

/// <summary>
/// Plays the game's sound effects and background loop.
/// </summary>
/// <remarks>
/// Every method is safe to call whether or not audio is available, wanted, or loaded. A device with
/// no audio output, a failed asset read and a player who has turned sound off all reach the same
/// place: nothing happens. Sound is a garnish, so no failure here may ever interrupt play.
/// </remarks>
public interface IAudioService
{
    /// <summary>True when the assets loaded and playback is possible.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Loads the sounds. Called once during startup so the first tap on the board is not the
    /// thing that pays for reading seven files off disk.
    /// </summary>
    Task PrimeAsync();

    /// <summary>
    /// Applies the player's audio preferences, starting or stopping the background loop to match.
    /// Called when settings load and whenever one of the switches changes.
    /// </summary>
    void Configure(bool soundEffects, bool music);

    /// <summary>Plays <paramref name="sound"/>, if effects are switched on.</summary>
    void Play(GameSound sound);

    /// <summary>Silences the background loop while the app is not in the foreground.</summary>
    void SuspendMusic();

    /// <summary>Restarts the background loop after <see cref="SuspendMusic"/>, if music is switched on.</summary>
    void ResumeMusic();
}
