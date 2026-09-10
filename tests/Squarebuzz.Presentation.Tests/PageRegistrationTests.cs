using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Squarebuzz.App.Services;
using Squarebuzz.Core.Model;
using Squarebuzz.Presentation.Services;
using Squarebuzz.Presentation.Tests.Fakes;
using Squarebuzz.Presentation.ViewModels;
using Xunit;

namespace Squarebuzz.Presentation.Tests;

public sealed class PageRegistrationTests
{
    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILocalizationService, FakeLocalizationService>();
        services.AddSingleton<LifetimeCounters>();
        return services;
    }

    [Fact]
    public void DisposedPagesAndViewModels_AreCollectedWhileRootContainerStaysAlive()
    {
        var services = Services();
        services.AddPageWithViewModel<ProbePage, ProbeViewModel>();
        using var provider = services.BuildServiceProvider();
        var references = Enumerable.Range(0, 30).SelectMany(_ => OpenAndClose(provider)).ToArray();

        Collect();

        Assert.All(references, reference => Assert.False(reference.IsAlive));
        Assert.Equal(30, provider.GetRequiredService<LifetimeCounters>().DisposedViewModels);
        Assert.False(provider.GetRequiredService<LifetimeCounters>().IsDisposed);
        Assert.Null(provider.GetService<ProbeViewModel>());
        GC.KeepAlive(provider);
    }

    [Fact]
    public void DisposableTransientControl_RemainsRetainedEvenAfterManualDisposal()
    {
        // Demonstrates that the GC test detects the original root-DI retention, rather than
        // passing simply because Dispose removed the language event subscription.
        var services = Services();
        services.AddTransient<ProbeViewModel>();
        services.AddTransient<ProbePage>();
        using var provider = services.BuildServiceProvider();
        var references = OpenAndClose(provider);

        Collect();

        Assert.All(references, reference => Assert.True(reference.IsAlive));
        GC.KeepAlive(provider);
    }

    [Fact]
    public void PagesHaveIndependentViewModels_AndShareAppServices()
    {
        var services = Services();
        services.AddPageWithViewModel<ProbePage, ProbeViewModel>();
        using var provider = services.BuildServiceProvider();
        var covered = provider.GetRequiredService<ProbePage>();
        var top = provider.GetRequiredService<ProbePage>();
        Assert.NotSame(covered.ViewModel, top.ViewModel);
        Assert.Same(covered.Counters, top.Counters);

        // Covering a page leaves it alive. Only the page removed from the stack is disposed.
        top.ViewModel.Dispose();
        provider.GetRequiredService<ILocalizationService>().SetLanguage(AppLanguage.Polish);
        Assert.Equal(1, covered.LanguageChanges);
        Assert.Equal(0, top.LanguageChanges);
        Assert.False(covered.Counters.IsDisposed);
        covered.ViewModel.Dispose();
    }

    [Fact]
    public void FailedPageConstruction_DisposesItsViewModel()
    {
        var services = Services();
        services.AddPageWithViewModel<FailingPage, ProbeViewModel>();
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(Record.Exception(() => provider.GetRequiredService<FailingPage>()));
        Assert.Equal(1, provider.GetRequiredService<LifetimeCounters>().DisposedViewModels);
        Assert.False(provider.GetRequiredService<LifetimeCounters>().IsDisposed);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] OpenAndClose(IServiceProvider provider)
    {
        var page = provider.GetRequiredService<ProbePage>();
        var references = new[] { new WeakReference(page), new WeakReference(page.ViewModel) };
        page.ViewModel.Dispose();
        return references;
    }

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    public sealed class LifetimeCounters : IDisposable
    {
        public int DisposedViewModels { get; set; }
        public bool IsDisposed { get; private set; }
        public void Dispose() => IsDisposed = true;
    }

    public sealed class ProbeViewModel(ILocalizationService strings, LifetimeCounters counters)
        : LocalizedViewModel(strings)
    {
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                counters.DisposedViewModels++;
            }
            base.Dispose(disposing);
        }
    }

    public sealed class ProbePage
    {
        public ProbePage(ProbeViewModel viewModel, LifetimeCounters counters)
        {
            ViewModel = viewModel;
            Counters = counters;
            // Like GamePage, the page is retained by a view-model event as well as owning it.
            viewModel.PropertyChanged += OnPropertyChanged;
        }

        public ProbeViewModel ViewModel { get; }
        public LifetimeCounters Counters { get; }
        public int LanguageChanges { get; private set; }
        private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e) => LanguageChanges++;
    }

    public sealed class FailingPage
    {
        public FailingPage(ProbeViewModel viewModel)
        {
            ArgumentNullException.ThrowIfNull(viewModel);
            throw new InvalidOperationException("Page construction failed");
        }
    }
}
