using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Squarebuzz.Presentation.Services;

namespace Squarebuzz.Presentation.ViewModels;

/// <summary>Shared multiplication prompt before parent actions.</summary>
/// <remarks>
/// This prevents accidental access; it is not an authentication or security boundary.
/// </remarks>
public sealed partial class ParentGate : ObservableObject
{
    private readonly ILocalizationService _strings;

    private Func<Task>? _onPassed;
    private int _expectedAnswer;

    public ParentGate(ILocalizationService strings)
    {
        ArgumentNullException.ThrowIfNull(strings);

        _strings = strings;
    }

    [ObservableProperty]
    public partial bool IsOpen { get; private set; }

    [ObservableProperty]
    public partial string Question { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Answer { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasFailed { get; private set; }

    public string Title => _strings.GetString("grownUps");

    public string RetryMessage => _strings.GetString("gateRetry");

    public string SubmitLabel => _strings.GetString("ok");

    public string CancelLabel => _strings.GetString("cancel");

    /// <summary>
    /// Poses a fresh question. <paramref name="onPassed"/> runs only on a correct answer.
    /// </summary>
    public void Open(Func<Task> onPassed)
    {
        ArgumentNullException.ThrowIfNull(onPassed);

        _onPassed = onPassed;

        // Both factors drawn independently, so the question cannot be predicted from the clock
        // or learned by rote. Kept in the 3-9 range: solvable by an adult at a glance, beyond a
        // child who has not met multiplication.
        var left = Random.Shared.Next(3, 10);
        var right = Random.Shared.Next(3, 10);

        _expectedAnswer = left * right;

        // Wrapped in an isolate (LRI ... PDI) so the sum reads left to right whatever the page's
        // direction. Digits are weakly directional and "×", "=" and "?" not at all, so in an
        // Arabic, Hebrew or Persian layout the bidi algorithm was free to lay this out as
        // "? = 4 × 7" - a different-looking question for exactly the parents least able to
        // tell a rendering quirk from a mistake.
        Question = FormattableString.Invariant($"⁦{left} × {right} = ?⁩");
        Answer = string.Empty;
        HasFailed = false;
        IsOpen = true;
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        if (!TryReadAnswer(Answer, out var answer) || answer != _expectedAnswer)
        {
            HasFailed = true;
            Answer = string.Empty;
            return;
        }

        IsOpen = false;

        var callback = _onPassed;
        _onPassed = null;

        if (callback is not null)
        {
            await callback();
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        IsOpen = false;
        HasFailed = false;
        Answer = string.Empty;
        _onPassed = null;
    }

    /// <summary>
    /// Reads the typed answer as a whole number, however the device's keyboard chose to write it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not the plain <c>int.TryParse(string)</c>, which parses under the current culture and so
    /// accepts thousands separators and a leading sign - "2,4" is a valid 24 in one culture and a
    /// failure in the next, which made the gate's strictness depend on the language setting. The
    /// settings repository is scrupulous about invariant parsing; this is the same rule applied to
    /// the one place a person types a number.
    /// </para>
    /// <para>
    /// Native digits are folded to ASCII first. A Persian or Bengali keyboard produces its own
    /// digits by default, and a parent typing the right answer in their own numerals should not be
    /// told to ask a grown-up.
    /// </para>
    /// <para>
    /// The value is accumulated digit by digit rather than copied into a buffer and parsed. The
    /// buffer used to be a <c>stackalloc</c> sized by whatever was typed, and neither gate entry
    /// limits its length - so pasting a long enough string overflowed the stack, a crash no
    /// handler can catch.
    /// </para>
    /// </remarks>
    private static bool TryReadAnswer(string? text, out int answer)
    {
        answer = 0;

        var digits = text.AsSpan().Trim();

        if (digits.IsEmpty)
        {
            return false;
        }

        var total = 0;

        foreach (var c in digits)
        {
            // GetNumericValue maps every Unicode decimal digit - ٤, ۴, ৪, ４ - to its value.
            var value = char.GetNumericValue(c);

            if (value is < 0 or > 9 || value != Math.Floor(value))
            {
                return false;
            }

            // Anything past int range is certainly not the answer to a times-table question.
            if (total > (int.MaxValue - (int)value) / 10)
            {
                return false;
            }

            total = (total * 10) + (int)value;
        }

        answer = total;
        return true;
    }

    /// <summary>Re-reads the localised labels after a language change.</summary>
    public void RefreshLabels()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(RetryMessage));
        OnPropertyChanged(nameof(SubmitLabel));
        OnPropertyChanged(nameof(CancelLabel));
    }
}
