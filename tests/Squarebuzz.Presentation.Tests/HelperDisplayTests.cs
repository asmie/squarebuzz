using Squarebuzz.Core.Model;
using Xunit;

namespace Squarebuzz.Presentation.Tests;

/// <summary>
/// Counters appear only while their helper is on. A mistake count with warnings off would be
/// the warning itself, and a hint count with hints off is only noise.
/// </summary>
public sealed class HelperDisplayTests : IDisposable
{
    private readonly GameViewModelHarness _h = new();

    public void Dispose() => _h.Dispose();

    private async Task StartAsync(HelperSettings helpers)
    {
        _h.Settings.Settings = GameSettings.Default with { Helpers = helpers };
        await _h.Vm.InitialiseAsync();
    }

    [Fact]
    public async Task WithEveryHelperOn_BothCountersShow()
    {
        await StartAsync(HelperSettings.Default);

        Assert.True(_h.Vm.ShowMistakes);
        Assert.True(_h.Vm.ShowHints);
        Assert.Contains("mistakes", _h.Vm.StatusText, StringComparison.Ordinal);
        Assert.Contains("hints", _h.Vm.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithWarningsOff_NoMistakeCountIsShown()
    {
        await StartAsync(HelperSettings.Default with { WarnOnMistakes = false });

        Assert.False(_h.Vm.ShowMistakes);
        Assert.DoesNotContain("mistakes", _h.Vm.StatusText, StringComparison.Ordinal);
        Assert.Contains("hints", _h.Vm.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithHintsOff_TheButtonIsDisabledAndNoHintCountIsShown()
    {
        await StartAsync(HelperSettings.Default with { AllowHints = false });

        Assert.False(_h.Vm.ShowHints);
        Assert.False(_h.Vm.CanUseHint);
        Assert.DoesNotContain("hints", _h.Vm.StatusText, StringComparison.Ordinal);
        Assert.Contains("mistakes", _h.Vm.StatusText, StringComparison.Ordinal);
    }
}
