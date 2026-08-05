using Squarebuzz.Core.Model;

namespace Squarebuzz.Presentation.Services;

/// <summary>
/// Reads the game's words aloud, for players who cannot yet read them.
/// </summary>
/// <remarks>
/// <para>
/// This is the "Voice narration" switch. It is not a screen reader: it speaks the text the game
/// chooses to speak, and it exists because the youngest players in the target age range can read
/// the clue <em>numbers</em> long before they can read "Auto-cross finished lines". Platform screen
/// readers remain the right answer for genuine accessibility, and are a separate job -
/// <c>SemanticProperties</c> on the pages rather than speech from here.
/// </para>
/// <para>
/// Speech uses the platform's engine, so what is available depends entirely on the device: a
/// phone with no voice installed for the chosen language cannot narrate it. That is reported
/// through <see cref="IsAvailable"/> rather than hidden, because a switch that silently does
/// nothing is worse than one that explains itself.
/// </para>
/// </remarks>
public interface INarrationService
{
    /// <summary>
    /// True when the device has a voice for the language most recently passed to
    /// <see cref="PrepareAsync"/>. False until it has been called.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Finds a voice for <paramref name="language"/>. Called at startup and whenever the
    /// player changes language, since the answer is different for each.
    /// </summary>
    Task PrepareAsync(AppLanguage language);

    /// <summary>Turns narration on or off. Stops anything in progress when switched off.</summary>
    void Configure(bool enabled);

    /// <summary>
    /// Speaks <paramref name="text"/>, cutting off whatever was being said.
    /// </summary>
    /// <remarks>
    /// Interrupting rather than queueing is deliberate: these are captions for what is on screen
    /// now, and a backlog of sentences describing screens the player has already left would be
    /// worse than saying nothing.
    /// </remarks>
    void Speak(string text);

    /// <summary>Stops immediately.</summary>
    void StopSpeaking();
}
