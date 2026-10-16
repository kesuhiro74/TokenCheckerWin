# CLAUDE.md

TokenCheckerWin で作業する Claude Code 向けのガイドです。ユーザー向けの網羅的な仕様は `README.md` にあります。ここには作業に効くコマンド・構成・規約・落とし穴だけをまとめます。

## 作業モード（モデル・effort）

- **モデル**: モデルと effort の既定はユーザー全体設定（`~/.claude/CLAUDE.md` と `settings.json`）に従う。CLAUDE.md からモデルは強制できない。
- **ultracode（xhigh effort + 自動ワークフロー編成）**: 複数ファイルにまたがる変更や横断的な作業では `/effort ultracode` を使う。具体例: 80%/95% 閾値を3箇所で揃える、プロバイダ実装（`ClaudeUsageProvider`/`CodexUsageProvider`）の追加・改修、データフローの変更、プライバシー不変条件（後述）に関わる設計判断。
- ルーティンの編集（リネーム・1行修正・文言変更）に ultracode は重すぎ、レート制限を圧迫する（このアプリが監視している当の対象でもある）。軽作業は `/effort medium`、通常作業は `/effort high` で十分。effort の切替はユーザーが行うので、作業規模と合っていないと判断したら切替を提案する（ultracode での作業が終わったら `/effort high` に戻すよう促す）。

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

本スイートはアプリの不変条件を固定している（**80%/95% 閾値**＝`UsageTheme.AccentColor`、**プライバシー masking**＝`DiagnosticMasker`、**Copilot allowance（Free=200 ほか）**＝`AppSettings.CopilotCreditAllowance`、**100%予測/本日9:00増分**＝`CopilotUsageTracker`、**Copilot 現在期間ベースライン（プラン変更時の手動補正）**＝`CopilotBaseline`、**設定マイグレーション**＝`SettingsStore.ApplyLegacyMigrations`、**プロバイダ失敗の分離**＝`UsageAggregator`、**金額表記**＝`DailyCostText`、**モデル別内訳の正規化・上位5件＋その他**＝`CostBreakdown`、**追加情報の行**＝`ServiceExtrasText`、**フッター合計の表示条件**＝`MonthlyTotal`、**Extras の永続化と Message 除去**＝`LastUsageStore`、**Normalize/Clone**）。これらの領域を変更したらテストを更新し緑を維持する。テスト容易化のための内部シーム（`CopilotUsageStore`/`CopilotUsageTracker`/`LastUsageStore` の `internal` ctor、`SettingsStore.ApplyLegacyMigrations` の `internal` 化、App の `InternalsVisibleTo`）以外で本番コードのテスト用改変はしない。

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

- **Claude** (`ClaudeUsageProvider`): 非公式 OAuth usage endpoint (`api.anthropic.com/api/oauth/usage`) を叩く。`~/.claude/.credentials.json`（`CLAUDE_CONFIG_DIR` で上書き可）は**1リフレッシュ1回だけ読み**、access token（出力しない）と `subscriptionType` / `rateLimitTier`（プラン名用。`ClaudePlanLabel`）のみ取り出す。HTTP 401/403→`Unauthorized`、429→`RateLimited`、5xx/タイムアウト/JSON エラー→`Error`。credentials はあるが token 取得不可→`Error` ではなく `NotLoggedIn`。
  - レスポンスのパースは `ClaudeUsageResponseParser`（純粋関数・テスト対象）に集約。`five_hour`→300分窓、`seven_day`→10080分窓を優先し、欠けていれば `limits[]` の `kind=session` / `weekly_all` で補う。`limits[]` の `weekly_scoped`（ラベルは `scope.model.display_name`→`scope.surface.display_name`）は `Extras.ScopedLimits` に入れ、**`Windows` には絶対に混ぜない**（App の `FindWeeklyWindow` が `WindowDurationMins == 10080` の先頭を週次として拾うため）。scoped が無い時のみ旧 `seven_day_opus`/`seven_day_sonnet` を使う。追加課金は `spend.enabled`（`amount_minor / 10^exponent`）→ `extra_usage.is_enabled`（セント単位）の順。難読化キー（`iguana_necktie` 等）は不安定なので**使わない**。
  - 429 は `Retry-After`（秒数または HTTP 日付）を `ClaudeRetryAfter` で解釈し、その時刻まで HTTP を打たず `RateLimited` を返す。ヘッダ無しは既定5分・上限60分、**メモリのみ**で永続化しない。
