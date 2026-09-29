using CommunityToolkit.Mvvm.ComponentModel;

namespace Squarebuzz.Presentation.ViewModels;

/// <summary>
/// Base for every screen ViewModel. <see cref="ObservableObject"/> supplies change
/// notification; <see cref="IsBusy"/> is here because several screens load puzzles or
/// database rows asynchronously and need to gate input while they do.
/// </summary>
public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>
    /// Called by the page's <c>OnAppearing</c>. Override for work that must happen every
    /// time a screen is shown rather than once at construction.
    /// </summary>
    public virtual Task OnAppearingAsync() => Task.CompletedTask;
}
