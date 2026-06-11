# CLAUDE.md

TokenCheckerWin で作業する Claude Code 向けのガイドです。ユーザー向けの網羅的な仕様は `README.md` にあります。ここには作業に効くコマンド・構成・規約・落とし穴だけをまとめます。

## 作業モード（モデル・effort）

- **モデル**: 最新の Claude Opus を使用する（現行の最新は Opus 4.8）。実際の選択は Claude Code の `/model` か `settings.json` の `model` で行うこと。CLAUDE.md からモデルは強制できないため、ここでは方針として明記する。
- **ultracode（xhigh effort + 自動ワークフロー編成）**: 複数ファイルにまたがる変更や横断的な作業では `/effort ultracode` を使う。具体例: 80%/95% 閾値を3箇所で揃える、プロバイダ実装（`ClaudeUsageProvider`/`CodexUsageProvider`）の追加・改修、データフローの変更、プライバシー不変条件（後述）に関わる設計判断。Claude Code v2.1.154 以降が必要。Max/Team では既定でオンのことが多い。
- ルーティンの編集（リネーム・1行修正・文言変更）に ultracode は使わない。重く、レート制限を圧迫する（このアプリが監視している当の対象でもある）。軽作業は `/effort medium`、通常作業は `/effort high` で十分。**ultracode を使った作業が終わったら `/effort high` に戻す。**

## このアプリは何か

Claude Code / OpenAI Codex の使用率（5時間・週次のレート制限）を Windows 通知領域に常駐表示する .NET 10 WinForms トレイアプリ。取得失敗時は前回成功値にフォールバックする。**認証情報・トークンは一切保存しない**（最重要の設計制約。後述）。

## ビルド・実行

リポジトリのルート（`C:\dev\TokenCheckerWin`）から実行すること。`src/...` 相対パスは**ルート以外から叩くと `MSB1009` で落ちる**。

```powershell
# ビルド（ソリューション全体）
dotnet build

# App プロジェクトだけビルド
dotnet build src/TokenChecker.App/TokenChecker.App.csproj -c Debug

# トレイ常駐として起動
dotnet run --project src/TokenChecker.App

# 起動直後にステータス窓 / 設定窓を開く（開発・スクショ・動作確認向け）
dotnet run --project src/TokenChecker.App -- --show-status
dotnet run --project src/TokenChecker.App -- --show-settings

# 使用率を JSON で標準出力に書き出す POC（プロバイダ動作の素早い確認に最適）
dotnet run --project src/TokenChecker.Poc
```

ビルド済み exe を直接起動する場合（`dotnet run` がルート外で失敗する時の回避にも）:

```powershell
& "C:\dev\TokenCheckerWin\src\TokenChecker.App\bin\Debug\net10.0-windows\TokenChecker.exe" --show-status
```

CLI フラグは `Program.cs` で処理: `--show-status` / `--show-settings`（大文字小文字区別なし）。フラグなしは静かにトレイ常駐。

### 落とし穴: 再ビルド前に実行中インスタンスを止める

App が起動したままだと exe がロックされてビルドが失敗する。コード変更→ビルド前に必ず停止する（出力 exe は `TokenChecker.exe`＝プロセス名 `TokenChecker`）:

```powershell
Stop-Process -Name TokenChecker -Force -ErrorAction SilentlyContinue
```

### テスト（`dotnet test` がグリーンであること＝完了条件）

`tests/` に xUnit テストプロジェクトがある（`TokenChecker.Core.Tests`＝`net10.0` / `TokenChecker.App.Tests`＝`net10.0-windows`）。**コミット・リリース前に `dotnet test`（ソリューション全体）が必ずグリーンであること**。GitHub Actions（`.github/workflows/ci.yml`）が push / PR ごとに windows-latest 上で `dotnet test` を実行し、失敗を赤チェックで止める。

```powershell
Stop-Process -Name TokenChecker -Force -ErrorAction SilentlyContinue   # exe ロック回避
dotnet test
```

本スイートはアプリの不変条件を固定している（**80%/95% 閾値**＝`UsageTheme.AccentColor`、**プライバシー masking**＝`DiagnosticMasker`、**Copilot allowance（Free=200 ほか）**＝`AppSettings.CopilotCreditAllowance`、**100%予測/本日9:00増分**＝`CopilotUsageTracker`、**設定マイグレーション**＝`SettingsStore.ApplyLegacyMigrations`、**プロバイダ失敗の分離**＝`UsageAggregator`、**Normalize/Clone**）。これらの領域を変更したらテストを更新し緑を維持する。テスト容易化のための内部シーム（`CopilotUsageStore`/`CopilotUsageTracker` の `internal` ctor、`SettingsStore.ApplyLegacyMigrations` の `internal` 化、App の `InternalsVisibleTo`）以外で本番コードのテスト用改変はしない。

