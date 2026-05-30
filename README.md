# TokenCheckerWin

TokenCheckerWin は、Claude Code と OpenAI Codex の使用率を Windows の通知領域から確認できる常駐アプリです。

Claude Code / Codex の 5時間制限・週次制限を取得し、通常モード、コンパクトモード、ミニマムモードで見やすく表示します。取得に失敗した場合でも、前回成功した使用率を表示できます。

## 主な機能

- Claude Code / Codex の使用率表示
- 5時間制限・週次制限の表示
- Windows通知領域への常駐
- 通常 / コンパクト / ミニマムの 3 表示モード
- 使用率のバー / ドーナツ表示(サービスごとのブランド色)
- リセットまでの残り時間表示
- タイトルバーのない角丸フライアウト(どこでもドラッグ移動・Esc で閉じる)
- Claude Code / Codex のログイン補助
- 前回成功値のフォールバック表示
- Windowsログイン時の自動起動設定

## 注意事項

- Claude Code の使用率取得には非公式の usage endpoint を利用しています
- 将来の仕様変更により取得できなくなる可能性があります
- このアプリは認証情報やトークンを保存しません
- Claude Code / Codex のログイン処理は、それぞれの公式 CLI を起動して行います
- Windows SmartScreen の警告が表示される場合があります

## 動作環境

- Windows 11
- VS Code
- .NET 8 SDK 以降の .NET 8 互換 SDK
- Visual Studio は不要

## プロジェクト構成

- `src/TokenChecker.Core`: 使用率のモデル、プロバイダ・インタフェース、アグリゲータ、各プロバイダ実装の共有ライブラリ
- `src/TokenChecker.Poc`: `UsageSnapshot` を JSON で標準出力に書き出すコンソール POC
- `src/TokenChecker.App`: WinForms + `NotifyIcon` の通知領域常駐アプリ

## ビルド

```powershell
dotnet build
```

## POC を実行する

```powershell
dotnet run --project src/TokenChecker.Poc
```

POC は Claude / Codex の `UsageSnapshot` を JSON で出力します。プロバイダの主な挙動は次の通りです。

- `claude` / `codex` が PATH 上に存在するかをまずチェックします。
- Codex プロバイダは `codex app-server --listen stdio://` を起動し、stdin/stdout に JSONL を流して `initialize` → `account/read` → `account/rateLimits/read` を呼びます。読み取り後は app-server プロセスを停止します。
- `rateLimitsByLimitId` の各エントリは汎用的にパースし、`usedPercent` / `windowDurationMins` / `resetsAt` が取得できた場合に `RateLimitWindow` として公開します。
- トークン・認証データ・メールアドレス・パス全体は読み取りも出力もしません。
- CLI が見つからない場合は `NotInstalled` を返します。
- Codex のログイン未完了レスポンスは `NotLoggedIn` として扱います。
- Codex の `account.type` が `chatgpt` でない(API キー認証など)場合、使用率を取得できないことを `Error` として報告します。
- Codex の起動失敗・タイムアウト・JSON/プロトコルエラーは `Error` として報告し、POC 全体は失敗させません。
- Claude Code の使用率取得は非公式の OAuth usage endpoint を利用しています。仕様は予告なく変わる可能性があります。
- Claude Code の OAuth 認証情報は、その endpoint に必要なアクセストークンを取り出すためだけに読み取ります。認証情報 JSON 本文、トークン、メールアドレス、パス全体を出力することはありません。
- Claude Code の `five_hour` は 300 分の `RateLimitWindow`、`seven_day` は 10080 分の `RateLimitWindow` にマッピングします。
- Claude Code usage endpoint の HTTP `401`/`403` は `Unauthorized`、`429` は `RateLimited`、5xx・タイムアウト・JSON エラーは `Error` として報告します。
- `.credentials.json` は存在するものの、その中から有効な access token が取り出せない(例: `/logout` でファイルが空にされた、JSON 構造が想定外など)場合は、`Error` ではなく `NotLoggedIn` として報告します。UI 側は「未ログイン」バッジ + 「Claude Code にログインしてください」のメッセージで案内します。

## VS Code から使う

このフォルダを VS Code で開き、次のいずれかを利用します。

- ターミナル: `dotnet build`
- ターミナル: `dotnet run --project src/TokenChecker.Poc`
- タスク: `dotnet build`
- タスク: `run poc`
- デバッグ構成: `TokenChecker.Poc`

