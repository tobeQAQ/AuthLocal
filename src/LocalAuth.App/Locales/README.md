# Localization

Simplified Chinese source strings act as message IDs, like gettext source messages.
`en.json`, `fr.json`, `ja.json` and `zh-TW.json` contain the same set of IDs.
`UiText` embeds these JSON files into the executable and falls back to English for
unsupported system languages. No online translation service is used at runtime.

Keep numeric placeholders (`{0}`, `{1}`), file-filter separators and intentional
leading/trailing spaces. Short card actions must fit the copy button. User-entered
names and passwords are never translated.

Run `python scripts/check-localization.py` before submitting a translation.