**ローカルゲート（pre-push フック）**: `git push` の直前に `dotnet test` を自動実行し、失敗したら push を中止する。リポジトリの `.githooks/pre-push` がそれを行う。**クローンごとに一度だけ**有効化する:

```powershell
git config core.hooksPath .githooks
```

緊急時のみ `git push --no-verify` でバイパス可（原則使わない）。`TokenChecker.exe` 起動中は exe ロックでビルドが失敗するため、先に `Stop-Process -Name TokenChecker -Force` する。

**Claude セッション用フック（ローカル・任意）**: `.claude/settings.json` の Stop フック（`.claude/hooks/test-gate.ps1`）が、`src/` か `tests/` に未コミット変更があるとき応答完了前に `dotnet test` を自動実行し、失敗したら停止せず修正を促す（変更が無ければ何もしない）。`.claude/` は gitignore 済みでこのフックは各自のローカル設定。新規作成直後は Claude Code で一度 `/hooks` を開くか再起動すると有効化される。

依然として POC + 実起動の目視も併用する（UI 描画・実プロバイダ取得はテスト対象外）。

## プロジェクト構成

- **`src/TokenChecker.Core`** — プラットフォーム非依存の共有ライブラリ。使用率モデル（`UsageSnapshot` / `ServiceUsage` / `RateLimitWindow` / `ProviderStatus`）、`IUsageProvider`、`UsageAggregator`、プロバイダ実装（`Providers/`）。`net10.0`。
- **`src/TokenChecker.App`** — WinForms + `NotifyIcon` のトレイ UI。`net10.0-windows`, `WinExe`, `UseWindowsForms`。Core を参照。
- **`src/TokenChecker.Poc`** — `UsageSnapshot` を JSON で吐くコンソール。プロバイダ挙動の確認用。

### データフロー

`TrayApplicationContext`（`ApplicationContext` 実装、アプリの中枢）がタイマーで `UsageAggregator.CaptureAsync()` を呼ぶ → 各 `IUsageProvider`（`ClaudeUsageProvider` / `CodexUsageProvider`）が `ServiceUsage` を返す → `StatusForm` の UI とトレイアイコン（`TrayIconRenderer`）を更新。アグリゲータは個別プロバイダの例外を握りつぶして `Error` 化するので、1サービスの失敗が全体を巻き込まない。

### プロバイダの要点

- **Claude** (`ClaudeUsageProvider`): 非公式 OAuth usage endpoint (`api.anthropic.com/api/oauth/usage`) を叩く。`~/.claude/.credentials.json`（`CLAUDE_CONFIG_DIR` で上書き可）から access token を**読むだけ**で出力しない。`five_hour`→300分窓、`seven_day`→10080分窓。HTTP 401/403→`Unauthorized`、429→`RateLimited`、5xx/タイムアウト/JSON エラー→`Error`。credentials はあるが token 取得不可→`Error` ではなく `NotLoggedIn`。
- **Codex** (`CodexUsageProvider`): `codex app-server --listen stdio://` を起動し JSON-RPC（`initialize`→`account/read`→`account/rateLimits/read`）。読み取り後 app-server を停止。`account.type != "chatgpt"`（API キー認証など）は使用率取得不可で `Error`。

両プロバイダとも PATH に CLI が無ければ `NotInstalled`（PATH 探索は `CommandLineProbe`、Windows では `.cmd` 等の PATHEXT を考慮）。

## 重要な規約・不変条件

### プライバシー（最重要・絶対に破らない）

- トークン・OAuth 認証情報・メールアドレス・絶対パス全体を、UI・ツールチップ・ログ・保存ファイルのいずれにも**書き出さない**。
- 診断文字列は「詳細を表示」内だけに出し、必ずマスクする（email→`<email>`、path→`<path>`、`token=`/`secret=`/`key=`/`bearer=` 等→`<redacted>`、長い英数字塊→`<redacted>`）。マスクは **`TokenChecker.Core.DiagnosticMasker.Mask(value, maxLength)` に一元化**（唯一の真実）。Core のプロバイダも App の `ProviderStatusPresenter.SafeDiagnostics` もこれに委譲する。マスク規則を変える時はこの1箇所だけを直す（コピーを増やさない）。`maxLength` だけ用途別（プロバイダ要約=160 / UI 詳細=400）。
- アプリが書き込むのは `settings.json`（設定のみ）、`last_usage.json`（数値の使用率のみ。診断 `Message` は `null` にしてから保存）、`copilot_usage.json`（Copilot の月次/9:00 ベースライン追跡。数値と日付のみ。トークン・ログイン・パス・メールは持たない）の3ファイルだけ。資格情報ファイルやクレデンシャルストアへは**書かない**。
- 機能追加でこれらに反する可能性があるときは、実装前にユーザーへ確認する。

