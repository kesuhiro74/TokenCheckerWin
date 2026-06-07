# TokenCheckerWin

TokenCheckerWin は、Claude Code と OpenAI Codex と GitHub Copilot の AI Credits の使用率を Windows の通知領域から確認できる常駐アプリです。

Claude Code / Codex の 5時間制限・週次制限を取得し、通常モード、コンパクトモード、ミニマムモードで見やすく表示します。取得に失敗した場合でも、前回成功した使用率を表示できます。

## 画面例

<img width="412" height="103" alt="image" src="https://github.com/user-attachments/assets/1397f163-3639-4ab2-814c-ea6a6003d940" /><br>
<img width="297" height="202" alt="image" src="https://github.com/user-attachments/assets/f4f1deac-3f45-4102-8fbb-5d6d49b028c0" />
<img width="295" height="201" alt="image" src="https://github.com/user-attachments/assets/ec29c47d-cecb-400b-b90a-e420b452beb2" />

## 主な機能

- Claude Code / Codex の使用率表示
- 5時間制限・週次制限の表示
- Windows通知領域への常駐
- 通常 / コンパクト / ミニマムの 3 表示モード
- 使用率のバー / ドーナツ表示(サービスごとのブランド色)
- リセットまでの残り時間表示
- タイトルバーのない角丸フライアウト(どこでもドラッグ移動・Esc で閉じる)
- Claude Code / Codex のログイン補助
- GitHub Copilot の AI Credits 当月消費表示(オプトイン・専用ウィンドウ)
- 前回成功値のフォールバック表示
- ライト / ダーク / システム連動のテーマ(Windows の色モードに追従)
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
- .NET 10 SDK 以降の .NET 10 互換 SDK
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

GitHub Copilot の AI Credits プロバイダだけを確認したい場合は `--github-copilot` を渡します。`--raw` を併せると、候補エンドポイント(`ai_credit/usage` → `usage` → `premium_request/usage`)を叩いて、HTTP ステータスとマスク済みの `usageItems` フィールド(`product` / `sku` / `unitType` / `quantity` / `grossQuantity` / `grossAmount` / `netQuantity` / `netAmount` / `copilot`)だけを出力します(レスポンス本文・login・トークンは出力しません)。`GITHUB_TOKEN` が必要です。

```powershell
dotnet run --project src/TokenChecker.Poc -- --github-copilot
dotnet run --project src/TokenChecker.Poc -- --github-copilot --raw
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
.\publish\win-x64\TokenChecker.exe --show-status
```

引数なしで起動した場合は、設定画面で選択した各ウィンドウの表示方法(`常時表示` / `ホバー表示`)に従って起動します(`常時表示` のウィンドウは起動時に表示され、`ホバー表示` のウィンドウは対象トレイアイコンのホバーで表示されます)。`--show-status` は、起動時に Claude / Codex ステータスウィンドウを明示的に表示する補助オプションです。

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

- トレイ右クリックメニューは次の **5 項目** だけです（どのトレイアイコンを右クリックしても同じメニュー。両ウィンドウ OFF 時のコントロール用アイコンでも同じです）。
  - `今すぐ更新` — 使用率の再取得（provider が無くてもクラッシュしません）。
  - `Claude/Codexステータス表示モード` ▶(`通常モード` / `コンパクトモード` / `ミニマムモード`) — 現在の `DisplayMode` にチェック。選択すると即時にステータス窓の表示が切り替わり `settings.json` に保存されます。Claude/Codex ウィンドウが OFF のときは無効表示。
  - `GitHubCopilot表示モード` ▶(`常時表示` / `ホバー表示`) — 現在の `CopilotDisplayMode` にチェック。選択すると即時に Copilot ウィンドウの表示方法が切り替わり保存されます（`常時表示`＝有効なら表示・ピンは解除して常時表示に統一、`ホバー表示`＝ピン留め時以外は非表示でトレイアイコンのホバーで表示）。Copilot ウィンドウが OFF のときは無効表示。
  - `設定` — 設定ダイアログを開きます。
  - `終了`。
