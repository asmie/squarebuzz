using System.Globalization;
using Squarebuzz.Core.Model;
using Squarebuzz.Presentation.Navigation;
using Squarebuzz.Presentation.Services;
using Squarebuzz.Presentation.Tests.Fakes;
using Squarebuzz.Presentation.ViewModels;
using Xunit;

namespace Squarebuzz.Presentation.Tests;

public sealed class AboutViewModelTests : IDisposable
{
    private readonly FakeLocalizationService _strings = new();
    private readonly FakeNavigationService _navigation = new();
    private readonly FakeUiThread _uiThread = new();
    private readonly FakeLinkOpener _links = new();
    private readonly AboutViewModel _vm;

    public AboutViewModelTests()
    {
        _vm = new AboutViewModel(_strings, _navigation, _uiThread, new FakeAppVersion(), _links);
    }

    public void Dispose() => _vm.Dispose();

    private async Task PassGateAsync()
    {
        // The question is wrapped in a bidi isolate (LRI ... PDI) so it reads left to right on an
        // RTL page; strip that before reading the factors.
        var parts = _vm.Gate.Question.Trim('⁦', '⁩').Split('×');
        var left = int.Parse(parts[0].Trim(), CultureInfo.InvariantCulture);
        var right = int.Parse(parts[1].Replace("= ?", "", StringComparison.Ordinal).Trim(), CultureInfo.InvariantCulture);

        _vm.Gate.Answer = (left * right).ToString(CultureInfo.InvariantCulture);
        await _vm.Gate.SubmitCommand.ExecuteAsync(null);
    }

    [Fact]
    public void ExternalLinks_SitBehindTheParentGate()
    {
        _vm.RateCommand.Execute(null);

        Assert.True(_vm.Gate.IsOpen);
        Assert.False(_vm.HasNotice);
        Assert.Equal(0, _links.StoreOpenCount);
    }

    [Fact]
    public async Task PassingTheGate_OpensTheStoreOnce()
    {
        _vm.RateCommand.Execute(null);
        await PassGateAsync();

        Assert.Equal(1, _links.StoreOpenCount);
        Assert.False(_vm.Gate.IsOpen);
        Assert.False(_vm.HasNotice);
        Assert.Empty(_links.Opened);
    }

    [Fact]
    public async Task Rating_WithNothingToOpenIt_ShowsTheStoreAddress()
    {
        _links.StorePage = ExternalLinks.MicrosoftStoreListing;
        _links.Succeeds = false;
        _vm.RateCommand.Execute(null);

        await PassGateAsync();

        Assert.Equal(ExternalLinks.MicrosoftStoreListing.ToString(), _vm.Notice);
        Assert.Equal(1, _links.StoreOpenCount);
    }

    [Fact]
    public async Task Rating_WrongAnswerOrCancellation_DoesNotOpenTheStore()
    {
        _vm.RateCommand.Execute(null);
        _vm.Gate.Answer = "0";
        await _vm.Gate.SubmitCommand.ExecuteAsync(null);

        Assert.True(_vm.Gate.IsOpen);
        Assert.Equal(0, _links.StoreOpenCount);

        _vm.Gate.CancelCommand.Execute(null);
        await PassGateAsync();

        Assert.Equal(0, _links.StoreOpenCount);
    }

    [Fact]
    public void Rating_WithoutConfiguredStore_IsHiddenAndDoesNothing()
    {
        _links.StorePage = null;

        Assert.False(_vm.CanRate);
        _vm.RateCommand.Execute(null);

        Assert.False(_vm.Gate.IsOpen);
        Assert.Equal(0, _links.StoreOpenCount);
    }

    [Fact]
    public async Task Privacy_OpensThePolicyOnlyAfterTheGate()
    {
        _vm.PrivacyCommand.Execute(null);
        Assert.Empty(_links.Opened);

        await PassGateAsync();

        Assert.Equal([ExternalLinks.PrivacyPolicy], _links.Opened);
        Assert.False(_vm.HasNotice);
    }

    [Fact]
    // A device with no browser still gets the address, so a parent can read it elsewhere.
    public async Task Privacy_WithNothingToOpenIt_ShowsTheAddress()
    {
        _links.Succeeds = false;

        _vm.PrivacyCommand.Execute(null);
        await PassGateAsync();

        Assert.Equal(ExternalLinks.PrivacyPolicy.ToString(), _vm.Notice);
    }

    [Fact]
    // The numbers come from the installed package, not from a string typed into every language.
    public void Version_IsTheInstalledOne()
    {
        Assert.Equal("version:2.3,45", _vm.VersionLabel);
    }

    [Fact]
    public async Task LanguageChange_ClearsTheNotice()
    {
        _links.Succeeds = false;
        _vm.RateCommand.Execute(null);
        await PassGateAsync();
        Assert.True(_vm.HasNotice);

        _strings.SetLanguage(AppLanguage.Polish);

        Assert.Equal(string.Empty, _vm.Notice);
        Assert.False(_vm.HasNotice);
    }

    [Fact]
    // The value was right but nobody was told: the binding kept showing an empty notice pill.
    public async Task LanguageChange_TellsTheViewTheNoticeIsGone()
    {
        _links.Succeeds = false;
        _vm.RateCommand.Execute(null);
        await PassGateAsync();
        var raised = new List<string?>();
        _vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        _strings.SetLanguage(AppLanguage.Polish);

        Assert.Contains(nameof(_vm.HasNotice), raised);
    }

    [Fact]
    public async Task HowTo_NavigatesToTheLessons()
    {
        await _vm.HowToCommand.ExecuteAsync(null);

        var request = Assert.Single(_navigation.Requests);
        Assert.Equal(Routes.HowTo, request.Route);
    }
}
