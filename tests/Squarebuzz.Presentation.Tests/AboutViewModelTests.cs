using System.Globalization;
using Squarebuzz.Core.Model;
using Squarebuzz.Presentation.Navigation;
using Squarebuzz.Presentation.Tests.Fakes;
using Squarebuzz.Presentation.ViewModels;
using Xunit;

namespace Squarebuzz.Presentation.Tests;

public sealed class AboutViewModelTests : IDisposable
{
    private readonly FakeLocalizationService _strings = new();
    private readonly FakeNavigationService _navigation = new();
    private readonly FakeUiThread _uiThread = new();
    private readonly AboutViewModel _vm;

    public AboutViewModelTests()
    {
        _vm = new AboutViewModel(_strings, _navigation, _uiThread);
    }

    public void Dispose() => _vm.Dispose();

    private async Task PassGateAsync()
    {
        var parts = _vm.Gate.Question.Split('×');
        var left = int.Parse(parts[0].Trim(), CultureInfo.InvariantCulture);
        var right = int.Parse(parts[1].Replace("= ?", "", StringComparison.Ordinal).Trim(), CultureInfo.InvariantCulture);

        _vm.Gate.Answer = (left * right).ToString(CultureInfo.InvariantCulture);
        await _vm.Gate.SubmitCommand.ExecuteAsync(null);
    }

    [Fact]
    public void ExternalLinks_SitBehindTheParentGate()
    {
        _vm.ContactCommand.Execute(null);

        Assert.True(_vm.Gate.IsOpen);
        Assert.False(_vm.HasNotice);
    }

    [Fact]
    public async Task PassingTheGate_ShowsTheUnlockNotice()
    {
        _vm.ContactCommand.Execute(null);
        await PassGateAsync();

        Assert.Equal("🔓 aboutContact", _vm.Notice);
        Assert.True(_vm.HasNotice);
    }

    [Fact]
    public async Task EachGatedLink_NamesItselfInTheNotice()
    {
        _vm.PrivacyCommand.Execute(null);
        await PassGateAsync();
        Assert.Equal("🔓 aboutPrivacy", _vm.Notice);

        _vm.RateCommand.Execute(null);
        await PassGateAsync();
        Assert.Equal("🔓 aboutRate", _vm.Notice);
    }

    [Fact]
    public void Credits_AreNotGated()
    {
        _vm.CreditsCommand.Execute(null);

        Assert.False(_vm.Gate.IsOpen);
        Assert.Equal("squarebuzz team", _vm.Notice);
    }

    [Fact]
    public void LanguageChange_ClearsTheNotice()
    {
        _vm.CreditsCommand.Execute(null);
        Assert.True(_vm.HasNotice);

        _strings.SetLanguage(AppLanguage.Polish);

        Assert.Equal(string.Empty, _vm.Notice);
        Assert.False(_vm.HasNotice);
    }

    [Fact]
    public async Task HowTo_NavigatesToTheLessons()
    {
        await _vm.HowToCommand.ExecuteAsync(null);

        var request = Assert.Single(_navigation.Requests);
        Assert.Equal(Routes.HowTo, request.Route);
    }
}
