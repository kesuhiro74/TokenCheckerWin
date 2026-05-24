# TokenCheckerWin

Windows notification area app for checking Claude Code and OpenAI Codex usage and rate-limit state.

This repository currently contains a console proof of concept and a minimal WinForms notification area app.

## Requirements

- Windows 11
- VS Code
- .NET 8 SDK or newer .NET 8 compatible SDK
- Visual Studio is not required

## Projects

- `src/TokenChecker.Core`: shared usage models, provider interfaces, aggregator, and usage providers
- `src/TokenChecker.Poc`: console POC that writes a `UsageSnapshot` JSON document to stdout
- `src/TokenChecker.App`: minimal WinForms + `NotifyIcon` tray app

## Build

```powershell
dotnet build
```

## Run The POC

```powershell
dotnet run --project src/TokenChecker.Poc
```

The POC prints a JSON `UsageSnapshot` for Claude and Codex. Current provider behavior:

- They check whether `claude` or `codex` appears to be available on `PATH`.
- The Codex provider starts `codex app-server --listen stdio://`, sends JSONL requests over stdin/stdout, and stops the app-server process after the read attempt.
- The Codex provider calls `initialize`, `account/read`, and `account/rateLimits/read`.
- Codex `rateLimitsByLimitId` entries are parsed generically. `usedPercent`, `windowDurationMins`, and `resetsAt` are exposed in `RateLimitWindow` when available.
- They do not read or print tokens, authentication data, email addresses, or local paths.
- Missing CLIs are reported as `NotInstalled`.
- Codex login-required responses are reported as `NotLoggedIn`.
- Codex TUI startup does not guarantee usage collection is available. The app-server `account/read` result must be a ChatGPT account (`account.type == "chatgpt"`); API-key or unknown account modes cannot provide rate-limit usage for this POC.
- Codex app-server startup failures, timeouts, and JSON/protocol failures are reported as `Error` without failing the whole POC.
- Claude CLI and `.credentials.json` presence are detected safely, but Claude usage API collection is not implemented yet.
- Claude usage collection uses an undocumented OAuth endpoint that may change without notice.
- Claude OAuth credentials are read only to extract the access token needed for that endpoint; credential JSON, tokens, email addresses, and full local paths are never printed.
- Claude `five_hour` usage is mapped to a 300-minute `RateLimitWindow`; `seven_day` usage is mapped to a 10080-minute `RateLimitWindow` when present.
- Claude usage endpoint `401`/`403` is reported as `Unauthorized`, `429` as `RateLimited`, and 5xx/timeouts/invalid JSON as `Error`.

## VS Code

Open this folder in VS Code, then use:

- Terminal: `dotnet build`
- Terminal: `dotnet run --project src/TokenChecker.Poc`
- Task: `dotnet build`
- Task: `run poc`
- Debug configuration: `TokenChecker.Poc`

## Current Verification

Verification was run in this environment on 2026-05-23:

```powershell
dotnet build
dotnet run --project src/TokenChecker.Poc
```

Observed result:

- `dotnet build` succeeded.
- `dotnet run --project src/TokenChecker.Poc` succeeded.
- Claude was reported as `NotInstalled`.
- Codex CLI was detected, but this environment was not logged in, so Codex was reported as `NotLoggedIn`.

Expected result in a Codex logged-in environment:

- Codex is reported as `Available`.
- `UsageSnapshot.Services[].Windows` contains at least one Codex `RateLimitWindow`.

Current constraints:

- The POC does not use Codex `chatgptAuthTokens`.
- Output intentionally avoids tokens, authentication data, email addresses, and full local paths.

## Manual Tray App Checks

Run the tray app:

```powershell
dotnet run --project src/TokenChecker.App
```

Manual checks:

- The app starts without showing a main window.
- A notification area icon appears.
- Left-clicking the icon shows one small status window; repeated left-clicks focus the same window.
- Closing the status window hides it without exiting the app.
- Right-clicking the icon opens a menu with `今すぐ更新` and `終了`.
- Right-clicking the icon opens `設定`.
- The settings window can change refresh interval between `30秒`, `1分`, `5分`, and `10分`.
- The settings window can toggle Windows login startup.
- Settings are saved under the current user's AppData folder and survive app restarts.
- If the settings file is damaged, the app starts with default settings.
- The status window position is restored after it has been moved and closed.
- If the saved status window position is outside the current monitor layout, the window opens inside the visible work area.
- Claude and Codex visibility can be toggled in settings.
- Turning both service cards off leaves the app running and keeps the tray icon/menu usable.
- Toggling Windows login startup adds or removes the `TokenCheckerWin` value under the current user's Run key.
- Published app builds should register the published executable path for startup; development `dotnet` runs fall back to a `dotnet "<app dll>"` command.
- `settings.json` contains only refresh interval, startup preference, visible services, and the status window position.
- The status window uses compact Claude and Codex cards.
- Codex usage is shown with large percentages for the `5h` and `Weekly` windows when those durations are present.
- Codex `5h` and `Weekly` usage windows show lightweight donut rings with the percentage centered in each ring.
- Donut rings use muted color for missing values, warning color at 80% or higher, and danger color at 95% or higher.
- Reset timing is shown as remaining time such as `45m`, `3h`, or `2d`.
- Claude `NotInstalled`, `NotLoggedIn`, `Error`, and `Available` states remain readable in the compact card.
- Repeated `今すぐ更新` clicks do not start overlapping updates.
- Exiting during an update removes the tray icon and does not leave an app process behind.
- If a refresh fails after a successful refresh, the last successful service values are retained per service, so a temporary Claude or Codex failure does not erase the other service's last known windows.
- Claude CLI diagnostics terminate the `claude --version` child process if the version check times out.
