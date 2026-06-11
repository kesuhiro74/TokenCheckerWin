# UI Localization (English / Japanese) — Design

Date: 2026-06-07
**Status: Implemented (v0.8.x)**

> Note: The implementation diverged from this design in §3 (The table). The design
> described a `Strings` class with one `static string` property per key (e.g.
> `public static string Settings => _ja ? "設定" : "Settings"`). The actual
> implementation uses a **lookup-table approach**: Japanese text is the key, and
> `Strings.T(ja)` / `Strings.Tf(jaFormat, args)` look it up in a static
> `Dictionary<string, string> English` table, falling back to the Japanese key when
> a translation is missing. This keeps the calling convention simple (pass the
> Japanese literal; no per-string property needed) and the table easy to audit.
> Everything else in this design (timing, default, setting persistence, startup
> wiring, LanguageResolver, SettingsForm language combo) was implemented as
> described.

## Context

TokenCheckerWin's UI was Japanese-only. The owner wanted the UI switchable between
**English** and **Japanese**, applied at startup (restart to take effect), defaulting
to **System** (follow the OS UI language).

## Implemented architecture (summary)

- `AppSettings.AppLanguage { System, English, Japanese }` — persisted by name via
  `JsonStringEnumConverter`.
- `LanguageResolver.ResolveJapanese(AppLanguage)` — resolves System via
  `CultureInfo.CurrentUICulture` (`ja*` → Japanese, else English).
- `Strings` — static class with `Apply(bool japanese)` called once at startup.
  `T(ja)` / `Tf(jaFormat, args)` look up the English translation in the static
  `Strings.English` dictionary (Japanese key → English string), falling back to the
  Japanese text when no entry is found.
- `Program.Main` — calls `Strings.Apply(...)` before `ApplicationConfiguration.Initialize()`, next to `UsageTheme.Apply(...)`.
- `SettingsForm` — Language combo (システム連動 / English / 日本語) in 共通設定,
  with a "(再起動で反映)" note.

## Testing

`tests/TokenChecker.App.Tests/LocalizationTests.cs` covers:
- All keys in `Strings.English` return non-empty under both `Apply(true)` and
  `Apply(false)`.
- Spot-checks that selected keys map to the expected text.

## Out of scope (unchanged)

- Masked diagnostics and `詳細` box payload (English `key=value` technical text).
- Code comments and log/exception text (English throughout).
- Provider internal strings in `TokenChecker.Core` (none are user-facing).
- 80/95 thresholds, providers, `copilot_usage.json` schema, token/auth path.
