using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Squarebuzz.App.Services;

namespace Squarebuzz.App.ViewModels;

/// <summary>
/// A small multiplication question in front of anything meant for a grown-up.
/// </summary>
/// <remarks>
/// <para>
/// A speed bump, not security, and honest about being one: it exists so a six-year-old does not
/// wander into "erase everything" or an external link, not to withstand a determined ten-year-old.
/// </para>
/// <para>
/// Shared by Options and About rather than reimplemented in each. Two independent copies of a
/// guard like this drift apart, and the weaker one becomes the way in.
/// </para>
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
        Question = $"{left} × {right} = ?";
        Answer = string.Empty;
        HasFailed = false;
        IsOpen = true;
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        if (!int.TryParse(Answer, out var answer) || answer != _expectedAnswer)
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

    /// <summary>Re-reads the localised labels after a language change.</summary>
    public void RefreshLabels()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(RetryMessage));
        OnPropertyChanged(nameof(CancelLabel));
    }
}