- ログイン/ログアウト・認証状態の再確認・GitHub Copilot の初回設定/接続テストは、**右クリックメニューには出さず設定ダイアログ側**に集約しています。
- 設定ダイアログの `Claude Code にログイン` を押すと、新しい `cmd.exe` を開いて `claude` CLI を起動します。プロンプトで `/login` と入力してログインを完了し、その後に `認証状態を再確認` を押してください。`Claude Code からログアウト` も同じく `claude` を開き、ユーザーが `/logout` を入力します。アプリ側で `.credentials.json` や OAuth トークンを読むことはありません。
- 設定ダイアログの `Codex にログイン` は、新しい `cmd.exe` で `codex login` を実行します。ブラウザで ChatGPT サインインを完了したあと、`認証状態を再確認` を押してください。`codex logout` も同じく実行できます。Codex の使用率取得は ChatGPT ログインを前提としており、API キー認証では使用率を取得できません。
- 設定ダイアログの `認証状態を再確認` は単に使用率取得を再実行します。ログインが成功していればステータスが `正常取得` に切り替わり、リング表示も更新されます。
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
- `settings.json` には、更新間隔・自動起動設定・テーマ(`ThemeMode`: システム連動 / ライト / ダーク)・表示モード(`DisplayMode`、後方互換のための `CompactMode`)・各ウィンドウの表示 ON/OFF・表示方法(`常時表示` / `ホバー表示`)・Claude / Codex の表示対象サービス・GitHub Copilot のプラン / Custom 上限・配色・各ウィンドウの位置といった**アプリ設定のみ**を保存します。トークン・認証情報・login・URL・パス・メールアドレスは一切保存しません。
- 設定ファイルが破損していた場合は、デフォルト設定で起動します。
- 状態ウィンドウの位置は移動・閉じる後も復元されます。保存された位置が現在のモニタ構成外にある場合は、表示領域内に補正して開きます。
- 設定で Claude Code / Codex の表示・非表示を切り替えられます。両方非表示にしてもアプリは常駐し、トレイアイコン / メニューは利用できます。
- Windows ログイン時の自動起動は、現在ユーザーの Run キーにある `TokenCheckerWin` の値で管理します。publish 済みビルドは publish 済み exe のパスを登録し、開発中の `dotnet` 実行では `dotnet "<app dll>"` 形式にフォールバックします。
- 更新間隔は `30秒` / `1分` / `5分` / `10分` から選択できます。
- **テーマ(ライト / ダーク / システム連動)** を「共通設定」で選べます。`システム連動` は Windows の色モード(設定 > 個人用設定 > 色)に追従します。**反映は起動時のみ**で、変更後は**アプリを再起動**すると反映されます(設定画面にも「(再起動で反映)」と表示)。ウィンドウ(Claude/Codex・GitHub Copilot)と設定ダイアログがダーク/ライトに切り替わります(トレイアイコンの配色は共通)。

### 前回成功値のフォールバック

- 取得に失敗しても、サービスごとに最後に成功した使用率を保持しているため、Claude / Codex の片方が一時的に失敗してももう片方の前回値は消えません。
- 最後に成功した Claude / Codex の使用率は、`settings.json` と同じ AppData フォルダの `last_usage.json` にも永続化されます。起動直後に Anthropic 側で `HTTP 429` を引いた場合(例: 同じ usage endpoint に対して POC を直前に実行した直後など)でも、前回の使用率がドーナツに残ったまま表示されます。
- 失敗時のバッジには現在のステータス(`取得を一時制限中` / `取得失敗` など)が出て、本文には `一時的に取得できません。前回成功値を表示しています` と表示されます。次回の取得が成功するとドーナツが更新され、永続化スナップショットも上書きされます。
- `last_usage.json` にはサービスごとの `Status` / `Windows` / `CapturedAtUtc` の数値だけを保存し、プロバイダの診断 `Message` 文字列は `null` にクリアしてから書き出します。トークン・認証情報・メールアドレス・パスは一切保存しません。

### その他の挙動

- `今すぐ更新` を連打しても重複した更新は走らないようロックされています。
- 更新中にアプリを終了するとトレイアイコンも消え、プロセスが残りません。
- Claude CLI 診断で `claude --version` がタイムアウトした場合、子プロセスは Kill されます。

### GitHub Copilot(AI Credits・オプトイン)

GitHub Copilot は 2026-06-01 から従量課金(AI Credits)へ移行しました。Claude / Codex のようなリアルタイムのレート制限ウィンドウ(`utilization%` + `resets_at`)は無く、個人トークンで取得できるのは **当月の AI Credits 消費量** だけです。月次の上限(同梱クレジット枠)は API では公開されていないため、上限は **設定でプランを選ぶ(または手入力する)** ことでアプリ側が与えます。

