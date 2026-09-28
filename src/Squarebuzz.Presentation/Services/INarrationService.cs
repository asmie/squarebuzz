using Squarebuzz.Core.Model;

namespace Squarebuzz.Presentation.Services;

/// <summary>Speaks selected game text through the device text-to-speech engine.</summary>
/// <remarks>
/// Narration is controlled by its own preference and does not replace screen-reader navigation.
/// IsAvailable reports whether the chosen language has an installed voice.
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

    /// <summary>Speaks the current text, interrupting any previous narration.</summary>
    void Speak(string text);

    /// <summary>Stops immediately.</summary>
    void StopSpeaking();
}
