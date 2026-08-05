namespace Squarebuzz.App.Views;

/// <summary>
/// Disposes a pushed page's ViewModel when the page is popped off the navigation stack.
/// </summary>
/// <remarks>
/// <para>
/// Nothing else ever disposes a transient ViewModel: the DI container does so only at process
/// exit, and no page did at all. Every screen ViewModel subscribes to the language-changed event
/// of an app-lifetime singleton (see <see cref="Squarebuzz.Presentation.ViewModels.LocalizedViewModel"/>), so each visit
/// to a screen used to leak the whole page through that handler list - and the game screen's
/// one-second clock kept ticking after the player had left with the back gesture, inflating the
/// saved time and the parental screen-time count.
/// </para>
/// <para>
/// Being popped is detected as no longer being on the navigation stack, because
/// <see cref="Page.NavigatedFrom"/> also fires when the page is merely covered by a deeper push
/// (game to How-to-play), where the ViewModel must survive.
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
        page.NavigatedFrom += (_, _) =>
        {
            if (!page.Navigation.NavigationStack.Contains(page))
            {
                (page.BindingContext as IDisposable)?.Dispose();
            }
        };
    }
}
