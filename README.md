# TokenCheckerWin

English | **[日本語](README.ja.md)**

TokenCheckerWin is a tray-resident Windows app that shows your Claude Code, OpenAI Codex, and GitHub Copilot AI Credits usage from the notification area.

It fetches the Claude Code / Codex 5-hour and weekly rate limits and presents them in three readable layouts — Normal, Compact, and Minimum. When a fetch fails, it can keep showing the last successfully retrieved usage.

## Screenshots

<img width="413" height="102" alt="image" src="https://github.com/user-attachments/assets/affc94d4-e3e7-4d96-95c2-1580b17e4362" /><br>
<img width="302" height="203" alt="image" src="https://github.com/user-attachments/assets/405f7698-9996-42fc-ad0c-c8f446e7d955" />
<img width="302" height="206" alt="image" src="https://github.com/user-attachments/assets/bec22e93-53f7-4c57-b703-23d9e3c552da" />

## Features

- Claude Code / Codex usage display
- 5-hour and weekly rate-limit display
- Resides in the Windows notification area
- Three display modes: Normal / Compact / Minimum
- Bar / doughnut usage rendering (per-service brand colors)
- Time-remaining-until-reset display
- Title-bar-less rounded flyout (drag anywhere to move, Esc to close)
- Claude Code / Codex sign-in assistance
- GitHub Copilot AI Credits current-month consumption display (opt-in, dedicated window)
- Last-successful-value fallback display
- Light / Dark / System-linked theme (follows the Windows color mode)
- UI language switch (System-linked / English / Japanese, applied on restart)
- Auto-start at Windows sign-in

## Notes

- Claude Code usage is retrieved through an unofficial usage endpoint
- A future specification change may make retrieval stop working
- This app does not store any credentials or tokens
- Claude Code / Codex sign-in is performed by launching each official CLI
- Windows SmartScreen may display a warning

## Requirements

- Windows 11
- VS Code
- .NET 10 SDK (global.json pins 10.0.100 with rollForward latestFeature)
- Visual Studio is not required

## Project layout

- `src/TokenChecker.Core`: shared library for the usage models, provider interface, aggregator, and each provider implementation
- `src/TokenChecker.Poc`: console POC that writes a `UsageSnapshot` to stdout as JSON
- `src/TokenChecker.App`: WinForms + `NotifyIcon` notification-area resident app

## Build

```powershell
dotnet build
```

## Test

An xUnit test project under `tests/` pins the app's invariants (the 80%/95% thresholds, privacy masking, the Copilot allowance, prediction logic, settings migration, provider-failure isolation, and more). Make sure the whole solution is green before committing or releasing.

```powershell
dotnet test
```

GitHub Actions (`.github/workflows/ci.yml`) runs `dotnet test` on windows-latest for every push / PR.

## Running the POC

```powershell
dotnet run --project src/TokenChecker.Poc
```

To check only the GitHub Copilot AI Credits provider, pass `--github-copilot`. Adding `--raw` hits the candidate endpoints (`ai_credit/usage` → `usage` → `premium_request/usage`) and prints only the HTTP status and the masked `usageItems` fields (`product` / `sku` / `unitType` / `quantity` / `grossQuantity` / `grossAmount` / `netQuantity` / `netAmount` / `copilot`) — never the response body, login, or token. A `GITHUB_TOKEN` is required.

```powershell
dotnet run --project src/TokenChecker.Poc -- --github-copilot
dotnet run --project src/TokenChecker.Poc -- --github-copilot --raw
```

The POC prints the Claude / Codex `UsageSnapshot` as JSON. The main provider behaviors are:

- It first checks whether `claude` / `codex` exist on the PATH.
- The Codex provider launches `codex app-server --listen stdio://`, streams JSONL over stdin/stdout, and calls `initialize` → `account/read` → `account/rateLimits/read`. It stops the app-server process after reading.
- Each `rateLimitsByLimitId` entry is parsed generically and exposed as a `RateLimitWindow` when `usedPercent` / `windowDurationMins` / `resetsAt` are available.
- Tokens, auth data, email addresses, and full paths are never read out or printed.
- If a CLI is not found, it returns `NotInstalled`.
- A Codex not-signed-in response is treated as `NotLoggedIn`.
- When the Codex `account.type` is not `chatgpt` (e.g. API-key auth), it reports `Error` because usage cannot be retrieved.
- Codex launch failures, timeouts, and JSON/protocol errors are reported as `Error` without failing the whole POC.
- Claude Code usage uses an unofficial OAuth usage endpoint. The specification may change without notice.
- The Claude Code OAuth credentials are read only to extract the access token that endpoint needs. The credentials JSON body, tokens, email addresses, and full paths are never printed.
- Claude Code `five_hour` maps to a 300-minute `RateLimitWindow` and `seven_day` to a 10080-minute `RateLimitWindow`.
- For the Claude Code usage endpoint, HTTP `401`/`403` is `Unauthorized`, `429` is `RateLimited`, and 5xx / timeout / JSON errors are `Error`.
- If `.credentials.json` exists but no valid access token can be extracted from it (e.g. the file was emptied by `/logout`, or the JSON shape is unexpected), it reports `NotLoggedIn` rather than `Error`. The UI then shows a "not signed in" badge and a "Please sign in to Claude Code" message.

## Using it from VS Code

Open this folder in VS Code and use any of:

- Terminal: `dotnet build`
- Terminal: `dotnet run --project src/TokenChecker.Poc`
- Task: `dotnet build`
- Task: `run poc`
- Debug configuration: `TokenChecker.Poc`

## Trying out the notification-area app

To launch it as a system-tray resident:

```powershell
dotnet run --project src/TokenChecker.App
```

To open the status window right at startup (handy for development, screenshots, and post-publish checks), pass `--show-status`:

```powershell
dotnet run --project src/TokenChecker.App -- --show-status
.\publish\win-x64\TokenChecker.exe --show-status
```

When launched without arguments, each window follows the display method you selected in settings (`Always show` / `Hover`): an `Always show` window appears at startup, while a `Hover` window appears when you hover its tray icon. `--show-status` is just a helper that explicitly shows the Claude / Codex status window at startup.

To open only the settings dialog, pass `--show-settings`:

```powershell
dotnet run --project src/TokenChecker.App -- --show-settings
```

### Display modes

You can switch between three display modes from the settings dialog and the tray right-click menu. The chosen value is saved to the `DisplayMode` field of `settings.json` (the boolean `CompactMode` is also written for backward compatibility with older versions).

Every mode is shown as a title-bar-less rounded flyout. You can drag anywhere on the window to move it (except links and text fields), and close it with the `Esc` key or by clicking the tray icon again. The window position is saved and reopens in the same place.

