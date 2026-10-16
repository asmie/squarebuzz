using System.Globalization;
using Squarebuzz.App.Services;
using Squarebuzz.Core.Model;
using Xunit;

namespace Squarebuzz.Presentation.Tests;

[CollectionDefinition("Process culture", DisableParallelization = true)]
public sealed class ProcessCultureDefinition;

[Collection("Process culture")]
public sealed class LocalizationCultureTests
{
    [Fact]
    public void InitialEnglishSelectionAppliesCultureWithoutSpuriousLanguageEvent()
    {
        var current = CultureInfo.CurrentCulture;
        var ui = CultureInfo.CurrentUICulture;
        var defaultCurrent = CultureInfo.DefaultThreadCurrentCulture;
        var defaultUi = CultureInfo.DefaultThreadCurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("pl-PL");
            var service = new LocalizationService();
            var events = 0;
            service.LanguageChanged += (_, _) => events++;
            service.SetLanguage(AppLanguage.English);
            Assert.Equal("en", CultureInfo.CurrentCulture.Name);
            Assert.Equal("en", CultureInfo.CurrentUICulture.Name);
            Assert.Equal("en", CultureInfo.DefaultThreadCurrentCulture!.Name);
            Assert.Equal("en", CultureInfo.DefaultThreadCurrentUICulture!.Name);
            Assert.Equal(0, events);
        }
        finally
        {
            CultureInfo.DefaultThreadCurrentCulture = defaultCurrent;
            CultureInfo.DefaultThreadCurrentUICulture = defaultUi;
            CultureInfo.CurrentCulture = current;
            CultureInfo.CurrentUICulture = ui;
        }
    }
}
