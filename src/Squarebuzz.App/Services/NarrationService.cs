using Squarebuzz.Core.Model;

using Squarebuzz.Presentation.Services;

namespace Squarebuzz.App.Services;

/// <inheritdoc />
public sealed class NarrationService : INarrationService, IDisposable
{
    /// <summary>
    /// Words are read a little slower than the platform default. These are children, and the
    /// default rate on most devices is pitched at adults skimming notifications.
    /// </summary>
    private const float Pitch = 1.05f;

    private readonly Lock _gate = new();

    private Locale? _locale;
    private CancellationTokenSource? _speaking;
    private bool _isEnabled;
    private bool _isDisposed;
    private int _prepareGeneration;

    public bool IsAvailable { get; private set; }

    public async Task PrepareAsync(AppLanguage language)
    {
        // Callers fire this without awaiting, and enumerating voices is slow - so a player
        // flicking through the language list can have several of these in flight. Only the
        // newest may commit its answer, or an older enumeration finishing last would quietly
        // install the previous language's voice.
        var generation = Interlocked.Increment(ref _prepareGeneration);

        var wanted = language.ToCultureCode();
        Locale? found;

        try
        {
            var locales = await TextToSpeech.Default.GetLocalesAsync().ConfigureAwait(false);

            // Match on the language part only. Devices report "en-GB", "en-US", "en_US" and
            // occasionally just "en", and any English voice can read English text.
            //
            // Deliberately not falling back to some other language's voice. An English engine
            // reading Polish is not a degraded version of narration, it is noise - and a child
            // who cannot read the words cannot work out that the voice is wrong either.
            found = locales.FirstOrDefault(l =>
                l.Language.StartsWith(wanted, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception)
        {
            // No engine at all: some Android images ship without one, and desktop support varies.
            found = null;
        }

        if (generation != Volatile.Read(ref _prepareGeneration))
        {
            // A newer PrepareAsync started while this one was enumerating; its answer wins.
            return;
        }

        _locale = found;
        IsAvailable = found is not null;

        if (!IsAvailable)
        {
            StopSpeaking();
        }
    }

    public void Configure(bool enabled)
    {
        _isEnabled = enabled;

        if (!enabled)
        {
            StopSpeaking();
        }
    }

    public void Speak(string text)
    {
        if (!_isEnabled || !IsAvailable || _isDisposed || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        CancellationToken token;

        lock (_gate)
        {
            // Cancel the previous utterance and hand this one a fresh token, so two Speak calls
            // in quick succession cannot both be speaking.
            Cancel();

            _speaking = new CancellationTokenSource();
            token = _speaking.Token;
        }

        // Not awaited: speaking a sentence takes seconds, and the caller is a property setter or
        // a move on the board. Failures are swallowed inside SpeakSafelyAsync.
        _ = SpeakSafelyAsync(text, token);
    }

    public void StopSpeaking()
    {
        lock (_gate)
        {
            Cancel();
        }
    }

    public void Dispose()
    {
        _isDisposed = true;
        StopSpeaking();
    }

    private async Task SpeakSafelyAsync(string text, CancellationToken token)
    {
        try
        {
            await TextToSpeech.Default
                .SpeakAsync(text, new SpeechOptions { Locale = _locale, Pitch = Pitch }, token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The expected way an utterance ends when the next one starts.
        }
        catch (Exception)
        {
            // A failed sentence must never surface: narration is an aid, not a feature the game
            // depends on, and the platform engine can fail transiently while it loads a voice.
        }
    }

    /// <summary>Cancels and disposes the current utterance. Callers must hold <see cref="_gate"/>.</summary>
    private void Cancel()
    {
        if (_speaking is not { } current)
        {
            return;
        }

        _speaking = null;

        try
        {
            current.Cancel();
            current.Dispose();
        }
        catch (Exception)
        {
            // Racing a completing utterance can throw on dispose; nothing to do about it.
        }
    }
}