### 80% / 95% の閾値は UsageTheme の定数を参照する

警告 `>= 80%` / 危険 `>= 95%` の配色エスカレーション閾値は **`UsageTheme.WarningPercent`（80）/ `UsageTheme.CriticalPercent`（95）に一元化**されており、各呼び出し元はリテラルを複製せずこの定数を参照すること:
- `TrayIconRenderer`（`DetermineState`・`CopilotBarColor`）— `UsageTheme.CriticalPercent` / `WarningPercent` を直接参照
- `StatusForm.UsageAccentColor`（通常/コンパクトの数字・バー）— `UsageTheme.AccentColor()` に委譲
- `StatusForm.BrandUsageColor`（ミニマムのブランド色エスカレーション）— `UsageTheme.BrandUsageColor()` に委譲

**不変条件**: 80/95 の値が他箇所でずれてはならない。定義は `UsageTheme` の1箇所のみ。閾値を変更するときはここだけを直す。

### 設定の永続化

- 保存先: `%APPDATA%\TokenCheckerWin\settings.json`（`SettingsStore`、`JsonStringEnumConverter` 使用）。同フォルダに `last_usage.json`（`LastUsageStore`）と `copilot_usage.json`（`CopilotUsageStore`／`CopilotUsageTracker`、数値・日付のみ）。3ファイルとも `AtomicFile.WriteAllText`（temp→`File.Move(overwrite: true)`）で原子的に書き出し、途中失敗で半端なファイルを残さない。
- `DisplayMode`（`Normal`/`Compact`/`Minimum`）が表示モードの真実。旧 `CompactMode` bool は後方互換でミラー書き出しするだけ。レガシー移行（`CompactMode`→`DisplayMode`）は `SettingsStore.Load` で**JSON にフィールドが無い時だけ**一度行う。`AppSettings.Normalize()` で再移行してはいけない（モード切替が巻き戻る）。理由は `AppSettings.cs` / `SettingsStore.cs` のコメント参照。
- 保存ファイルが壊れていれば既定値で起動（例外を握りつぶす）。永続化失敗でアプリを落とさない。

### コメント・文字列の言語方針

コード内コメント・コミットメッセージは英語。UI 表示文字列は日本語をキーに `Strings.T()` / `Strings.Tf()` 経由で多言語化（直接ハードコードしない）。

### UI（`StatusForm.cs`）

- 表示は3モード: `LayoutNormal()` / `LayoutCompact()` / `LayoutMinimum()`。ボーダレス（`FormBorderStyle.None`）+ ドロップシャドウ（`CS_DROPSHADOW`）。ウィンドウ全体ドラッグ移動（`WM_NCLBUTTONDOWN`+`HTCAPTION`、`LinkLabel`/`TextBox` は除外）、Esc で閉じる（`HideRequested`）。
- ブランド色: Claude=青 `(74,124,232)`、Codex=紫 `(139,92,214)`（トレイの淡色より濃いめ）。
- 角丸ウィンドウは `ApplyRoundedRegion(radius)`、矩形に戻すのは `ClearRegion()`。GDI+ カスタム描画（`OnPaint` + `CreateRoundedRectPath`、`SmoothingMode.AntiAlias`）。
- 既存の内部コントロール（`UsageBarControl` 角丸ピルバー、`UsageRingControl` ドーナツ）を再利用する。新しい描画を足す前に流用を検討。

### UI 変更の検証

- このセッションのデスクトップはヘッドレスで、`Graphics.CopyFromScreen` は黒画像を返すことがある。客観確認は「パネルに対する `DrawToBitmap` + 色ヒストグラム」や、実行中ウィンドウを `GetWindowRect`（P/Invoke）で実寸計測する方法が有効。最終的な見た目はユーザーに目視確認を依頼する。
- テスト用に `settings.json` を一時変更した場合は、確認後に必ず元の値へ復元する。

## Git

- 既定ブランチ `main`。コミット/プッシュはユーザーが明示したときだけ行う。
- コミットメッセージ末尾に付与:
  ```
  Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
  ```
- `bin/` `obj/` `publish/` `.claude/` `.codex/` は `.gitignore` 済み。
