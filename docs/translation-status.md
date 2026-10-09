# Translation review

As of 2026-09-28, the app lists 39 languages: neutral English and 38 resource files. Each has
390 keys. Chinese has separate Simplified (`zh-Hans`) and Traditional (`zh-Hant`) entries.

No complete native-speaker sign-off is recorded. Treat translated text as awaiting review,
including the latest trial, trophy and picture names. Earlier draft confidence ratings have
been removed because they were not based on a recorded language review.

## Automated checks

Run `dotnet run tools/check-strings.cs` after resource changes. It checks keys, placeholder
parity and selected script errors. These checks cannot establish grammar, word choice or
whether a label fits on a device. See [audit status](audit.md) for the current validation results.

`AppLanguages.All` defines supported codes and picker order. Missing resource values can
fall back to English. Test language selection, persistence and announcements on an installed
app. [Language initialization](language-selection.md) describes the first-run behavior.

## Review priorities

| Area | What to check |
| --- | --- |
| Tone | Clear language suitable for children aged 6 and up, especially mascot lines and mistake feedback. There is no upper age limit; adult players should feel welcome too. |
| Picture names | Everyday names. The cat is a whole animal, not only a face. |
| Trials and trophies | Idioms such as "Beat the Clock" and "Star Gazer"; consistent game terms. |
| Placeholders | Preserve `{0}`, `{1}` and `{n}` and their intended meaning. |
| Short labels | Fill, cross, undo, redo, hint and pause at large font sizes. |
| Accessibility | Read complete cell and board descriptions aloud, including clues and numbers. |
| RTL | Arabic, Hebrew and Persian layouts and mixed-direction numbers on a device. |
| Regional usage | Traditional Chinese vocabulary and Norwegian locale naming. |

Maltese, Zulu and Swahili drafts need particular attention to borrowed terms and game vocabulary.
This identifies review work, not a measured quality rating.

## Recording a review

Record language, reviewer, date, source revision, screens checked and unresolved issues.
Use a dedicated test profile for first-run tests. Include font and script fallback;
[font coverage notes](../src/Squarebuzz.App/Resources/Fonts/README.md) describe the bundled faces.

Earlier Android observations for Chinese and Hebrew are historical evidence. Repeat the
current screens after the artwork and dependency changes.
