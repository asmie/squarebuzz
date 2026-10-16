using Squarebuzz.Core.Model;
using Squarebuzz.Presentation.Services;
using Xunit;

namespace Squarebuzz.Presentation.Tests;

public sealed class SpeechLocaleMatcherTests
{
    [Theory]
    [InlineData(AppLanguage.ChineseSimplified, "zh", "CN", true)]
    [InlineData(AppLanguage.ChineseSimplified, "zh", "SG", true)]
    [InlineData(AppLanguage.ChineseSimplified, "zh", "TW", false)]
    [InlineData(AppLanguage.ChineseTraditional, "zh", "TW", true)]
    [InlineData(AppLanguage.ChineseTraditional, "zh", "HK", true)]
    [InlineData(AppLanguage.ChineseTraditional, "zh", "MO", true)]
    [InlineData(AppLanguage.ChineseTraditional, "zh", "CN", false)]
    [InlineData(AppLanguage.ChineseSimplified, "zh-CN", null, true)]
    [InlineData(AppLanguage.ChineseTraditional, "zh-TW", null, true)]
    [InlineData(AppLanguage.ChineseSimplified, "zh-Hans-CN", null, true)]
    [InlineData(AppLanguage.ChineseTraditional, "zh-Hant-TW", null, true)]
    [InlineData(AppLanguage.ChineseTraditional, "zh_Hant", null, true)]
    [InlineData(AppLanguage.ChineseTraditional, "ZH_tw", null, true)]
    [InlineData(AppLanguage.ChineseTraditional, "zh-Hans", "TW", false)]
    [InlineData(AppLanguage.ChineseSimplified, "zh", null, true)]
    [InlineData(AppLanguage.ChineseTraditional, "zh", null, false)]
    [InlineData(AppLanguage.English, "en-US", null, true)]
    [InlineData(AppLanguage.English, "en_GB", null, true)]
    [InlineData(AppLanguage.Polish, "pl", "PL", true)]
    [InlineData(AppLanguage.Portuguese, "pt-BR", null, true)]
    [InlineData(AppLanguage.English, "pl", "PL", false)]
    [InlineData(AppLanguage.Finnish, "fil-PH", null, false)]
    [InlineData(AppLanguage.English, "", null, false)]
    [InlineData(AppLanguage.English, null, null, false)]
    public void PlatformVoice_MatchesOnlyTheRequestedLanguage(
        AppLanguage requested, string? language, string? country, bool expected) =>
        Assert.Equal(expected, SpeechLocaleMatcher.Matches(requested, language, country));
}