- **Normal**: the most detailed layout, stacking the Claude Code / Codex cards vertically. It features the `5h` usage as the hero — a large number plus a wide progress bar — with the `Weekly` usage (number plus a subtle thin bar) below it. It also has the time remaining until the `5h` reset (e.g. `2 h 18 m left (resets 11:50)`), a status badge, and a `Show details` link (collapsible masked diagnostics).
- **Compact**: a space-saving mode that condenses Claude Code / Codex into two side-by-side cards showing only the `5h` doughnut chart, the badge, and the reset text. The window width shrinks to the number of visible services (a single card's width when only one is shown), with reduced padding. The `Last updated` line is omitted in this mode.
- **Minimum**: the smallest layout, stacking the two services as one row each (`● ServiceName ──bar── 45%`). Each row shows usage with a dot in the service brand color (Claude = blue / Codex = purple) and a thin wide bar, escalating to amber at 80%+ and red at 95%+. Weekly, diagnostics, and reset time are omitted in this mode.

### Notification-area icon

- It carries no external image and generates the icon in code at startup. An icon is shown per enabled window (Claude / Codex use vertical % bars; GitHub Copilot also uses a vertical % bar).
- The Claude / Codex vertical % bars show the maximum `usedPercent` of the enabled services as a bar that grows vertically, so a single glance at the tray hints at the most constrained window. Two bars appear when both Claude and Codex are enabled, one when only one is.
- The color changes with the overall worst value.
  - Normal (`< 80%`): the service brand color
  - Warning (`>= 80%`): amber
  - Critical (`>= 95%`): red
  - Error (both providers `NotInstalled` / `NotLoggedIn` / `Unauthorized` / `RateLimited` / `Error`): muted red
  - Before retrieval / no data: light gray
- The hover tooltip follows the UI language and shows a per-service status badge, or `5h X% / Weekly Y%`.

### Claude Code / Codex sign-in assistance

This app does not store credentials. All sign-in is delegated to the official CLIs.

- The tray right-click menu has exactly **five items** (the same menu regardless of which tray icon you right-click — including the control icon shown when both windows are OFF).
  - `Refresh now` — re-fetch usage (does not crash even if a provider is absent).
  - `Claude/Codex status display mode` ▶ (`Normal` / `Compact` / `Minimum`) — checks the current `DisplayMode`. Selecting one immediately switches the status window and saves to `settings.json`. Disabled when the Claude/Codex window is OFF.
  - `GitHub Copilot display mode` ▶ (`Always show` / `Hover preview`) — checks the current `CopilotDisplayMode`. Selecting one immediately switches the Copilot window's display method and saves it (`Always show` = shown when enabled, unpinned into a unified always-on state; `Hover preview` = hidden unless pinned, shown by hovering the tray icon). Disabled when the Copilot window is OFF.
  - `Settings` — opens the settings dialog.
  - `Exit`.
- Log in / out, re-checking the auth status, and GitHub Copilot first-time setup / connection test are **kept out of the right-click menu and consolidated in the settings dialog**.
- Pressing `Log in` for Claude Code in the settings dialog opens a new `cmd.exe` and launches the `claude` CLI. Type `/login` at the prompt to finish logging in, then press `Re-check auth status`. `Log out` likewise opens `claude`, and you type `/logout`. The app never reads `.credentials.json` or OAuth tokens.
- `Log in` for Codex in the settings dialog runs `codex login` in a new `cmd.exe`. Finish the ChatGPT sign-in in your browser, then press `Re-check auth status`. `codex logout` works the same way. Codex usage retrieval requires ChatGPT sign-in; API-key auth cannot retrieve usage.
- `Re-check auth status` in the settings dialog simply re-runs the usage fetch. If sign-in succeeded, the status switches to `OK` and the tray icon updates.
- The settings dialog has an `Auth status` section showing the current status of `Claude Code` / `Codex` (`OK` / `Not logged in` / `CLI not found` / `Auth error` / `Temporarily rate-limited` / `Fetch failed`), per-service `Log in` / `Log out` buttons, and a shared `Re-check auth status` button.
- If a CLI is not found on the PATH, it only shows a `Claude Code CLI not found` / `Codex CLI not found` message and does not launch any process.

### Status badges and messages

- Status badges follow the UI language (in English: `OK` / `Not installed` / `Not logged in` / `Auth error` / `Temporarily rate-limited` / `Fetch failed` / `Unknown`). The raw `ProviderStatus` enum names are never shown directly. `Temporarily rate-limited` appears only when the usage endpoint itself returned HTTP `429` (note: this is not an LLM quota overage).
- The body text below the badge shows a short user-facing sentence (e.g. `Claude Code usage is being read`). Raw diagnostic strings like `claudeFound=...; usageApi=...` are not shown in the normal view.
- Each card's `Show details` / `Hide details` link reveals masked diagnostic info for troubleshooting.

### Reset time

- The time remaining until reset is shown in local time.
- `5h` window: `in 2h 18m (resets 11:50)`
- `Weekly` window: `in 3d 4h (resets 5/27 18:00)`

### Settings and saved files

- Settings are saved under the current user's `AppData` folder and persist across restarts.
- `settings.json` stores **app settings only**: refresh interval, auto-start, theme (`ThemeMode`: system-linked / light / dark), UI language (`Language`: System-linked / English / Japanese), display mode (`DisplayMode`, plus `CompactMode` for backward compatibility), each window's ON/OFF, display method (`Always show` / `Hover`), the visible Claude / Codex services, the GitHub Copilot plan / custom cap, accent color, and each window's position. It never stores tokens, credentials, login, URLs, paths, or email addresses.
- If the settings file is corrupted, the app starts with defaults.
- The status window position is restored after moving / closing. If a saved position is off the current monitor layout, it opens corrected into the visible area.
- Settings can show/hide Claude Code / Codex. Even with both hidden, the app stays resident and the tray icon / menu remain available.
- Auto-start at Windows sign-in is managed by the `TokenCheckerWin` value under the current user's Run key. A published build registers the published exe's path; a development `dotnet` run falls back to the `dotnet "<app dll>"` form.
- The refresh interval can be `30 s` / `1 min` / `5 min` / `10 min`.
- **Theme (Light / Dark / System-linked)** can be chosen under "Common settings". `System-linked` follows the Windows color mode (Settings > Personalization > Colors). **It is applied only at startup**, so **restart the app** for a change to take effect (the settings screen also shows "(applied on restart)"). The windows (Claude/Codex, GitHub Copilot) and the settings dialog switch between dark/light (the tray icon palette is shared).
- **UI language (System-linked / English / Japanese)** can be chosen under "Common settings". `System` uses Japanese when the Windows display language (UI culture) is Japanese, and English otherwise. Like the theme, **it is applied only at startup**, so **restart the app** for a change to take effect across the entire UI (windows, settings dialog, tray menu, tooltips). The settings screen also shows "(applied on restart)".

### Last-successful-value fallback

- Even when a fetch fails, the last successful usage is retained per service, so a temporary failure of one of Claude / Codex does not wipe out the other's previous value.
- The last successful Claude / Codex usage is also persisted to `last_usage.json` in the same AppData folder as `settings.json`. Even if Anthropic returns `HTTP 429` right after startup (e.g. just after running the POC against the same usage endpoint), the previous usage stays in the doughnut.
- On failure, the badge shows the current status (`Temporarily rate-limited` / `Fetch failed`, etc.) and the body shows `Temporarily unavailable — showing the last successful values`. When the next fetch succeeds, the doughnut updates and the persisted snapshot is overwritten.
- `last_usage.json` stores only the numeric `Status` / `Windows` / `CapturedAtUtc` per service, with the provider's diagnostic `Message` string cleared to `null` before writing. It never stores tokens, credentials, email addresses, or paths.

### Other behaviors

- Repeatedly clicking `Refresh now` is locked so duplicate refreshes do not run.
- Exiting the app during a refresh also removes the tray icon, leaving no process behind.
- If `claude --version` times out during Claude CLI diagnostics, the child process is killed.

### GitHub Copilot (AI Credits, opt-in)

GitHub Copilot moved to usage-based billing (AI Credits) on 2026-06-01. There is no real-time rate-limit window (`utilization%` + `resets_at`) like Claude / Codex; a personal token can only retrieve the **current month's AI Credits consumption**. Because the monthly cap (included credit allowance) is not exposed by the API, the app supplies the cap by having you **select a plan in settings (or enter it manually)**.

- **Off by default (opt-in).** Only when you enable "Show the GitHub Copilot window" under "GitHub Copilot settings" does it access the GitHub billing endpoint. While disabled, it never touches `/user` or billing (the fetch itself does not run).
- **A dedicated window** (a rounded flyout separate from the Claude / Codex status window). The top shows the **Copilot icon + `GitHub Copilot`** (line 1) and the **selected plan name** (line 2, smaller and lighter, e.g. `Copilot Pro` / `Copilot Pro+` / `Copilot Max` / `Custom 7,000 credits`). Normally it shows only the usage prominently (e.g. `66% used`, with `used` smaller and light gray); **hovering the main display area (`n% used`)** or giving it keyboard focus switches to the detail values (e.g. `4,627 / 7,000 used`) — hovering anywhere else does not switch it. The bar fraction, reset estimate, sub-info, and window size are identical between the normal and detail states.
- **Font**: the GitHub Copilot window's main text (title, plan name, numbers, `used`, status badge, sub-info) uses `Moralerspace` **when it is installed**, and **falls back to the standard font (Segoe UI) when it is not** (the font is not bundled; the app works and does not crash without it).
- **A "Credits" caption row** sits just above the percentage: the label `Credits` (left) and the estimated monthly reset (right, e.g. `Reset ~7/1`).
- **Today's burn, promoted to a second hero line**: the increment since 09:00 today is shown with a spark icon — a small `Today` label (styled like the `used` suffix) plus a larger value (styled like the `n%` number), e.g. `✦ Today +96 (+1.4%)`, or `Today: not measured` when there is no baseline. Only the **spark icon** escalates with the **daily pace** — green under 4%, amber at 4–5%, red at 5%+ (a one-day burn measure, distinct from the monthly 80% / 95% usage thresholds). Below it, in small text, the predicted date of reaching 100% at the current pace (e.g. `At this pace, 100% around 6/21`, or `Not enough data to project` when impossible).
- **Tokens are read only from the `GITHUB_TOKEN` environment variable** (read-only, not stored, not printed). When unset, it reports "not signed in" and guides you to set a token. A fine-grained PAT needs **User permissions "Plan: Read"**.
- **Plan** (None / Free=200 / Pro=1,500 / Pro+=7,000 / Max=20,000 / Custom manual entry) is chosen in settings. With `None`, it shows only the consumption (credits). Choosing a plan shows the cap, remaining, fraction, and bar, escalating to amber at 80%+ and red at 95%+ (the same thresholds as Claude / Codex).
  - **Copilot Free** shows only a usage meter against the monthly AI Credits allowance (200). Free's code completions (inline suggestions, 2,000/month) and chat (50/month) quotas do not appear in the billing endpoint a personal token can read (completions do not consume credits), so **this app does not show them** (retrieving them would need Copilot OAuth sign-in and a private API, and in-app sign-in is intentionally not implemented).
- **Display method** can be chosen per window from two options (set separately for the Claude / Codex window and the GitHub Copilot window).
  - `Always show`: the target window is always shown while the app runs (you can re-show it even after closing, as long as the setting is ON).
  - `Hover`: hovering the target window's dedicated tray icon fades it in, and moving the mouse outside the window hides it immediately (moving from icon to window does not hide it). **Clicking** the tray icon switches it to pinned (always show).
- **Tray icons** are shown per enabled window (Claude / Codex use vertical % bars; GitHub Copilot also uses a vertical % bar). The Copilot vertical % bar uses the icon area vertically to show the current-month consumption fraction (against the plan cap) (a slightly squared rounded rectangle; the 80%/95% color changes are preserved). On top of the bar, a large warning spark is overlaid when today's burn (consumption since 09:00) runs high — amber at 4-5%, red at 5%+, nothing below 4% — so heavy days stand out at a glance (the same daily-pace thresholds as the card spark, distinct from the monthly 80/95%). When both windows are OFF, a single control icon is shown so you can still reach Settings and Exit.
- **Accent color**: you can choose the **normal color (below 80%) of the Copilot card bar and the tray vertical % bar** in settings (`Blue (default)` / `Green` / `Sky` / `Purple` / `Slate`). **The number (`n%`) is fixed to a near-black color and `used` to light gray** (unaffected by the accent setting). **Escalation to amber at 80%+ and red at 95%+ is preserved on the bar** (the same thresholds as Claude / Codex).<br>Note: the accent setting affects only the bar. If you had selected e.g. `Slate` in an earlier build, the bar takes that color from this version on.
- **Border while pinned / always-on, and hide on outside click**: pinning the Copilot window (always show) by clicking the tray icon adds a faint 1px border around it (no border in normal hover display). While shown/pinned by clicking, **clicking outside the window hides it** (clicking inside does not; it does not conflict with tray or right-click-menu operations).
- **Constraints**:
  - Only usage billed directly to the personal account is in scope. Organization / Enterprise-managed Copilot usage does not appear in the personal billing endpoint (it may `403` / `404`).
  - Retrieval may not work for accounts not on the Enhanced Billing Platform.
  - The monthly cap is the plan's known value (or your manual entry). Because a GitHub allotment change could make it stale, the constants are consolidated in one place (`AppSettings`).
  - The reset date is a **calendar-month approximation estimate** (it is not retrievable from the API). If the billing cycle is anchored to the signup date, it can drift from the calendar month. The UI shows it as `This month (calendar-month approx.)` / `This month · est. reset {month}/{day}`.
- **HTTP status handling**: `GITHUB_TOKEN` unset → `Not logged in` / `401` → "invalid or expired" / `403` → "check Plan(Read) permission, personal billing, and Enhanced Billing eligibility" / `429` and Retry-After → `Temporarily rate-limited` / 5xx, timeout, JSON anomaly → `Fetch failed`. When there is no current-month consumption (empty array, no Copilot rows), it shows 0 usage as a normal result.
- **First-time setup (PAT + `GITHUB_TOKEN` environment-variable method)**: "GitHub Copilot settings" has **`First-time setup`** and **`Connection test`** buttons. `First-time setup` opens a wizard that lets you (1) open GitHub's fine-grained PAT creation page in your default browser plus show the **creation steps** at the bottom, (2) show the steps to set the Windows user environment variable `GITHUB_TOKEN` (with a PowerShell example), and (3) run a `Connection test` that tries a fetch with the current `GITHUB_TOKEN`.
  - **Fine-grained PAT creation steps (key points)**:
    1. **Token name**: a clear name (e.g. `TokenChecker`).
    2. **Expiration**: `90 days` recommended (`No expiration` is also fine if you prioritize continuous use).
    3. **Permissions**: `[+ Add permissions]` → add **`Plan`** from the list and select **`Read-only`**.
    4. Press **`Generate token`** to create and copy the token.
    5. Set the copied token to the Windows user environment variable **`GITHUB_TOKEN`** (you do not enter it into the app). Restart TokenCheckerWin afterward.
  - **In-app sign-in / OAuth / Device Flow is not currently implemented.** The token is **never entered into the app and never stored** — it is only read from the `GITHUB_TOKEN` user environment variable.
  - **The connection test does not display the token.** On success it shows only usage numbers (e.g. `Fetched successfully. This month: 4,627 / 7,000 credits  Usage: 66%`); on failure it shows only safe boilerplate (unset / invalid-or-expired / insufficient permission (check Plan=read) / rate-limited / fetch-failed). It never displays, stores, or logs the token, login, URL, path, email, or raw diagnostic strings.
  - After setting the environment variable, an already-running TokenCheckerWin may not pick it up. Restart it if it does not.
  - When `GITHUB_TOKEN` is unset and the Copilot window is ON, the window guides you with `GITHUB_TOKEN is not set` / `See the steps under "First-time setup" in settings` (no token input field is placed in the window).

## Credentials and privacy

- The app never writes tokens, OAuth credentials, full paths, or email addresses to any of the UI, tooltips, logs, or `settings.json`.
- The normal view never shows raw diagnostic strings (`claudeFound=true; versionPresent=true; ...`, `accountNull=false; ...`). They appear only inside `Show details`, and even then they pass through the following masking.
  - Email-like → `<email>`
  - Absolute paths (Windows / POSIX) → `<path>`
  - Values after `token=` / `secret=` / `key=` / `authorization=` / `bearer=` → `<redacted>`
  - Long alphanumeric blobs → `<redacted>`
- `Show details` also includes a single line `[debug] serviceName=...; currentStatus=...; currentWindowCount=...; fallbackStatus=...; fallbackWindowCount=...;`. This lets you tell whether the on-screen ring is the latest value or a fallback without reading raw diagnostics.
- Log-in assistance (the Claude Code / Codex `Log in` buttons, etc.) only launches the official CLI inside a new `cmd.exe`. The app does not read `~/.claude/.credentials.json`, `~/.codex/auth.json`, the Windows Credential Manager, API keys, and so on. It does not store them either. The app writes only these three files:
  - `settings.json` (settings only)
  - `last_usage.json` (numeric usage only; diagnostic `Message` nulled out)
  - `copilot_usage.json` (for GitHub Copilot delta calculation: **numbers and dates only**, such as the target month, last fetch time, current-month used credits, and the 09:00-today window baseline. It does not store tokens, login, URLs, paths, or email addresses. It is created only when the Copilot window is enabled and a fetch succeeds.)
- The Claude Code usage endpoint is unofficial and may change without notice. Treat the displayed Claude usage as best-effort.

## License

This software is distributed under the [MIT License](LICENSE) (Copyright (c) 2026 kesuhiro74).

For the third-party components used / bundled (GitHub Octicons, the .NET runtime), see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
