# UI Localization (English / Japanese) — Design

Date: 2026-06-07
Status: Approved approach; spec under review.

## Context

TokenCheckerWin's UI is currently Japanese-only — every user-facing string is
hardcoded in the `TokenChecker.App` assembly (Core has none; providers emit only
masked, English `key=value` diagnostics). The owner wants the UI switchable
between **English** and **Japanese**. This is sub-project **C** of a three-part
effort (A = add a license [done], C = UI i18n, B = make the docs English-primary).

Approved decisions (from brainstorming):
- **Approach:** a custom static string table resolved once at startup — mirrors
  the existing theme pattern (`UsageTheme.Apply` / `WindowsTheme.ResolveDark`
  called in `Program` before `ApplicationConfiguration.Initialize()`). No `.resx`
  / satellite assemblies (keeps the single-file self-contained publish simple and
  the strings unit-testable).
- **Switch timing:** applied at **startup** (restart to take effect), exactly like
  the theme. A settings change is saved and used on the next launch.
- **Default:** **System** — follow the OS UI language (`ja*` → Japanese, else
  English).

## Goals / non-goals

Goals: all user-facing UI text (settings dialog, tray menu & tooltips, status
window, Copilot window, first-time setup wizard, friendly status messages,
reset-time text) is shown in the selected language.

Non-goals (stay as-is, English/neutral):
- Masked diagnostics (`DiagnosticMasker` output) and the "詳細" diagnostic box
  payload — these are `key=value` technical text, not prose.
- Log/exception text and code comments (comments are not translated).
- Provider internal strings in `TokenChecker.Core` (there are none user-facing).

## Architecture

### 1. Setting — `AppSettings`
- `internal enum AppLanguage { System = 0, English = 1, Japanese = 2 }`
- `public AppLanguage Language { get; set; } = AppLanguage.System;`
- `Normalize()`: `if (!Enum.IsDefined(Language)) Language = AppLanguage.System;`
- `Clone()`: copy `Language`.
- Persisted by name via the existing `JsonStringEnumConverter`.

### 2. Resolution — `LanguageResolver` (new, mirrors `WindowsTheme`)
- `static bool ResolveJapanese(AppLanguage setting)`:
  - `Japanese` → true; `English` → false;
  - `System` → `CultureInfo.CurrentUICulture` (fallback `InstalledUICulture`)
    two-letter name == `"ja"`.
- Pure and unit-testable (the System branch is tested by passing an explicit
  culture into an internal overload `ResolveJapanese(AppLanguage, CultureInfo)`).

### 3. The table — `Strings` (new static class, like `UsageTheme`)
- `private static bool _ja;`
- `public static void Apply(bool japanese) => _ja = japanese;` (called once at
  startup; default English so any pre-Apply read is safe).
- One member per user-facing string:
  - Plain: `public static string Settings => _ja ? "設定" : "Settings";`
  - Formatted: `public static string ResetIn(string hm, string at) => _ja ? $"あと{hm}（{at}リセット）" : $"in {hm} (resets {at})";`
- Members are grouped by area with comments (Common / Tray / Status / Copilot /
  Setup wizard / Provider status). This is the bulk of the work: extract each
  hardcoded Japanese UI literal and route it through a `Strings` member.

### 4. Startup — `Program`
- Before `ApplicationConfiguration.Initialize()` (next to the theme apply):
  `var settings = new SettingsStore().Load();`
  `Strings.Apply(LanguageResolver.ResolveJapanese(settings.Language));`
  (The theme block already loads settings; reuse the single load.)

### 5. Settings UI — `SettingsForm`
- Add a **Language / 言語** `ComboBox` to the 共通設定 group:
  `システム連動 / English / 日本語` (System / English / Japanese), with a
  "(再起動で反映) / (restart to apply)" note (the note text itself is localized).
- The settings dialog's own labels are localized via `Strings`.
- Group/dialog heights nudged to fit the added row.

## Files touched (string extraction)

Replace hardcoded Japanese UI literals with `Strings.*` in (count = Japanese-
containing lines incl. comments; actual UI-string subset is smaller):
- `SettingsForm.cs` (~64), `GitHubCopilotSetupForm.cs` (~48),
  `CopilotWindow.cs` (~35), `TrayApplicationContext.cs` (~22, menu + tooltips),
  `ProviderStatusPresenter.cs` (~17, friendly/status text & badges),
  `StatusForm.cs` (~16), `ResetTimeFormatter.cs` (~12),
  `AuthCommandService.cs` (~6).
- `CopilotUsageTracker.cs`'s Japanese is sub-info text shown in the Copilot card
  (prediction / today delta) — those user-facing strings move to `Strings`;
  comments stay.

New files: `Strings.cs`, `LanguageResolver.cs` (both `internal`, in
`TokenChecker.App`).

## Testing

Add to `TokenChecker.App.Tests`:
- `StringsTests`: reflect over all public static `string` properties of `Strings`;
  assert each returns non-empty under both `Apply(true)` and `Apply(false)`
  (catches a missing translation). Spot-check a few keys map to the right text.
- `LanguageResolverTests`: `Japanese`→true, `English`→false, `System` with a
  `ja-JP` culture → true and an `en-US` culture → false.
- `AppSettingsTests`: `Normalize` undefined `Language` → System; `Clone` copies
  `Language`.

`dotnet test` must stay green (the pre-push hook + CI gate it).

## Verification

1. `dotnet build` 0/0; `dotnet test` green.
2. `--show-settings` with `Language=English` (temp settings) → dialog, menu,
   windows render in English; with `Japanese` → Japanese; restore settings.
3. Render the status/settings/Copilot windows via DrawToBitmap in both languages
   for an objective check; final look verified on the real machine.
4. Privacy invariant unchanged: diagnostics still masked & English; no new
   persisted fields beyond `Language` in `settings.json`.

## Out of scope / sequencing

- Sub-project **B** (docs English-primary) follows; the English README will then
  describe this language toggle.
- 80/95 thresholds, providers, `copilot_usage.json` schema, token/auth path:
  untouched.