- **既定はオフ(オプトイン)** です。設定の「GitHub Copilot 設定」で「GitHub Copilot ウィンドウを表示」を有効にしたときだけ、GitHub の billing endpoint にアクセスします。無効な間は `/user`・billing へ一切アクセスしません(取得処理自体を走らせません)。
- **専用ウィンドウ**(Claude / Codex のステータス窓とは別の角丸フライアウト)に表示します。上部は **Copilot アイコン＋`GitHub Copilot`**(1行目)と**選択中のプラン名**(2行目・小さめ薄め。例: `Copilot Pro` / `Copilot Pro+` / `Copilot Max` / `Custom 7,000 credits`)です。通常時は使用率のみを大きく表示し(例: `66% 使用済み`。`使用済み` は小さめ・薄いグレー)、**メイン表示エリア(`n% 使用済み`)にマウスを乗せる**かキーボードフォーカスすると詳細値(例: `4,627 / 7,000 使用済み`)に切り替わります(メイン表示エリア以外にマウスを置いても切り替わりません)。バーの割合・リセット目安・サブ情報・ウィンドウサイズは通常時と詳細時で変わりません。
- **フォント**: GitHub Copilot ウィンドウの主要表示(タイトル・プラン名・数値・`使用済み`・ステータスバッジ・サブ情報)は、`Moralerspace` を**インストールしている場合に使用**します。**未インストールなら標準フォント(Segoe UI)へ自動でフォールバック**します(フォントは同梱しません・無くても動作し、アプリは落ちません)。
- **追加のサブ情報**: このペースで 100% に到達する予測日(例: `このペースだと 6/21 頃に 100%`、不能時は `予測にはデータ不足`)と、本日 9:00 以降の増分(例: `本日9:00以降 +327 credits（+4.7%）`、基準値が無ければ `未計測`)を小さく表示します。
- **トークンは環境変数 `GITHUB_TOKEN` のみ** から読み取ります(読み取り専用・非保存・非出力)。未設定のときは「未ログイン」として、トークン設定を案内します。fine-grained PAT の場合は **User permissions の「Plan: Read」** が必要です。
- **プラン**(なし / Free=200 / Pro=1,500 / Pro+=7,000 / Max=20,000 / Custom 手入力)を設定で選びます。`なし` のときは使用量(クレジット)だけを表示します。プランを選ぶと上限・残量・割合・バーを表示し、80% 以上で橙・95% 以上で赤にエスカレーションします(Claude / Codex と同じ閾値)。
  - **Copilot Free** は当月の AI Credits 枠(200)に対する使用率メーターのみを表示します。Free のコード補完(インライン候補・2,000/月)やチャット(50/月)のクォータは、個人トークンで読み取れる billing エンドポイントには現れない(補完はクレジットを消費しない)ため、**本アプリでは表示しません**(取得には Copilot の OAuth ログインと非公開 API が必要で、アプリ内ログインは実装しない方針のため)。
- **ウィンドウの表示方法**はウィンドウごとに 2 種類から選べます(Claude / Codex ウィンドウと GitHub Copilot ウィンドウで別々に設定可)。
  - `常時表示`: アプリ起動中は対象ウィンドウを常に表示します(閉じても設定が ON なら再表示できます)。
  - `ホバー表示`: 対象ウィンドウ専用のタスクトレイアイコンにマウスを乗せるとフェードインで表示し、マウスがウィンドウの外に出ると即座に隠れます(アイコン→ウィンドウへの移動では消えません)。トレイアイコンを**クリックするとピン留め**(常時表示)に切り替わります。
