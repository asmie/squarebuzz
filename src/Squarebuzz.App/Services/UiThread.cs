using Squarebuzz.Presentation.Services;

namespace Squarebuzz.App.Services;

/// <summary>The real UI thread: MAUI's <see cref="MainThread"/>.</summary>
public sealed class MauiUiThread : IUiThread
{
    public void BeginInvokeOnMainThread(Action action) => MainThread.BeginInvokeOnMainThread(action);
}