## 通知領域アプリの動作確認

タスクトレイ常駐として起動するには次を実行します。

```powershell
dotnet run --project src/TokenChecker.App
```

起動直後に状態ウィンドウを開いて確認したい場合(開発・スクリーンショット・publish 後の動作確認向け)は `--show-status` を渡してください。

```powershell
dotnet run --project src/TokenChecker.App -- --show-status
.\publish\win-x64\TokenChecker.App.exe --show-status
```

`--show-status` を渡さない場合は、通常通りトレイ常駐として静かに起動します。

設定ダイアログだけを開きたい場合は `--show-settings` を渡します。

```powershell
dotnet run --project src/TokenChecker.App -- --show-settings
```

### 表示モード

設定ダイアログとトレイ右クリックメニューから、次の 3 つの表示モードを切り替えられます。選択値は `settings.json` の `DisplayMode` フィールドに保存されます(旧バージョン向けに `CompactMode` の真偽値も互換のために書き出されます)。

いずれのモードもタイトルバーのない角丸のフライアウトとして表示されます。ウィンドウのどこでもドラッグで移動でき(リンク・テキスト欄を除く)、`Esc` キーまたはトレイアイコンの再クリックで閉じられます。ウィンドウ位置は保存され、次回も同じ場所に開きます。

- **通常モード (Normal)**: Claude Code / Codex のカードを縦に並べる最も詳しい表示です。`5時間` の使用率を大きな数字 + 横長プログレスバーで主役として見せ、その下に `週次` の使用率(数字 + 控えめな細いバー)も表示します。`5時間` のリセットまでの残り時間(例: `あと2時間18分（11:50リセット）`)、状態バッジ、`詳細を表示` リンク(マスク済み診断情報を折りたたみ表示)も備えます。
- **コンパクトモード (Compact)**: Claude Code / Codex を横並びの 2 カードにまとめ、`5時間` のドーナツチャートとバッジ、リセット文だけを表示する省スペースモードです。表示中のサービス数に合わせてウィンドウ幅が詰まり(片方だけ表示なら 1 枚分の幅)、余白を抑えた角丸ウィンドウになります。`最終更新` 行はこのモードでは省略します。
- **ミニマムモード (Minimum)**: 2 サービスを 1 行ずつ(`● サービス名 ──バー── 45%`)積み重ねた最小表示です。各行はサービスのブランド色(Claude=青 / Codex=紫)のドットと細い横長バーで使用率を示し、使用率に応じて 80% 以上で橙、95% 以上で赤にエスカレーションします。週次・診断・リセット時刻はこのモードでは省略します。

### 通知領域アイコン

- 外部画像を持たず、起動時にコードでアイコンを生成します。外側リングが Claude Code、内側リングが Codex、中央の `T` グリフがアプリ識別子です。
- それぞれのリングは最大 `usedPercent` の値に応じて時計回りに伸び、トレイを一目見るだけで最も逼迫しているウィンドウの目安が分かります。
- 全体最悪値に応じて配色が変わります。
  - 通常 (`< 80%`): 青の外リング + 紫の内リング
  - 警告 (`>= 80%`): アンバー
  - 危険 (`>= 95%`): 赤
  - エラー (両プロバイダが `NotInstalled` / `NotLoggedIn` / `Unauthorized` / `RateLimited` / `Error`): くすんだ赤
  - 取得前 / データなし: ライトグレー
- マウスオーバーのツールチップも日本語化されており、サービスごとに日本語のステータスバッジ、または `5h X% / Weekly Y%` を表示します。

### Claude Code / Codex のログイン補助

このアプリは認証情報を保存しません。ログイン処理はすべて公式 CLI に委ねます。

- トレイ右クリックメニューに次の項目があります。
  - `今すぐ更新`
  - `表示モード` ▶(通常 / コンパクト / ミニマム)
  - `設定`
  - `Claude Code にログイン` / `Claude Code からログアウト`
  - `Codex にログイン` / `Codex からログアウト`
  - `認証状態を再確認`
  - `終了`