- **トレイアイコン**は有効なウィンドウごとに表示します(Claude / Codex はリング、GitHub Copilot は縦%バー)。Copilot の縦%バーはアイコン領域を縦長に使い、当月消費の割合(プラン上限比)を示します(角はやや角張った角丸矩形・80%/95% の色変化は維持)。両ウィンドウとも OFF のときは、設定・終了に到達できるよう操作用アイコンを 1 つだけ表示します。
- **配色**: GitHub Copilot の**カードのバー・トレイ縦%バーの通常色(80% 未満)**を設定で選べます(`グリーン（既定）` / `ブルー` / `スカイ` / `パープル` / `スレート`)。**数値(`n%`)は黒系・`使用済み` は薄いグレーで固定**(配色設定の影響を受けません)。**80% 以上で橙・95% 以上で赤へのエスカレーションはバー側で維持**されます(Claude / Codex と同じ閾値)。<br>※配色設定はバーにのみ反映されます。以前のビルドで `スレート` 等を選んでいた場合、本バージョンからはバーがその色になります。
- **ピン留め / 常時表示中の枠と外クリック非表示**: トレイアイコンのクリックで Copilot ウィンドウをピン留め(常時表示)にすると、外周に薄い 1px の枠線が付きます(通常のホバー表示では枠線なし)。クリックで表示・ピン留めした状態では、**ウィンドウの外をクリックすると非表示**になります(ウィンドウ内クリックでは消えません。トレイ操作・右クリックメニュー操作とは競合しません)。
- **制約**:
  - 個人アカウントに直接課金される利用量のみが対象です。Organization / Enterprise 管理の Copilot 利用量は個人 billing endpoint には現れません(`403` / `404` になり得ます)。
  - Enhanced Billing Platform 対象外のアカウントでは取得できないことがあります。
  - 月次上限はプランの既知値(または手入力)です。GitHub の配分変更で陳腐化し得るため、定数は 1 箇所(`AppSettings`)に集約しています。
  - リセット日は **暦月近似の推定**です(API からは取得できません)。請求サイクルが署名日基準の場合は暦月とずれ得ます。UI は「当月集計・リセット目安(推定)」として表示します。
- **HTTP ステータスの扱い**: `GITHUB_TOKEN` 未設定 → `未ログイン` / `401` → 「無効または期限切れ」/ `403` → 「Plan(Read) 権限・個人課金・Enhanced Billing 対象を確認」/ `429`・Retry-After → `取得を一時制限中` / 5xx・タイムアウト・JSON 異常 → `取得失敗`。当月消費が無い(空配列・Copilot 行なし)場合は使用量 0 として正常表示します。
- **初回設定（PAT + `GITHUB_TOKEN` 環境変数方式）**: 設定の「GitHub Copilot 設定」に **`初回設定`** と **`接続テスト`** ボタンがあります。`初回設定` を押すとウィザードが開き、(1) GitHub の fine-grained PAT 作成ページを既定ブラウザで開く＋下部に**作成手順**を表示、(2) Windows のユーザー環境変数 `GITHUB_TOKEN` 設定手順（PowerShell 例つき）を表示、(3) 現在の `GITHUB_TOKEN` で取得を試す `接続テスト` を行えます。
  - **fine-grained PAT の作成手順（要点）**:
    1. **Token name**: 分かりやすい名前（例: `TokenChecker`）。
    2. **Expiration**: 推奨 `90 days`（継続利用優先なら `No expiration` も可）。
    3. **Permissions**: `[+ Add permissions]` →一覧から **`Plan`** を追加し、**`Read-only`** を選択。
    4. **`Generate token`** を押してトークンを作成・コピー。
    5. コピーしたトークンを Windows のユーザー環境変数 **`GITHUB_TOKEN`** に設定（アプリには入力しません）。設定後 TokenCheckerWin を再起動。
  - **アプリ内ログイン / OAuth / Device Flow は現時点では未実装**です。トークンは**アプリ内に入力させず・保存せず**、ユーザー環境変数 `GITHUB_TOKEN` から読み取るだけです。
  - **接続テストはトークンを表示しません**。成功時は使用量の数値（例: `当月使用量: 4,627 / 7,000 credits`・`使用率: 66%`）のみ、失敗時は安全な定型文（未設定／無効・期限切れ／権限不足（Plan=read 確認）／レート制限中／取得失敗）のみを表示します。token・login・URL・path・email・生の診断文字列は表示・保存・ログ出力しません。
  - 環境変数の設定後、既に起動中の TokenCheckerWin には反映されない場合があります。反映されないときは再起動してください。
  - `GITHUB_TOKEN` 未設定で Copilot ウィンドウが ON のときは、ウィンドウに「`GITHUB_TOKEN が未設定です` / 設定画面の『初回設定』から手順を確認してください」と案内します（ウィンドウ内にトークン入力欄は設けません）。

## 認証情報とプライバシー

- アプリは UI・ツールチップ・ログ・`settings.json` のいずれにも、トークン・OAuth 認証情報・パス全体・メールアドレスを書き出しません。
- 通常表示には生の診断文字列(`claudeFound=true; versionPresent=true; ...`、`accountNull=false; ...`)を出しません。`詳細を表示` の中だけに表示し、しかも次のマスク処理を通します。
  - メールアドレス風 → `<email>`
  - 絶対パス(Windows / POSIX) → `<path>`
  - `token=` / `secret=` / `key=` / `authorization=` / `bearer=` の値 → `<redacted>`
  - 長い英数字の塊 → `<redacted>`