- **Codex** (`CodexUsageProvider`): `codex app-server --listen stdio://` を起動し JSON-RPC（`initialize`→`account/read`→`account/rateLimits/read`、params は `{excludeResetCreditDetails: false}`）。読み取り後 app-server を停止。`account.type != "chatgpt"`（API キー認証など）は使用率取得不可で `Error`。`account.planType` のみプラン名に使い（`CodexPlanLabel`）、**email など他の account フィールドは一切読まない**。クレジット残高・individualLimit・リセット券（`rateLimitResetCredits`）は `CodexRateLimitsExtrasParser` が `Extras` に詰める（id/title/description は保持しない）。`account/rateLimitResetCredit/consume` は券を消費するので**絶対に呼ばない**。
- **使用状況の付加情報** (`ServiceExtras`): `ServiceUsage.Extras`（既定 null、省略可なので旧 `last_usage.json` もそのまま読める）。中身はプラン表示名・モデル別週次枠・追加課金・Codex のクレジット/リセット券で、**数値・日付・表示名のみ**（トークン・email・アカウントID・パスは持たない）。`LastUsageStore` は Extras も保存・復元する（取得失敗時のフォールバック表示でもプラン名などが出る）。保存するのは数値・日付・表示名だけで、診断 `Message` は従来どおり `null` にしてから書く。

両プロバイダとも PATH に CLI が無ければ `NotInstalled`（PATH 探索は `CommandLineProbe`、Windows では `.cmd` 等の PATHEXT を考慮）。

### 金額表示（通常モードの「Today ¥n / This month ¥n」とフッター合計）

- **Core** `LocalCost/`: `ClaudeDailyCostReader`（`~/.claude/projects/**/*.jsonl`、`CLAUDE_CONFIG_DIR` 上書き可）と `CodexDailyCostReader`（`~/.codex/sessions/YYYY/MM/DD/rollout-*.jsonl`、`CODEX_HOME` 上書き可）がトークン数を集計し、`ModelPricing`（**単価の唯一の真実**。USD/MTok、decimal 演算のみ、最長前方一致）でコスト化。ログからは**数値・モデル名・重複排除用 ID のみ**抽出し、本文・cwd・パスは保持しない。追記中ファイル対策で `FileShare.ReadWrite | Delete` で開く（`File.ReadLines` 禁止）。
- **複数区間の1回走査**: `ComputeMany(dir, IReadOnlyList<CostInterval>)` が UTC 半開区間ごとの結果を**1回のファイル走査**で返す（当日＋当月をまとめて出す用途。戻り値は引数と同順）。単一区間の `Compute` はそのラッパー。重複排除は走査全体で1つの集合（同一イベントは含まれる各区間で1回ずつ計上されるので、当日⊆当月が常に成立）、LastWriteTime 足切りと Codex の日付ディレクトリ走査は**最も早い開始／最も遅い終了**を使う。
- `DailyCostResult.CostByModelUsd` がモデル id → USD の内訳（キーはログ上の生のモデル id）。単価不明モデルは従来どおり `UnknownModelEvents` のみ加算され内訳にも合計にも入らないので、内訳の合計は常に `CostUsd` と一致する。
- **為替**: `UsdJpyRateProvider` が `api.frankfurter.dev`（USD→JPY）を**1日1回**取得。失敗時は 150 円フォールバック（30分後に再試行）。キャッシュは**メモリのみ・永続化しない**。
- **App**: `DailyCostService`（当日＋当月を `ComputeMany` で1回走査、TTL 10分、手動更新で強制再計算、**例外を一切漏らさず** `DailyCostsView.Empty` に集約＝使用率表示を巻き込まない）→ `StatusForm.UpdateSnapshot` 第3引数 `DailyCostsView`（サービスごとに `ServiceCostView{Today, Month, MonthByModel}`）。
- **金額文字列の唯一の真実は `DailyCostText`**（`Amount` / `MinimumCost` / `FooterTotal` / `MonthBreakdownTooltip`）。日本語は ¥（N0）、英語は $（N2、InvariantCulture）。他所で通貨記号や書式を組み立てない。
- モデル別内訳は `CostBreakdown`（純粋関数）: 生のモデル id から末尾の日付サフィックス（`-20250514`）を落として合算し、降順・上位5件＋「その他」。money 行の ToolTip に出す。
- 表示先: 通常モードカードの**金額行**（`Today` / `This month`＝ユーザー希望で両言語とも英語固定、ラベルは補助色・金額は Segoe UI Semibold 11pt）と**ウィンドウ下端のフッター合計**（表示中サービスの当月合計＝`MonthlyTotal.Sum`。どちらも不明なら非表示）。ミニマムモードは行末の `Cost` ラン1つに「¥1,234 <nf-md-calendar_today> / ¥23,456 <nf-md-calendar_month>」（金額はアイコンで区別。当月不明なら今日の分だけ。`/` は Cost ランで Separator ではない）。**Compact とトレイのツールチップには出さない。**
- 確認: `dotnet run --project src/TokenChecker.Poc -- --daily-cost`（日付・数値・モデル名のみの JSON。当日と当月、モデル別内訳を出力）。

