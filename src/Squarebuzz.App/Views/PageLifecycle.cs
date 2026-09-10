namespace Squarebuzz.App.Views;

/// <summary>
/// Disposes a pushed page's ViewModel when the page is popped off the navigation stack.
/// </summary>
/// <remarks>
/// <para>
/// Pushed view models are created outside DI disposal tracking by PageRegistration, so this
/// hook owns their cleanup. Disposing unsubscribes app-lifetime language/theme events and
/// stops the game's clock, allowing the page and view model to be collected together.
/// </para>
/// <para>
/// Being popped is detected as no longer being on the navigation stack, because
/// <see cref="Page.NavigatedFrom"/> also fires when the page is merely covered by a deeper push
/// (game to How-to-play), where the ViewModel must survive.
/// Parent removal also handles covered pages removed while clearing a stack.
/// </para>
/// <para>
/// For routed pages only. The three ShellContent roots - splash, onboarding, menu - are cached
/// by Shell for the app's lifetime, and their ViewModels are meant to stay alive.
/// </para>
/// </remarks>
internal static class PageLifecycle
{
    public static void DisposeViewModelWhenPopped(this ContentPage page)
    {
        var wasParented = page.Parent is not null;
        page.NavigatedFrom += OnNavigatedFrom;
        page.ParentChanged += OnParentChanged;

        void OnNavigatedFrom(object? sender, NavigatedFromEventArgs e)
        {
            if (!page.Navigation.NavigationStack.Contains(page))
            {
                DisposeViewModel();
            }
        }

        void OnParentChanged(object? sender, EventArgs e)
        {
            if (page.Parent is not null)
            {
                wasParented = true;
            }
            else if (wasParented)
            {
                DisposeViewModel();
            }
        }

        void DisposeViewModel()
        {
            // Both notifications can occur during one pop. Unhook before disposing so the
            // page's owner releases the view model once, even if disposal triggers more events.
            page.NavigatedFrom -= OnNavigatedFrom;
            page.ParentChanged -= OnParentChanged;
            (page.BindingContext as IDisposable)?.Dispose();
        }
    }
}
