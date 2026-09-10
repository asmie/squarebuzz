using System.Globalization;
using Squarebuzz.Core.Abstractions;
using Squarebuzz.Core.Model;
using Squarebuzz.Data.Entities;

namespace Squarebuzz.Data.Repositories;

/// <summary>
/// Stores Options as key/value rows.
/// </summary>
/// <remarks>
/// Enums are persisted by name, not ordinal, so inserting a value into an enum later cannot
/// silently reinterpret a saved setting. Anything missing, unparseable or undefined falls back to the
/// default, which is what makes the table tolerant of both older and newer builds.
/// </remarks>
public sealed class SqliteSettingsRepository : ISettingsRepository
{
    private readonly SquarebuzzDatabase _database;
    private readonly AppLanguage? _deviceLanguage;

    /// <param name="database">The shared connection.</param>
    /// <param name="deviceLanguage">
    /// The device's own language, for a player who has never chosen one. A value rather than a
    /// delegate on purpose: it has to be read before the app applies a saved language, so there is
    /// nothing to defer - see <see cref="SeedLanguageIfUnchosenAsync"/>. Null leaves the built-in
    /// default in place, which is what tests want.
    /// </param>
    public SqliteSettingsRepository(SquarebuzzDatabase database, AppLanguage? deviceLanguage = null)
    {
        ArgumentNullException.ThrowIfNull(database);

        _database = database;
        _deviceLanguage = deviceLanguage;
    }

    public async Task<GameSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connection = await _database.GetConnectionAsync().ConfigureAwait(false);
        var rows = await connection.Table<SettingEntity>().ToListAsync().ConfigureAwait(false);

        var values = rows.ToDictionary(r => r.Key, r => r.Value, StringComparer.Ordinal);
        var defaults = GameSettings.Default;

        var language = await SeedLanguageIfUnchosenAsync(connection, values, defaults.Language).ConfigureAwait(false);