## 重要な規約・不変条件

### プライバシー（最重要・絶対に破らない）

- トークン・OAuth 認証情報・メールアドレス・絶対パス全体を、UI・ツールチップ・ログ・保存ファイルのいずれにも**書き出さない**。
- 診断文字列は「詳細を表示」内だけに出し、必ずマスクする（email→`<email>`、path→`<path>`、`token=`/`secret=`/`key=`/`bearer=` 等→`<redacted>`、長い英数字塊→`<redacted>`）。マスクは **`TokenChecker.Core.DiagnosticMasker.Mask(value, maxLength)` に一元化**（唯一の真実）。Core のプロバイダも App の `ProviderStatusPresenter.SafeDiagnostics` もこれに委譲する。マスク規則を変える時はこの1箇所だけを直す（コピーを増やさない）。`maxLength` だけ用途別（プロバイダ要約=160 / UI 詳細=400）。
- アプリが書き込むのは `settings.json`（設定のみ）、`last_usage.json`（数値の使用率と `ServiceExtras`＝プラン表示名・モデル別週次枠・追加課金・クレジットの**数値／日付／表示名のみ**。診断 `Message` は `null` にしてから保存）、`copilot_usage.json`（Copilot の月次/9:00 ベースライン追跡。数値と日付のみ。トークン・ログイン・パス・メールは持たない）の3ファイルだけ。資格情報ファイルやクレデンシャルストアへは**書かない**。日次コストの為替レート・集計結果も**永続化しない**（メモリのみ）。
- ローカルのセッションログ（`~/.claude/projects` / `~/.codex/sessions`）は**読むだけ**。抽出するのは数値・モデル名・重複排除用 ID のみで、会話本文・cwd・パスを変数に保持・出力しない（`LocalCost/` 各 Reader のコメント参照）。
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
- **通常モードカード（`ServiceCard`）の縦位置は `LayoutRows()` の1か所だけが決める**。行順はタイトル（＋プラン名ピル＋右端ステータスバッジ）→ ステータスメッセージ（任意）→ 5時間ブロック → 週次ブロック → モデル別週次枠の行（任意）→ 区切り線 → 金額行 → 追加情報の行（任意）→ 詳細トグル → 詳細ボックス（展開時）。非表示の行は詰め、カード高さは可視行の合計として自分で決める（固定高さ定数は持たない）。高さが変わったら `HeightChanged` でウィンドウが追従する。新しい行を足すときは `LayoutRows()` に1行足すだけで済む形を保つ。
- プラン名ピルと区切り線は子コントロールではなく `ServiceCard.OnPaint` で描く（`UserPaint` パネル上の透過子コントロールを増やさないため）。ピルはステータスバッジと重なる場合は描画しない。
- 金額行の ToolTip は当月のモデル別内訳＋「API換算の推定額」注記、フッター合計の ToolTip は「実際の請求額ではない」注記。

### UI 変更の検証

- このセッションのデスクトップはヘッドレスで、`Graphics.CopyFromScreen` は黒画像を返すことがある。客観確認は「パネルに対する `DrawToBitmap` + 色ヒストグラム」や、実行中ウィンドウを `GetWindowRect`（P/Invoke）で実寸計測する方法が有効。最終的な見た目はユーザーに目視確認を依頼する。
- テスト用に `settings.json` を一時変更した場合は、確認後に必ず元の値へ復元する。

## Git

- 既定ブランチ `main`。コミット/プッシュはユーザーが明示したときだけ行う。
- コミットメッセージ末尾には、Claude Code が指示する `Co-Authored-By:` 行（実行中のモデル名入り）を付与する。
- リリースは Claude が最後まで行う: 版上げ、README（日英）更新、リリースノート（英日併記）、publish/zip、GitHub Release 作成まで。権限チェックで止まった工程があれば、どこで止まったかをユーザーに報告する。
- `bin/` `obj/` `publish/` `.claude/` `.codex/` は `.gitignore` 済み。
