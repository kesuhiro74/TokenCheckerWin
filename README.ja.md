# TokenCheckerWin

**[English](README.md)** | 日本語

TokenCheckerWin は、Claude Code・OpenAI Codex・GitHub Copilot の使用率を Windows の通知領域から確認できる常駐アプリです。Claude Code / Codex の 5 時間制限・週次制限と、GitHub Copilot の当月 AI Credits を取得し、見やすいフライアウトで表示します。取得に失敗した場合は、前回成功した値を表示し続けます。

このアプリは**認証情報やトークンを保存しません**([プライバシー](#プライバシー)参照)。Claude Code の使用率は**非公式**エンドポイント経由で取得しているため、将来の Anthropic 側の変更で取得できなくなる可能性があります。未署名ビルドのため Windows SmartScreen の警告が出る場合があります。

## 画面例

<img width="621" height="45" alt="image" src="https://github.com/user-attachments/assets/a6d63a85-d454-4f72-87fd-8038eba6d828" /><br>
<img width="411" height="100" alt="image" src="https://github.com/user-attachments/assets/f3c92fba-9293-4bee-8989-ac2f42566877" /><br>
<img width="386" height="435" alt="image" src="https://github.com/user-attachments/assets/89359c37-8c2d-4eb5-bd60-42439ef531b9" /><br>
<img width="298" height="214" alt="image" src="https://github.com/user-attachments/assets/aae9c329-edac-4b96-8acd-831981e527bb" />
<img width="299" height="217" alt="image" src="https://github.com/user-attachments/assets/6911f5b8-a73d-495f-ae39-8a0963a4b326" />

## 主な機能

- Windows 通知領域に常駐。トレイアイコンは外部画像を持たず、ウィンドウごとに縦%バーをコードで生成します。
- **Claude Code / Codex**: 5 時間制限・週次制限、リセットまでの残り時間、サービスごとのブランド色(Claude=青・Codex=紫)。
- Claude / Codex ウィンドウの 3 表示モード: **通常**(詳細カード)・**コンパクト**(横並びドーナツ)・**ミニマム**(サービスごとに Nerd Font の 1 行ステータス)。
- **本日の推定利用額**: ローカルの Claude / Codex セッションログから当日のトークンを集計し、内蔵のモデル別単価表で金額化します。表示は UI 言語の通貨で — 英語表示では米ドル(`$N.NN (daily)`)、日本語表示では円(`¥N (daily)`。USD→JPY レートは 1 日 1 回、公開為替 API から取得)。通常カードとミニマム行に表示し、算出できないときは非表示にします。
- **GitHub Copilot**: 当月 AI Credits 消費を専用ウィンドウに表示(オプトイン)。本日のバーン・100% 到達予測日・日別消費の棒グラフ・今月のモデル別内訳・追加利用額つき。
- アイコン・数値・バー全体で **80%(橙)/ 95%(赤)** の色エスカレーション。
- タイトルバーのない角丸フライアウト: どこでもドラッグ移動・`Esc` で閉じる・位置を記憶。
- 取得失敗時の前回成功値フォールバック。
- Claude Code / Codex のログイン補助(公式 CLI に委譲)。
- ライト / ダーク / システム連動のテーマと UI 言語(System / English / 日本語)。いずれも再起動で反映。
- Windows ログイン時の自動起動。

## 動作環境

- Windows 11
- .NET 10 SDK(`global.json` で `10.0.100`・`rollForward: latestFeature` を固定)
- VS Code(任意。Visual Studio は不要)

## ビルドと実行

```powershell
dotnet build

# トレイ常駐として起動
dotnet run --project src/TokenChecker.App

# 起動直後にウィンドウを開く(開発・スクリーンショット向け)
dotnet run --project src/TokenChecker.App -- --show-status      # 状態ウィンドウ
dotnet run --project src/TokenChecker.App -- --show-settings    # 設定ダイアログ
```

引数なしのときは、設定で選んだ各ウィンドウの表示方法に従います(`常時表示` は起動時に表示・`ホバー表示` はトレイアイコンのホバーで表示)。`--show-status` / `--show-settings` は、対応するウィンドウを開くだけの補助オプションです(大文字小文字を区別しません)。

自己完結・単一ファイルの publish:

```powershell
dotnet publish src/TokenChecker.App/TokenChecker.App.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
  -p:DebugType=none -o publish/win-x64

.\publish\win-x64\TokenChecker.exe --show-status
```

## 表示と設定

### 表示モード(Claude / Codex)

設定ダイアログまたはトレイメニューから切り替えます。選択値は `settings.json` の `DisplayMode` に保存されます(旧バージョン向けに `CompactMode` 真偽値も互換ミラー書き出し)。いずれのモードも角丸フライアウトで、どこでもドラッグ移動でき(リンク・テキスト欄を除く)、`Esc` で閉じられます。

- **通常 (Normal)** — Claude / Codex のカードを縦に並べる最も詳しい表示。`5時間` の使用率を主役(大きな数字 + 横長バー)とし、ラベル横にリセットまでの残り時間(例: `2時間18分（11:50 リセット）`)、下に `週次`(ラベル横にリセット日時、例: `（6/17 18:00 リセット）`)、本日の推定利用額(`¥46 (daily)`)、状態バッジ、`詳細を表示` リンクを備えます。
- **コンパクト (Compact)** — `5時間` のドーナツ・バッジ・リセット文だけの横並び 2 カード。表示中のサービス数に合わせて幅が詰まり、`最終更新` 行は省略します。
- **ミニマム (Minimum)** — サービスごとに 1 行の Nerd Font ステータスライン: `<アイコン> Claude | 5h 38% 4h39m 10:50 | 7d 39% 2d 6/17 18:00 | ¥46 (daily)`(サービス名・5時間/週次の `%` と「リセットまでの残り時間 + リセット時刻」(`4h39m` / `2d`)・本日の利用額)。文字は Cascadia Mono、アイコンは Symbols Nerd Font(未インストールならアイコンは省略)で描画します。`|` は 2 行で縦に揃え、ウィンドウは行幅に自動フィットします。データ不明のセグメント(リセット時刻なし・金額が算出不能など)はそのぶん省略します。

### トレイアイコン

有効なウィンドウごとに縦%バーのアイコンを表示します。Claude / Codex のアイコンは有効なサービスの最大 `usedPercent` を示すので、最も逼迫しているウィンドウが一目で分かります。色は全体最悪値に追従し、ブランド色(`< 80%`)・橙(`>= 80%`)・赤(`>= 95%`)・くすんだ赤(全プロバイダがエラー)・ライトグレー(データなし)です。ホバーのツールチップはサービス別バッジ、または `5h X% / Weekly Y%` を表示します。

### トレイ右クリックメニュー(5 項目)

どのトレイアイコンからも同じメニューが出ます(両ウィンドウ OFF 時のコントロール用アイコンを含む):

- `今すぐ更新` — 使用率の再取得(重複更新はロック)。
- `Claude/Codexステータス表示モード` ▶(`通常モード` / `コンパクトモード` / `ミニマムモード`)。
- `GitHubCopilot表示モード` ▶(`常時表示` / `ホバー表示`)。
- `設定` — 設定ダイアログを開きます。
- `終了`。

ログイン / ログアウト・認証状態の再確認・GitHub Copilot の初回設定 / 接続テストは、メニューではなく設定ダイアログ側にあります。

### ステータスバッジとリセット時刻

バッジは UI 言語に追従します(`正常取得` / `未インストール` / `未ログイン` / `認証エラー` / `取得を一時制限中` / `取得失敗` / `状態不明`)。`ProviderStatus` の英名はそのまま出しません。`取得を一時制限中` は usage endpoint 自体が HTTP `429` を返したときだけ表示されます(LLM のクォータ超過ではありません)。リセット時刻はローカルタイムで、`5時間` は `あと2時間18分（11:50リセット）`、`週次` は `あと3日4時間（5/27 18:00リセット）` のように表示します。

### 保存される設定

`settings.json`(ユーザーの `AppData` フォルダ配下)には**アプリ設定のみ**を保存します: 更新間隔(`30秒` / `1分` / `5分` / `10分`)・自動起動・テーマ(`ThemeMode`)・UI 言語(`Language`)・`DisplayMode`・各ウィンドウの ON/OFF と表示方法・ステータス窓を常に最前面にするか・Claude / Codex の表示対象サービス・Copilot のプラン / Custom 上限・配色・各ウィンドウの位置。トークン・認証情報・login・URL・パス・メールは一切保存しません。破損していればデフォルトで起動します。

- **ステータス窓を常に最前面に表示**(既定 ON)は、表示方法が `常時表示` のときだけ効きます。OFF にすると他アプリの背面に回せます。`ホバー表示` のときは常に最前面のままです(ホバーで前面に出すため)。`常時表示` + 最前面 ON のときは、窓をタスクバーに重ねて置いても、タスクバーや他アプリをクリックした際に前面へ自動復帰します。
- **テーマ**(ライト / ダーク / システム連動)と **UI 言語**(System / English / 日本語)は**起動時に反映**されます。どちらかを変更すると「今すぐ再起動しますか？」と確認し、はいでアプリがクリーンに自動再起動して反映します。`System` は Windows の色モード / 表示言語に追従します。
- ウィンドウの位置(タスクバーに重ねた位置を含む)を記憶します。保存位置が画面外なら、モニタ内に補正して開きます(タスクバーを除く作業領域ではなくモニタ全体を基準にするため、タスクバー上に置いた位置も保持されます)。
- 自動起動は現在ユーザーの Run キーの `TokenCheckerWin` 値で管理します(publish 済み exe のパス、または開発実行では `dotnet "<app dll>"` 形式)。

### 前回成功値のフォールバック

最後に成功した使用率をサービスごとに保持するため、Claude / Codex の片方が一時的に失敗してももう片方の値は消えません。`last_usage.json` にも永続化されるので、再起動後も前回値が残ります(例: 起動直後の `HTTP 429`)。失敗時はバッジに現在のステータスが出て、本文には `一時的に取得できません。前回成功値を表示しています` と表示されます。次回の成功でスナップショットを上書きします。

## GitHub Copilot(AI Credits・オプトイン)

GitHub Copilot は 2026-06-01 から従量課金(AI Credits)へ移行しました。Claude / Codex のようなリアルタイムのレート制限ウィンドウは無く、個人トークンで取得できるのは **当月の AI Credits 消費量** だけです。月次の上限は公開 API には現れないため、**プラン名と月次のクレジット上限は GitHub から自動判定**し(未ドキュメントのエンドポイント経由。後述)、自動判定できないときだけ **設定で選んだプラン(または Custom の手入力値)** にフォールバックします。

- **既定はオフ(オプトイン)**。「GitHub Copilot ウィンドウを表示」を有効にしたときだけ billing endpoint にアクセスします。無効な間は取得処理自体を走らせません。
- **専用ウィンドウ**に、Copilot アイコン + `GitHub Copilot`、選択中のプラン名、月次リセット時刻(ローカル時刻)つきの `クレジット` 見出し(例: `リセット 11/1 9:00`)、使用率(例: `66% 使用済み`)を表示します。**`n% 使用済み` エリアにマウスを乗せる**と詳細値(例: `4,627 / 7,000 使用済み`)に切り替わります(切り替えはホバーのみで、フォーカスでは切り替わりません)。フォントは `Moralerspace`(未インストールなら Segoe UI)。
- **本日の増分**を 2 番目の主役として表示します。本日 9:00 以降の増分(例: `✦ 本日 +96（+1.4%）`、基準値が無ければ `本日: 未計測`)。スパークアイコンは**按分された日次予算**で色を変えます: 月次リセットまでに残る平日(月〜金)へ残量を割り振った 1 日あたりの予算に対し、超過なら赤、予算の 1 ポイント以内に迫ると橙、それ未満は緑(月次の 80/95% 閾値とは別概念)。高い日は同じ警告スパークがトレイアイコンにも重なります。その下に 100% 到達予測日(例: `このペースだと 6/21 頃に 100%`、不能時は `予測にはデータ不足`)。
- **プラン名と月次上限は自動判定**します。**非公式・未ドキュメント**の `https://api.github.com/copilot_internal/user` から、プラン名と月次の AI クレジット上限を取得します(更新ごとに 1 回の追加 GET。ベストエフォート。トークンは同じ `GITHUB_TOKEN`、ヘッダーも他の呼び出しと同じ標準ヘッダー)。読み取るのは 2 つだけ — 表示名に変換するためのプランコードと、クレジット上限である `premium_interactions` の entitlement です。ログイン名・アカウント ID・トラッキング ID・組織/Enterprise の一覧・endpoints は一切読まず、表示も保存もしません。サポートされた API ではないため、失敗・仕様変更・想定外の応答のときは何も言わず従来どおりの動作に戻ります。
- **設定のプランはフォールバック**(なし / Free=200 / Pro=1,500 / Pro+=7,000 / Max=20,000 / Custom)です。自動判定できないときにだけ使われるので、通常は触る必要がありません。自動判定も選択も無い(`なし`)ときは消費量だけを表示し、上限があれば(自動判定でも選択でも)上限・残量・割合・バーが加わり、80% / 95% でエスカレーションします。(Free のコード補完・チャットのクォータは billing エンドポイントに現れないため表示しません。)
- **配色**: バーの通常色(80% 未満)を選べます(`ブルー（既定）` / `グリーン` / `スカイ` / `パープル` / `スレート`)。数値と `使用済み` ラベルは固定で、80/95% のエスカレーションはバー側で維持されます。
- **今月の日別**: 当月(**UTC 月**)の各日を 1 本のバーで表す小さな棒グラフを、到達予測の行と「今月のモデル別」の間に表示します。GitHub の課金日は `00:00 UTC` = `09:00 JST` 始まり — カードの `本日` 増分と同じ境界 — なので、本日のバー(アクセント色そのまま。過去の日は淡色、未来の日は空欄)は `本日 +N` とおおむね一致します。利用が無かった日は 0 です(GitHub はその日の行を返しません)。プランを選んでいるときは**破線**で**按分された日次予算**(`本日` のスパークアイコンが基準にしているものと同じ、1 平日あたりの予算)を示します。上限が無いときは破線を描きません。バーにマウスを乗せると `10/7  1,826 credits` を表示します。日別の値は `/settings/billing/usage` から取得するため、主取得がそのエンドポイントだった場合を除き**更新ごとに API 呼び出しが 1 回増えます**。この追加取得はベストエフォートで、失敗(どのステータス・タイムアウト・パース失敗でも)した場合はグラフだけが出ず、カードの他の表示には影響しません。
- **今月のモデル別**: 課金レスポンスが行ごとのモデル名を返す場合、当月のクレジットをモデル別に表示します(上位 5 件 + `その他` の集約行。各行にクレジットと構成比。例: `GPT-6.1 Sol   9,149（61%）`、1% 未満は `<1%`)。モデル名が無い行は `モデル不明` にまとめます。フォールバック側のエンドポイントはモデル名を返さないため、その場合はこのセクションごと非表示になります。内訳は常に暦月の累計なので、後述の「利用量を補正」が有効な月は見出しが `今月のモデル別（補正前の累計）` になり、合計が上の使用量と一致しません(構成比はそのまま参考になります)。
- **追加利用**: プランの無償枠で賄えなかった課金対象ぶん(レスポンスの `netAmount`)を `追加利用 $1.23` として表示します。該当フィールドが無ければ非表示で、`$0.00` は「無償枠内に収まった」という意味です。
- **リセットは正確な時刻**です: AI Credits の枠は毎月 1 日 00:00:00 UTC(GitHub が公式に定める境界で、契約日とは無関係)にリセットされます。カードはその時刻をローカル時刻で表示します。
- **月の途中でプランを変更したとき**: GitHub はプラン変更時に AI usage カウンタをリセットしますが、課金 API は当月の累計を返し続けるため、そのままだとカードが過大になります(例: 管理画面 3,915 に対しアプリ 9,041)。Copilot 設定の **「利用量を補正」** で、管理画面の現在値を一度入力すると、変更前ぶん(数値と対象月だけを保存)を差し引いて管理画面と一致させます。翌月初に自動で解除されます。
- **制約**: 個人アカウントに直接課金される利用量のみが対象です(Organization / Enterprise 管理の利用量は `403` / `404` になり得ます)。Enhanced Billing Platform 対象外のアカウントでは取得できないことがあります。
- **HTTP の扱い**: `GITHUB_TOKEN` 未設定 → `未ログイン`、`401` → 無効/期限切れ、`403` → Plan(Read) 権限・個人課金・Enhanced Billing 対象を確認、`429` → `取得を一時制限中`、5xx / タイムアウト / JSON 異常 → `取得失敗`。当月消費が無い場合は使用量 0 として正常表示します。

### 初回設定(`GITHUB_TOKEN` 環境変数)

トークンは環境変数 `GITHUB_TOKEN` から読み取る**だけ**です — アプリには入力させず、保存も出力もしません。(アプリ内ログイン / OAuth / Device Flow は未実装。)「GitHub Copilot 設定」に **`初回設定`**(ウィザード)と **`接続テスト`** ボタンがあります。

1. GitHub の **fine-grained PAT** を作成(名前は例 `TokenChecker`、Expiration は `90 days` 推奨)。
2. `[+ Add permissions]` で **`Plan`** を追加し、**`Read-only`** を選択。
3. `Generate token` を押してコピー。
4. Windows の**ユーザー環境変数 `GITHUB_TOKEN`** に設定し(アプリには入力しません)、起動中のインスタンスに反映させるため TokenCheckerWin を再起動。

**接続テストはトークンを表示しません**。成功時は使用量の数値のみ — `正常取得しました。` / `当月使用量: 4,627 / 7,000 credits` / `使用率: 66%`(プランが `なし` のときは `当月使用量: 4,627 credits`)。失敗時は安全な定型文(未設定 / 無効・期限切れ / 権限不足 / レート制限中 / 取得失敗)のみ。`GITHUB_TOKEN` 未設定で窓が ON のときは「初回設定」を案内します(窓内にトークン入力欄は設けません)。

## プライバシー

アプリは UI・ツールチップ・ログ・保存ファイルのいずれにも、トークン・OAuth 認証情報・パス全体・メールアドレスを**書き出しません**。

- **ログイン補助**(Claude Code / Codex の `ログイン` / `ログアウト` ボタン)は、公式 CLI を新しい `cmd.exe` 内で起動するだけです — `~/.claude/.credentials.json`、`~/.codex/auth.json`、Windows 資格情報マネージャー、API キーなどは**読みません**。`claude` / `codex` 実行後は `認証状態を再確認`(取得を再実行するだけ)を押します。
- **使用率取得時**、Claude Code プロバイダは非公式 usage endpoint を呼ぶ**目的のためだけ**に `~/.claude/.credentials.json`(`CLAUDE_CONFIG_DIR` で上書き可)からアクセストークンを読み取ります。**読み取りのみ**で、トークン・認証情報本文・メール・パスを表示・保存・ログ出力することはなく、資格情報ファイルへ書き込みもしません。
- **本日の利用額の推定**のため、ローカルの Claude / Codex セッションログ(`~/.claude/projects`・`~/.codex/sessions`。`CLAUDE_CONFIG_DIR` / `CODEX_HOME` で上書き可)を**読み取りのみ**で参照し、トークン数とモデル ID だけを取り出します — 会話本文・cwd・パスは扱いません。USD→JPY レートは無料の公開為替 API から 1 日 1 回取得します(失敗時は固定レートにフォールバック)。レートも算出した金額も永続化しません(メモリのみ)ので、下記の 3 ファイル方針は変わりません。
- **診断文字列**は通常表示には出ず、`詳細を表示` の中だけに、唯一の `DiagnosticMasker` でマスクしてから表示されます:
  - メールアドレス風 → `<email>`
  - 絶対パス(Windows / UNC / POSIX) → `<path>`
  - `token` / `secret` / `key` / `authorization` / `bearer` の後に `:` または `=` が続くもの → `name=<redacted>`
  - JWT(`eyJ...`) → `<redacted>`
  - 長い英数字の塊(32 文字以上) → `<redacted>`
- **アプリが書き込むのは次の 3 ファイルだけ**です(すべて `AppData` 配下・数値と日付のみ。トークン・login・URL・パス・メールは持ちません):
  - `settings.json` — アプリ設定のみ。
  - `last_usage.json` — Claude / Codex の数値使用率のみ(診断 `Message` は書き出し前に `null` 化)。
  - `copilot_usage.json` — GitHub Copilot の差分追跡(対象月・最終取得日時・当月使用クレジット・当日 9:00 の基準値)。Copilot ウィンドウが有効で取得に成功したときだけ作成されます。

Claude Code usage endpoint は非公式で予告なく変わり得ます。表示される Claude の使用率はベストエフォートとして扱ってください。

## 開発

### プロジェクト構成

- `src/TokenChecker.Core` — プラットフォーム非依存の共有ライブラリ: 使用率モデル、プロバイダ・インタフェース、`UsageAggregator`、各プロバイダ実装(`Providers/`)。
- `src/TokenChecker.App` — WinForms + `NotifyIcon` の通知領域常駐アプリ。
- `src/TokenChecker.Poc` — `UsageSnapshot` を JSON で標準出力に書き出すコンソール POC。

`TrayApplicationContext` がタイマーで `UsageAggregator.CaptureAsync()` を呼び、各プロバイダが `ServiceUsage` を返します。アグリゲータがプロバイダ個別の失敗を分離するので、1 サービスのエラーが他を巻き込みません。

### テスト

`tests/` 配下の xUnit スイートがアプリの不変条件(80%/95% 閾値・プライバシーマスキング・Copilot allowance・予測ロジック・設定マイグレーション・プロバイダ失敗分離など)を固定しています。コミット・リリース前にソリューション全体でグリーンを保ってください。

```powershell
dotnet test
```

GitHub Actions(`.github/workflows/ci.yml`)が push / PR ごとに windows-latest 上で `dotnet test` を実行します。ローカルの `.githooks/pre-push` フックも push 前に `dotnet test` を走らせ、失敗時は中止します。クローンごとに一度 `git config core.hooksPath .githooks` で有効化してください。

### プロバイダの要点(POC)

```powershell
dotnet run --project src/TokenChecker.Poc                              # Claude / Codex スナップショットを JSON 出力
dotnet run --project src/TokenChecker.Poc -- --github-copilot          # Copilot AI Credits のみ
dotnet run --project src/TokenChecker.Poc -- --github-copilot --raw    # マスク済みエンドポイント診断
dotnet run --project src/TokenChecker.Poc -- --github-copilot --internal-probe   # 非公式エンドポイントの調査
```

- 各プロバイダはまず CLI が PATH 上にあるか確認し、無ければ `NotInstalled` を返します。
- **Claude** は非公式 OAuth usage endpoint を使います。`five_hour` は 300 分窓、`seven_day` は 10080 分窓にマッピング。HTTP `401`/`403` → `Unauthorized`、`429` → `RateLimited`、5xx / タイムアウト / JSON エラー → `Error`。`.credentials.json` はあるが有効なアクセストークンが取れない(例: `/logout` で空になった)場合は、`Error` ではなく `NotLoggedIn` を報告します。
- **Codex** は `codex app-server --listen stdio://` を起動し、JSONL で `initialize` → `account/read` → `account/rateLimits/read` を呼んで app-server を停止します。ログイン未完了は `NotLoggedIn`、`account.type` が `chatgpt` 以外(API キー認証など)は ChatGPT ログイン前提のため `Error` を報告します。
- トークン・認証データ・メール・パス全体は読み取りも出力もしません。`--raw` では Copilot プロバイダが候補エンドポイント(`ai_credit/usage` → `usage` → `premium_request/usage`)を叩き、HTTP ステータスとマスク済みの `usageItems` フィールドだけを出力します(レスポンス本文・login・トークンは出しません)。`GITHUB_TOKEN` が必要です。
- `--internal-probe` は**スキーマ確認用**のフラグです。**非公式・未ドキュメント**の `https://api.github.com/copilot_internal/user` に 1 回だけ GET して、通常の fine-grained PAT が受け付けられるかどうかを確認します(コミュニティの情報では通常 Copilot アプリの OAuth トークンが必要とされています)。送信ヘッダーはプロバイダと同じ標準ヘッダーのみで、エディタ/クライアントの偽装(`Editor-Version` 等)はしません。出力は厳格なホワイトリスト: HTTP ステータス、`x-ratelimit-remaining`、トップレベルのキー*名*のみ、`copilot_plan` / `quota_reset_date`、`quota_snapshots` の各子要素名とその数値/真偽フィールドだけで、200 以外はステータスのみを出します。アプリ本体も(プラン名とクレジット上限の取得のみ・ベストエフォートで)このエンドポイントを使うようになりましたが、このフラグはスキーマ確認用の視点として残しており、アプリが使うより多くのフィールドを出力します。

## ライセンス

本ソフトウェアは [MIT License](LICENSE)(Copyright (c) 2026 kesuhiro74)の下で配布されます。同梱している第三者コンポーネント(GitHub Octicons、.NET ランタイム)の表記は [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) を参照してください。