        return new GameSettings
        {
            SoundEffects = ReadBool(values, Keys.SoundEffects, defaults.SoundEffects),
            Music = ReadBool(values, Keys.Music, defaults.Music),
            VoiceNarration = ReadBool(values, Keys.VoiceNarration, defaults.VoiceNarration),
            Haptics = ReadBool(values, Keys.Haptics, defaults.Haptics),

            Theme = ReadEnum(values, Keys.Theme, defaults.Theme),
            Accent = ReadEnum(values, Keys.Accent, defaults.Accent),
            FollowSystemTheme = ReadBool(values, Keys.FollowSystemTheme, defaults.FollowSystemTheme),
            BigNumbers = ReadBool(values, Keys.BigNumbers, defaults.BigNumbers),

            TapBehaviour = ReadEnum(values, Keys.TapBehaviour, defaults.TapBehaviour),
            Handedness = ReadEnum(values, Keys.Handedness, defaults.Handedness),
            CellZoomPercent = ReadInt(values, Keys.CellZoomPercent, defaults.CellZoomPercent),
            ShowMagnifier = ReadBool(values, Keys.ShowMagnifier, defaults.ShowMagnifier),

            Language = language,

            Helpers = new HelperSettings
            {
                AutoCross = ReadBool(values, Keys.HelperAutoCross, defaults.Helpers.AutoCross),
                WarnOnMistakes = ReadBool(values, Keys.HelperWarnOnMistakes, defaults.Helpers.WarnOnMistakes),
                ShowTimer = ReadBool(values, Keys.HelperShowTimer, defaults.Helpers.ShowTimer),
                AllowHints = ReadBool(values, Keys.HelperAllowHints, defaults.Helpers.AllowHints),
            },

            LastSize = ReadInt(values, Keys.LastSize, defaults.LastSize),
            LastDifficulty = ReadInt(values, Keys.LastDifficulty, defaults.LastDifficulty),
            LastPackId = ReadString(values, Keys.LastPackId, defaults.LastPackId),
            LastChallenge = ReadEnum(values, Keys.LastChallenge, defaults.LastChallenge),

            ScreenTimeLimitMinutes = ReadNullableInt(values, Keys.ScreenTimeLimitMinutes),
            HasSeenOnboarding = ReadBool(values, Keys.HasSeenOnboarding, defaults.HasSeenOnboarding),
        }.Sanitised();
    }

    /// <summary>
    /// The language for a player who has never chosen one: the device's, when the app supplied a
    /// way to read it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The absence of the row is the only reliable "never chosen" signal - a stored
    /// <see cref="AppLanguage.English"/> is a choice and must not be second-guessed - which is why
    /// this lives here rather than in startup code that can only see the loaded value.
    /// </para>
    /// <para>
    /// The seed is written, not just returned, so it is a choice from then on: without the write,
    /// a player who never opens Options would follow their phone's language for ever, which is a
    /// different feature from the one this is.
    /// </para>
    /// </remarks>
    private async Task<AppLanguage> SeedLanguageIfUnchosenAsync(
        SQLite.SQLiteAsyncConnection connection,
        Dictionary<string, string?> values,
        AppLanguage fallback)
    {
        if (values.ContainsKey(Keys.Language) || _deviceLanguage is not { } seeded)
        {
            return ReadEnum(values, Keys.Language, fallback);
        }

        await connection.InsertOrReplaceAsync(Row(Keys.Language, seeded)).ConfigureAwait(false);

        return seeded;
    }

    public async Task SaveAsync(GameSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        var connection = await _database.GetConnectionAsync().ConfigureAwait(false);

        var rows = new List<SettingEntity>
        {
            Row(Keys.SoundEffects, settings.SoundEffects),
            Row(Keys.Music, settings.Music),
            Row(Keys.VoiceNarration, settings.VoiceNarration),
            Row(Keys.Haptics, settings.Haptics),

            Row(Keys.Theme, settings.Theme),
            Row(Keys.Accent, settings.Accent),
            Row(Keys.FollowSystemTheme, settings.FollowSystemTheme),
            Row(Keys.BigNumbers, settings.BigNumbers),

            Row(Keys.TapBehaviour, settings.TapBehaviour),
            Row(Keys.Handedness, settings.Handedness),
            Row(Keys.CellZoomPercent, settings.CellZoomPercent),
            Row(Keys.ShowMagnifier, settings.ShowMagnifier),

            Row(Keys.Language, settings.Language),

            Row(Keys.HelperAutoCross, settings.Helpers.AutoCross),
            Row(Keys.HelperWarnOnMistakes, settings.Helpers.WarnOnMistakes),
            Row(Keys.HelperShowTimer, settings.Helpers.ShowTimer),
            Row(Keys.HelperAllowHints, settings.Helpers.AllowHints),

            Row(Keys.LastSize, settings.LastSize),
            Row(Keys.LastDifficulty, settings.LastDifficulty),
            new() { Key = Keys.LastPackId, Value = settings.LastPackId },
            Row(Keys.LastChallenge, settings.LastChallenge),

            new()
            {
                Key = Keys.ScreenTimeLimitMinutes,
                Value = settings.ScreenTimeLimitMinutes?.ToString(CultureInfo.InvariantCulture),
            },

            Row(Keys.HasSeenOnboarding, settings.HasSeenOnboarding),
        };

        // One transaction: a half-written settings table would leave the UI in a state the
        // player never chose.
        await connection.RunInTransactionAsync(transaction =>
        {
            foreach (var row in rows)
            {
                transaction.InsertOrReplace(row);
            }
        }).ConfigureAwait(false);
    }

    private static SettingEntity Row(string key, bool value) =>
        new() { Key = key, Value = value ? "1" : "0" };

    private static SettingEntity Row(string key, int value) =>
        new() { Key = key, Value = value.ToString(CultureInfo.InvariantCulture) };

    private static SettingEntity Row<TEnum>(string key, TEnum value)
        where TEnum : struct, Enum =>
        new() { Key = key, Value = value.ToString() };

    private static string ReadString(Dictionary<string, string?> values, string key, string fallback) =>
        values.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw) ? raw : fallback;

    private static bool ReadBool(Dictionary<string, string?> values, string key, bool fallback) =>
        values.GetValueOrDefault(key) switch
        {
            "1" => true,
            "0" => false,
            _ => fallback,
        };

    private static int ReadInt(Dictionary<string, string?> values, string key, int fallback) =>
        values.TryGetValue(key, out var raw)
        && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private static int? ReadNullableInt(Dictionary<string, string?> values, string key) =>
        values.TryGetValue(key, out var raw)
        && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static TEnum ReadEnum<TEnum>(Dictionary<string, string?> values, string key, TEnum fallback)
        where TEnum : struct, Enum =>
        values.TryGetValue(key, out var raw) && Enum.TryParse<TEnum>(raw, ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed)
            ? parsed
            : fallback;

    /// <summary>
    /// Setting keys. These are persisted strings - renaming one silently resets that setting
    /// for every existing player, so treat them as a wire format.
    /// </summary>
    private static class Keys
    {
        public const string SoundEffects = "audio.soundEffects";
        public const string Music = "audio.music";
        public const string VoiceNarration = "audio.voice";
        public const string Haptics = "audio.haptics";

        public const string Theme = "look.theme";
        public const string Accent = "look.accent";
        public const string FollowSystemTheme = "look.followSystemTheme";
        public const string BigNumbers = "look.bigNumbers";

        public const string TapBehaviour = "controls.tapBehaviour";
        public const string Handedness = "controls.handedness";
        public const string CellZoomPercent = "controls.cellZoomPercent";
        public const string ShowMagnifier = "controls.showMagnifier";

        public const string Language = "language";

        public const string HelperAutoCross = "helpers.autoCross";
        public const string HelperWarnOnMistakes = "helpers.warnOnMistakes";
        public const string HelperShowTimer = "helpers.showTimer";
        public const string HelperAllowHints = "helpers.allowHints";

        public const string LastSize = "lastGame.size";
        public const string LastDifficulty = "lastGame.difficulty";
        public const string LastPackId = "lastGame.packId";
        public const string LastChallenge = "lastGame.challenge";

        public const string ScreenTimeLimitMinutes = "parent.screenTimeLimitMinutes";

        public const string HasSeenOnboarding = "firstRun.hasSeenOnboarding";
    }
}
