# TokenCheckerWin

English | **[日本語](README.ja.md)**

TokenCheckerWin is a tray-resident Windows app that shows your Claude Code, OpenAI Codex, and GitHub Copilot usage from the notification area. It fetches the Claude Code / Codex 5-hour and weekly rate limits and the GitHub Copilot current-month AI Credits, and presents them in readable flyouts. When a fetch fails, it keeps showing the last successfully retrieved values.

The app stores **no credentials or tokens** (see [Privacy](#privacy)). Claude Code usage is read through an **unofficial** endpoint, so a future change on Anthropic's side may make it stop working. Windows SmartScreen may show a warning for the unsigned build.

## Screenshots

<img width="620" height="43" alt="image" src="https://github.com/user-attachments/assets/3046c2fa-10ad-4a96-b331-4e03bba7596b" /><br>
<img width="407" height="98" alt="image" src="https://github.com/user-attachments/assets/b8277701-1950-4f4b-8a51-6967345447d4" /><br>
<img width="385" height="432" alt="image" src="https://github.com/user-attachments/assets/67122aaa-72ee-4285-a2d7-66bab3362a99" /><br>
<img width="301" height="214" alt="image" src="https://github.com/user-attachments/assets/c6ddf833-60ab-40cd-96a1-61f4cbbfbb2b" />
<img width="301" height="216" alt="image" src="https://github.com/user-attachments/assets/6503145a-a8f4-4dbc-8ae6-f29edb2badc4" />

## Features

- Resides in the Windows notification area; per-window tray icons render a vertical % bar in code (no external image).
- **Claude Code / Codex**: 5-hour and weekly rate limits, time remaining until reset, per-service brand colors (Claude = blue, Codex = purple).
- Three display modes for the Claude / Codex window: **Normal** (detailed cards), **Compact** (side-by-side doughnuts), **Minimum** (a Nerd Font one-line status per service).
- **Today's estimated spend**: today's tokens from the local Claude / Codex session logs, priced with a built-in per-model table, shown in the UI language's currency — US dollars (`$N.NN (daily)`) in English, Japanese yen (`¥N (daily)`, via a public USD→JPY rate fetched once a day) in Japanese. On the Normal cards and the Minimum line; hidden when it cannot be computed.
- **GitHub Copilot**: current-month AI Credits consumption in a dedicated window (opt-in), with today's-burn pace and a 100%-reach date estimate.
- Color escalation at **80% (amber) / 95% (red)** across the icons, numbers, and bars.
- Title-bar-less rounded flyouts: drag anywhere to move, `Esc` to close, position remembered.
- Last-successful-value fallback when a fetch fails.
- Claude Code / Codex sign-in assistance (delegated to the official CLIs).
- Light / Dark / System-linked theme and UI language (System / English / Japanese), both applied on restart.
- Auto-start at Windows sign-in.

## Requirements

- Windows 11
- .NET 10 SDK (`global.json` pins `10.0.100` with `rollForward: latestFeature`)
- VS Code (optional; Visual Studio is not required)

## Build and run

```powershell
dotnet build

# Run as a tray-resident app
dotnet run --project src/TokenChecker.App

# Open a window at startup (handy for development / screenshots)
dotnet run --project src/TokenChecker.App -- --show-status      # status window
dotnet run --project src/TokenChecker.App -- --show-settings    # settings dialog
```

With no arguments, each window follows the display method chosen in settings (`Always show` opens at startup; `Hover preview` appears when you hover its tray icon). `--show-status` / `--show-settings` are case-insensitive helpers that just open the corresponding window.

To publish a self-contained single-file build:

```powershell
dotnet publish src/TokenChecker.App/TokenChecker.App.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
  -p:DebugType=none -o publish/win-x64

.\publish\win-x64\TokenChecker.exe --show-status
```

## Display and settings

### Display modes (Claude / Codex)

Switch from the settings dialog or the tray menu; the choice is saved to `DisplayMode` in `settings.json` (a legacy `CompactMode` boolean is mirrored for backward compatibility). Every mode is a rounded flyout you can drag anywhere (except links / text fields) and close with `Esc`.

- **Normal** — the most detailed layout, stacking the Claude / Codex cards. The `5h` usage is the hero (large number + wide bar) with the time until reset inline next to its label (e.g. `2h 18m (resets 11:50)`); `Weekly` sits below with its reset datetime inline (e.g. `(resets 6/17 18:00)`); then today's estimated spend (`¥46 (daily)`), a status badge, and a `Show details` link.
- **Compact** — two side-by-side cards showing only the `5h` doughnut, badge, and reset text. The window shrinks to the number of visible services; the `Last updated` line is omitted.
- **Minimum** — a single Nerd Font status line per service: `<icon> Claude | 5h 38% 4h39m 10:50 | 7d 39% 2d 6/17 18:00 | ¥46 (daily)` (service name; the 5-hour and weekly `%`, each followed by the time remaining and the reset time — `4h39m` / `2d`; today's spend). Text is drawn in Cascadia Mono and the icons in Symbols Nerd Font (icon runs are skipped entirely when that font is absent); the `|` separators are aligned between the two rows and the window auto-fits the line width. A segment is dropped when its data is unknown (e.g. no reset time, or cost not computable).

### Tray icon

Each enabled window gets a vertical % bar icon. The Claude / Codex icon shows the maximum `usedPercent` of the enabled services, so the most constrained window stands out at a glance. The color follows the overall worst value: brand color (`< 80%`), amber (`>= 80%`), red (`>= 95%`), muted red (all providers errored), light gray (no data yet). The hover tooltip shows a per-service badge or `5h X% / Weekly Y%`.

### Tray right-click menu (5 items)

The same menu appears from any tray icon, including the control icon shown when both windows are OFF:

- `Refresh now` — re-fetch usage (locked against duplicate refreshes).
- `Claude/Codex status display mode` ▶ (`Normal` / `Compact` / `Minimum`).
- `GitHub Copilot display mode` ▶ (`Always show` / `Hover preview`).
- `Settings` — open the settings dialog.
- `Exit`.

Sign-in / sign-out, re-checking auth, and the GitHub Copilot first-time setup / connection test live in the settings dialog, not the menu.

### Status badges and reset time

Badges follow the UI language (`OK` / `Not installed` / `Not logged in` / `Auth error` / `Temporarily rate-limited` / `Fetch failed` / `Unknown`); raw `ProviderStatus` names are never shown. `Temporarily rate-limited` means the usage endpoint itself returned HTTP `429` (not an LLM quota overage). Reset times are local: `5h` shows `in 2h 18m (resets 11:50)`, `Weekly` shows `in 3d 4h (resets 5/27 18:00)`.

### Saved settings

`settings.json` (under the user's `AppData` folder) stores **app settings only**: refresh interval (`30 s` / `1 min` / `5 min` / `10 min`), auto-start, theme (`ThemeMode`), UI language (`Language`), `DisplayMode`, each window's ON/OFF and display method, the visible Claude / Codex services, the Copilot plan / custom cap, accent color, and window positions. It never stores tokens, credentials, login, URLs, paths, or email. A corrupted file falls back to defaults.

- **Theme** (Light / Dark / System-linked) and **UI language** (System / English / Japanese) are applied **at startup**; when you change either, the app offers to **restart now** (it relaunches itself cleanly) so the change takes effect. `System` follows the Windows color mode / display language.
- An off-screen saved window position is corrected back into the visible area on open.
- Auto-start uses the `TokenCheckerWin` value under the current user's Run key (the published exe path, or the `dotnet "<app dll>"` form for a dev run).

### Last-successful-value fallback

The last successful usage is retained per service, so a temporary failure of Claude or Codex does not wipe out the other's value. It is also persisted to `last_usage.json` so the previous value survives a restart (e.g. an `HTTP 429` right after startup). On failure the badge shows the current status and the body shows `Temporarily unavailable — showing the last successful values`; the next success overwrites the snapshot.

## GitHub Copilot (AI Credits, opt-in)

GitHub Copilot moved to usage-based billing (AI Credits) on 2026-06-01. There is no real-time rate-limit window like Claude / Codex — a personal token can only retrieve the **current month's AI Credits consumption**. The API does not expose the monthly cap, so you supply it by **selecting a plan in settings (or entering a custom value)**.

- **Off by default (opt-in).** Only when you enable "Show the GitHub Copilot window" does the app touch the GitHub billing endpoint; while disabled, the fetch never runs.
- **Dedicated window** showing the Copilot icon + `GitHub Copilot`, the selected plan name, a `Credits` caption with the estimated monthly reset (e.g. `Reset ~7/1`), and the usage prominently (e.g. `66% used`). **Hovering the `n% used` area** (or focusing it) reveals the detail values (e.g. `4,627 / 7,000 used`). The window uses the `Moralerspace` font if installed, otherwise Segoe UI.
- **Today's burn** is a second hero line — the increment since 09:00 today (e.g. `✦ Today +96 (+1.4%)`, or `Today: not measured` with no baseline). Its spark icon escalates with the **daily pace**: green under 4%, amber 4–5%, red 5%+ (distinct from the monthly 80/95% thresholds). The same warning spark is overlaid on the tray icon on heavy days. Below it, the predicted 100%-reach date (e.g. `At this pace, 100% around 6/21`, or `Not enough data to project`).
- **Plan** (None / Free=200 / Pro=1,500 / Pro+=7,000 / Max=20,000 / Custom) is chosen in settings. `None` shows only the consumption; a plan adds the cap, remaining, fraction, and bar, escalating at 80% / 95%. (Free's code-completion and chat quotas are not in the billing endpoint and are not shown.)
- **Accent color** for the bar's normal (< 80%) color is selectable (`Blue` default / `Green` / `Sky` / `Purple` / `Slate`); the number and `used` label are fixed, and the 80/95% escalation is preserved.
- **The reset date is a calendar-month approximation** (not retrievable from the API); it can drift if your billing cycle is anchored to the signup date.
- **Constraints**: only usage billed directly to the personal account is in scope (Organization / Enterprise-managed usage may `403` / `404`); accounts not on the Enhanced Billing Platform may not work.
- **HTTP handling**: `GITHUB_TOKEN` unset → `Not logged in`; `401` → invalid/expired; `403` → check Plan(Read) permission, personal billing, Enhanced Billing eligibility; `429` → `Temporarily rate-limited`; 5xx / timeout / JSON anomaly → `Fetch failed`. No current-month consumption shows as 0 usage.

### First-time setup (`GITHUB_TOKEN` environment variable)

The token is read **only** from the `GITHUB_TOKEN` environment variable — never entered into the app, never stored, never printed. (In-app sign-in / OAuth / Device Flow is not implemented.) "GitHub Copilot settings" has **`First-time setup`** (a wizard) and **`Connection test`** buttons.

1. Create a GitHub **fine-grained PAT** (name it e.g. `TokenChecker`; expiration `90 days` recommended).
2. Under `[+ Add permissions]`, add **`Plan`** and select **`Read-only`**.
3. Press `Generate token` and copy it.
4. Set it as the Windows **user environment variable `GITHUB_TOKEN`** (do not type it into the app), then restart TokenCheckerWin so a running instance picks it up.

The **connection test never displays the token**. On success it shows only usage numbers — `Fetched successfully.` / `This month: 4,627 / 7,000 credits` / `Usage: 66%` (or `This month: 4,627 credits` when the plan is `None`). On failure it shows only safe boilerplate (unset / invalid-or-expired / insufficient permission / rate-limited / fetch-failed). When `GITHUB_TOKEN` is unset and the window is ON, the window points you to "First-time setup" (no token field is placed in the window).

## Privacy

The app **never** writes tokens, OAuth credentials, full paths, or email addresses to the UI, tooltips, logs, or any saved file.

- **Sign-in assistance** (the Claude Code / Codex `Log in` / `Log out` buttons) only launches the official CLI in a new `cmd.exe` — it does **not** read `~/.claude/.credentials.json`, `~/.codex/auth.json`, the Windows Credential Manager, API keys, etc. After running `claude` / `codex`, press `Re-check auth status` (which just re-runs the fetch).
- **During a usage fetch**, the Claude Code provider reads the access token from `~/.claude/.credentials.json` (override with `CLAUDE_CONFIG_DIR`) **for the sole purpose** of calling the unofficial usage endpoint. It is **read-only**: the token, credentials body, email, and paths are never displayed, saved, or logged, and the app never writes to any credential file.
- **For the daily-spend estimate**, the app reads the local Claude / Codex session logs (`~/.claude/projects`, `~/.codex/sessions`; override with `CLAUDE_CONFIG_DIR` / `CODEX_HOME`) **read-only**, extracting only token counts and model ids — never conversation content, cwd, or paths. The USD→JPY rate is fetched once a day from a free public FX API (falling back to a fixed rate on failure). Neither the rate nor the computed cost is persisted (in-memory only), so the three-file rule below is unchanged.
- **Diagnostics** never appear in the normal view; they show only inside `Show details`, and only after masking via the single `DiagnosticMasker`:
  - email-like → `<email>`
  - absolute paths (Windows / UNC / POSIX) → `<path>`
  - `token` / `secret` / `key` / `authorization` / `bearer` followed by `:` or `=` → `name=<redacted>`
  - JWTs (`eyJ...`) → `<redacted>`
  - long alphanumeric blobs (≥ 32 chars) → `<redacted>`
- **The app writes only these three files** (under `AppData`, all numbers/dates only — no tokens, login, URLs, paths, or email):
  - `settings.json` — app settings only.
  - `last_usage.json` — numeric Claude / Codex usage only (the diagnostic `Message` is nulled before writing).
  - `copilot_usage.json` — GitHub Copilot delta tracking (target month, last fetch time, current-month used credits, the 09:00-today baseline); created only when the Copilot window is enabled and a fetch succeeds.

The Claude Code usage endpoint is unofficial and may change without notice — treat the displayed Claude usage as best-effort.

## Development

### Project layout

- `src/TokenChecker.Core` — platform-agnostic shared library: usage models, the provider interface, `UsageAggregator`, and the provider implementations (`Providers/`).
- `src/TokenChecker.App` — WinForms + `NotifyIcon` notification-area app.
- `src/TokenChecker.Poc` — console POC that writes a `UsageSnapshot` to stdout as JSON.

`TrayApplicationContext` drives a timer that calls `UsageAggregator.CaptureAsync()`; each provider returns a `ServiceUsage`, and the aggregator isolates per-provider failures so one service's error never wipes out the others.

### Tests

An xUnit suite under `tests/` pins the app's invariants (the 80%/95% thresholds, privacy masking, the Copilot allowance, prediction logic, settings migration, provider-failure isolation, and more). Keep the whole solution green before committing or releasing.

```powershell
dotnet test
```

GitHub Actions (`.github/workflows/ci.yml`) runs `dotnet test` on windows-latest for every push / PR. A local `.githooks/pre-push` hook also runs `dotnet test` before each push and aborts on failure — enable it once per clone with `git config core.hooksPath .githooks`.

### Provider notes (POC)

```powershell
dotnet run --project src/TokenChecker.Poc                              # Claude / Codex snapshot as JSON
dotnet run --project src/TokenChecker.Poc -- --github-copilot          # Copilot AI Credits only
dotnet run --project src/TokenChecker.Poc -- --github-copilot --raw    # masked endpoint diagnostics
```

- Each provider first checks whether its CLI is on the PATH; if not, it returns `NotInstalled`.
- **Claude** uses the unofficial OAuth usage endpoint. `five_hour` maps to a 300-minute window, `seven_day` to 10080 minutes. HTTP `401`/`403` → `Unauthorized`, `429` → `RateLimited`, 5xx / timeout / JSON errors → `Error`. If `.credentials.json` exists but yields no usable access token (e.g. emptied by `/logout`), it reports `NotLoggedIn` rather than `Error`.
- **Codex** launches `codex app-server --listen stdio://` and calls `initialize` → `account/read` → `account/rateLimits/read` over JSONL, then stops the app-server. A not-signed-in response is `NotLoggedIn`; an `account.type` other than `chatgpt` (e.g. API-key auth) reports `Error`, since ChatGPT sign-in is required.
- Tokens, auth data, email, and full paths are never read out or printed. With `--raw`, the Copilot provider tries the candidate endpoints (`ai_credit/usage` → `usage` → `premium_request/usage`) and prints only the HTTP status and masked `usageItems` fields — never the response body, login, or token. `GITHUB_TOKEN` is required.

## License

This software is distributed under the [MIT License](LICENSE) (Copyright (c) 2026 kesuhiro74). For the bundled third-party components (GitHub Octicons, the .NET runtime), see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