- `Claude Code にログイン` を選ぶと、新しい `cmd.exe` を開いて `claude` CLI を起動します。プロンプトで `/login` と入力してログインを完了し、その後に `認証状態を再確認` を押してください。`Claude Code からログアウト` も同じく `claude` を開き、ユーザーが `/logout` を入力します。アプリ側で `.credentials.json` や OAuth トークンを読むことはありません。
- `Codex にログイン` を選ぶと、新しい `cmd.exe` で `codex login` を実行します。ブラウザで ChatGPT サインインを完了したあと、`認証状態を再確認` を押してください。`codex logout` も同じく実行できます。Codex の使用率取得は ChatGPT ログインを前提としており、API キー認証では使用率を取得できません。
- `認証状態を再確認` は単に使用率取得を再実行します。ログインが成功していればステータスが `正常取得` に切り替わり、リング表示も更新されます。
- 設定ダイアログには `ログイン状態` セクションがあり、`Claude Code` / `Codex` の現在のステータス(`正常` / `未ログイン` / `CLI未検出` / `認証エラー` / `取得を一時制限中` / `取得失敗`)と、サービス別の `ログイン` / `ログアウト` ボタン、共通の `認証状態を再確認` ボタンが置かれています。
- CLI が PATH に見つからない場合は `Claude Code CLI が見つかりません` / `Codex CLI が見つかりません` とメッセージを出すだけで、プロセスは起動しません。

### ステータスバッジとメッセージ

- ステータスバッジは日本語表示(`正常取得` / `未インストール` / `未ログイン` / `認証エラー` / `取得を一時制限中` / `取得失敗` / `状態不明`)です。`ProviderStatus` enum の英名はそのまま画面に出しません。`取得を一時制限中` は usage endpoint 自体が HTTP `429` を返したときだけ表示されます(LLM 自体のクォータ超過ではないことに注意)。
- バッジ下の本文は短いユーザ向け文(例: `Claude Code の使用率を取得できています`)を表示します。`claudeFound=...; usageApi=...` のような生診断文字列は通常表示には出しません。
- 各カードの `詳細を表示` / `詳細を隠す` リンクから、トラブルシュート用にマスク済みの診断情報を確認できます。

### リセット時刻

- リセットまでの残り時間はローカルタイムで表示します。
- `5時間` ウィンドウ: `あと2時間18分（11:50リセット）`
- `週次` ウィンドウ: `あと3日4時間（5/27 18:00リセット）`

### 設定と保存ファイル

- 設定は現在ユーザーの `AppData` フォルダ配下に保存され、アプリ再起動後も維持されます。
- `settings.json` には更新間隔、自動起動設定、表示対象サービス、状態ウィンドウの位置、`DisplayMode`(と後方互換のための `CompactMode`)だけを保存します。
- 設定ファイルが破損していた場合は、デフォルト設定で起動します。
- 状態ウィンドウの位置は移動・閉じる後も復元されます。保存された位置が現在のモニタ構成外にある場合は、表示領域内に補正して開きます。
- 設定で Claude Code / Codex の表示・非表示を切り替えられます。両方非表示にしてもアプリは常駐し、トレイアイコン / メニューは利用できます。
- Windows ログイン時の自動起動は、現在ユーザーの Run キーにある `TokenCheckerWin` の値で管理します。publish 済みビルドは publish 済み exe のパスを登録し、開発中の `dotnet` 実行では `dotnet "<app dll>"` 形式にフォールバックします。
- 更新間隔は `30秒` / `1分` / `5分` / `10分` から選択できます。

### 前回成功値のフォールバック

- 取得に失敗しても、サービスごとに最後に成功した使用率を保持しているため、Claude / Codex の片方が一時的に失敗してももう片方の前回値は消えません。
- 最後に成功した Claude / Codex の使用率は、`settings.json` と同じ AppData フォルダの `last_usage.json` にも永続化されます。起動直後に Anthropic 側で `HTTP 429` を引いた場合(例: 同じ usage endpoint に対して POC を直前に実行した直後など)でも、前回の使用率がドーナツに残ったまま表示されます。
- 失敗時のバッジには現在のステータス(`取得を一時制限中` / `取得失敗` など)が出て、本文には `一時的に取得できません。前回成功値を表示しています` と表示されます。次回の取得が成功するとドーナツが更新され、永続化スナップショットも上書きされます。
- `last_usage.json` にはサービスごとの `Status` / `Windows` / `CapturedAtUtc` の数値だけを保存し、プロバイダの診断 `Message` 文字列は `null` にクリアしてから書き出します。トークン・認証情報・メールアドレス・パスは一切保存しません。

### その他の挙動

