# Language selection

Options uses a native picker bound to `OptionsViewModel.Languages` and `SelectedLanguage`.
Labels use each language's own name. Null selections during picker updates are ignored.

At startup, `MauiProgram` captures the device language before applying saved settings.
`SqliteSettingsRepository` uses it only when no language row exists, then stores the choice.
Later device-language changes do not replace the saved setting.

`AppLanguages.FromCultureCode` maps supported cultures to the language enum. Chinese script
subtags take precedence over region: `zh-CN` selects Simplified Chinese; `zh-TW`, `zh-HK`
and `zh-MO` select Traditional Chinese. Unsupported cultures fall back to English.

`SettingsRepositoryTests` covers initial seeding, persistence and culture mapping.
`OptionsViewModelTests` covers picker selection and null updates. Native review should check
the picker, live relabeling, restart persistence and RTL layout on an installed build.

The picker and first-run seeding replaced the original language chips in August 2026.