- `詳細を表示` には `[debug] serviceName=...; currentStatus=...; currentWindowCount=...; fallbackStatus=...; fallbackWindowCount=...;` の 1 行も含まれます。これにより、画面のリングが今回取得値かフォールバック値かを生診断文字列を見ずに判定できます。
- ログイン補助(`Claude Code にログイン`、`Codex にログイン` など)は、公式 CLI を新しい `cmd.exe` 内で起動するだけです。アプリは `~/.claude/.credentials.json`、`~/.codex/auth.json`、Windows 資格情報マネージャー、API キーなどを読みません。保存も行いません。アプリが書き込むのは次の 3 ファイルだけです。
  - `settings.json`(設定のみ)
  - `last_usage.json`(数値の使用率のみ。診断 `Message` は `null` 化)
  - `copilot_usage.json`(GitHub Copilot の差分計算用。対象月・最終取得日時・当月使用クレジット・当日 9:00 窓の基準値といった**数値と日付のみ**。トークン・login・URL・パス・メールアドレスは保存しません。Copilot ウィンドウが有効で取得に成功したときだけ作成されます)
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
TokenCheckerWin v0.5.0

Claude Code / Codex の使用率(5時間制限・週次制限)に加え、GitHub Copilot の AI Credits 当月消費も Windows 通知領域から確認できる常駐アプリです。

主な機能:
- Claude Code / Codex の使用率をトレイアイコンとフライアウトに表示
- GitHub Copilot の AI Credits 当月消費を専用ウィンドウ／トレイ縦%バーに表示(オプトイン・環境変数 GITHUB_TOKEN の fine-grained PAT「Plan: Read」を読み取り)
- 用途に合わせて選べる 3 表示モード(すりガラス調のカード表示)
  - 通常: 5時間を主役に、週次の細いバーも添えた詳細表示
  - コンパクト: 5時間のドーナツを横並びにした省スペース表示
  - ミニマム: サービスのブランド色バーで使用率だけを示す最小表示
- 各サービスのブランドに合わせた見出し(✳ Claude Code / </> Codex)
- タイトルバーのない角丸フライアウト(どこでもドラッグ移動・Esc で閉じる)
- 各ウィンドウ(Claude/Codex・GitHub Copilot)の表示方法を「常時表示」または「ホバー表示」から個別に選択
- ライト / ダーク / システム連動のテーマ(Windows の色モードに追従・反映は起動時)
- リセットまでの残り時間表示と、取得失敗時の前回成功値フォールバック
- Windows ログイン時の自動起動設定
- 二重起動を防止(再起動するとすでに常駐しているウィンドウを前面に表示し、設定画面も多重に開きません)
- アプリ専用のアイコンを exe に同梱
- .NET 10 ベース(自己完結版はランタイム同梱のため .NET のインストール不要)

使い方:
1. zip ファイルをダウンロードして展開します
2. TokenChecker.exe を起動します
3. Windows 通知領域のアイコンをクリックすると使用率を確認できます
4. トレイアイコンを右クリックすると、メニューは「今すぐ更新」「Claude/Codexステータス表示モード」「GitHubCopilot表示モード」「設定」「終了」の 5 項目です
5. Claude Code / Codex のログイン・ログアウトや認証状態の再確認、GitHub Copilot の初回設定・接続テストは「設定」画面から行えます(未ログインの場合もここから案内されます)

注意事項:
- Claude Code の使用率取得には非公式の usage endpoint を利用しています
- 将来の仕様変更により取得できなくなる可能性があります
- このアプリは認証情報やトークンを保存しません
- ログイン処理は公式 CLI (claude / codex) を起動して行います
- GitHub Copilot の取得は環境変数 GITHUB_TOKEN(fine-grained PAT)のみを読み取り、トークンは保存・表示しません
- Windows SmartScreen の警告が表示される場合があります
```

リポジトリの GitHub description(About 欄)には、以下を設定することを推奨します。

```
Claude Code / Codex の5時間・週次使用率をWindowsタスクトレイに常駐表示するアプリ(通常/コンパクト/ミニマムの3表示モード)
```

## ライセンス

本ソフトウェアは [MIT License](LICENSE)（Copyright (c) 2026 kesuhiro74）の下で配布されます。

同梱・利用している第三者コンポーネント（GitHub Octicons、.NET ランタイム）の表記は [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) を参照してください。