- `今すぐ更新` を連打しても重複した更新は走らないようロックされています。
- 更新中にアプリを終了するとトレイアイコンも消え、プロセスが残りません。
- Claude CLI 診断で `claude --version` がタイムアウトした場合、子プロセスは Kill されます。

## 認証情報とプライバシー

- アプリは UI・ツールチップ・ログ・`settings.json` のいずれにも、トークン・OAuth 認証情報・パス全体・メールアドレスを書き出しません。
- 通常表示には生の診断文字列(`claudeFound=true; versionPresent=true; ...`、`accountNull=false; ...`)を出しません。`詳細を表示` の中だけに表示し、しかも次のマスク処理を通します。
  - メールアドレス風 → `<email>`
  - 絶対パス(Windows / POSIX) → `<path>`
  - `token=` / `secret=` / `key=` / `authorization=` / `bearer=` の値 → `<redacted>`
  - 長い英数字の塊 → `<redacted>`
- `詳細を表示` には `[debug] serviceName=...; currentStatus=...; currentWindowCount=...; fallbackStatus=...; fallbackWindowCount=...;` の 1 行も含まれます。これにより、画面のリングが今回取得値かフォールバック値かを生診断文字列を見ずに判定できます。
- ログイン補助(`Claude Code にログイン`、`Codex にログイン` など)は、公式 CLI を新しい `cmd.exe` 内で起動するだけです。アプリは `~/.claude/.credentials.json`、`~/.codex/auth.json`、Windows 資格情報マネージャー、API キーなどを読みません。保存も行いません。書き込むのは `settings.json`(設定のみ)と `last_usage.json`(数値の使用率のみ)だけです。
- Claude Code usage endpoint は非公式で、予告なく変更される可能性があります。表示される Claude の使用率はベストエフォートとして扱ってください。

## 動作確認(本リポジトリでの履歴)

2026-05-23 に次のコマンドで動作確認を行いました。

```powershell
dotnet build
dotnet run --project src/TokenChecker.Poc
```

観測結果:

- `dotnet build` は成功
- `dotnet run --project src/TokenChecker.Poc` は成功
- Claude は `NotInstalled` を報告
- Codex CLI は検出されたがログインしていない環境のため `NotLoggedIn` を報告

Codex にログイン済みの環境で期待される結果:

- Codex が `Available` を報告
- `UsageSnapshot.Services[].Windows` に少なくとも 1 つの Codex `RateLimitWindow` が含まれる

その他の制約:

- POC は Codex の `chatgptAuthTokens` を直接読みません。
- 出力には意図的にトークン・認証データ・メールアドレス・パス全体を含めません。

## GitHub Release 用説明文

GitHub Release を作成する際の本文として、以下をそのまま貼り付けて利用できます。

```
TokenCheckerWin v0.2.1

Claude Code と Codex の使用率(5時間制限・週次制限)を Windows 通知領域から確認できる常駐アプリです。

主な機能:
- Claude Code / Codex の使用率をトレイアイコンとフライアウトに表示
- 用途に合わせて選べる 3 表示モード
  - 通常: 5時間を主役に、週次の細いバーも添えた詳細表示
  - コンパクト: 5時間のドーナツを横並びにした省スペース表示
  - ミニマム: サービスのブランド色バーで使用率だけを示す最小表示
- タイトルバーのない角丸フライアウト(どこでもドラッグ移動・Esc で閉じる)
- リセットまでの残り時間表示と、取得失敗時の前回成功値フォールバック
- Windows ログイン時の自動起動設定

使い方:
1. zip ファイルをダウンロードして展開します
2. TokenChecker.App.exe を起動します
3. Windows 通知領域のアイコンをクリックすると使用率を確認できます
4. 未ログインの場合は、右クリックメニューから Claude Code / Codex にログインしてください

注意事項:
- Claude Code の使用率取得には非公式の usage endpoint を利用しています
- 将来の仕様変更により取得できなくなる可能性があります
- このアプリは認証情報やトークンを保存しません
- ログイン処理は公式 CLI (claude / codex) を起動して行います
- Windows SmartScreen の警告が表示される場合があります
```

リポジトリの GitHub description(About 欄)には、以下を設定することを推奨します。

```
Claude Code / Codex の5時間・週次使用率をWindowsタスクトレイに常駐表示するアプリ(通常/コンパクト/ミニマムの3表示モード)
```
