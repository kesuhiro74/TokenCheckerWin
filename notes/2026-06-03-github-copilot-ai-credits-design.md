# 設計仕様: GitHub Copilot AI Credits プロバイダ（専用ウィンドウ＋専用トレイ）

> ⚠ **現行仕様は「現行仕様サマリ（active）」と「§0 実装時の改訂」が正。** 本文 §1〜§14 のうち **UI に関する旧記述は廃止**です。特に **§5「UI 設計（通常モードの CopilotCard）」全体**、**§2/§13 の「トレイアイコン対象外」**、**§3／§7 の `VisibleServices` による Copilot 表示ゲート**、**§5.1 等の「`NotifyIcon` は1つ」前提**、**「通常モードに専用カードを追加」前提**、**旧 `CopilotWindowTrigger`／`TrayIconMode`／`CopilotWindowFadeSeconds`／`ShowOnStartup`** は **旧案・不採用**（現行はウィンドウごとの ON/OFF・Always/HoverPreview・有効ウィンドウごとの専用トレイアイコン・両窓OFF時のみコントロールアイコン・`CopilotWindowEnabled`/`ClaudeCodexWindowEnabled` ゲート）。Core プロバイダ／パーサ（§4）、Poc（§8）、プライバシー（§9）、`decimal` 集計（§4.3）、検証手順（§11）は引き続き有効。README と本サマリを一致させること。

**Status: 実装済み（v0.8.2 時点の現行仕様を反映）**

- 作成日(UTC): 2026-06-03
- 状態: 実装完了・main にマージ済み（v0.8.x）
- 注: §5（StatusForm への CopilotCard 案）・§7（VisibleServices ゲート配線案）は廃止・旧案として残置。§11（テスト無し時代の検証方針）は現在の `dotnet test` ベースの CI ゲートに置き換え済み。現行実装の正は「現行仕様サマリ（active）」と「§0 実装時の改訂」。

---

## 現行仕様サマリ（active・これが正）

- **別ウィンドウ**: Claude/Codex のステータス窓と GitHub Copilot ウィンドウは独立。Copilot を「ステータス窓の通常モードにカード追加」する旧案（§5）は**不採用**。
- **ウィンドウごとに ON/OFF**: `AppSettings.ClaudeCodexWindowEnabled` / `CopilotWindowEnabled`。OFF のウィンドウは表示しない。
- **ウィンドウごとに表示方法**: `WindowDisplayMode { Always, HoverPreview }`（`ClaudeCodexDisplayMode` / `CopilotDisplayMode`）。HoverPreview＝専用トレイアイコンのホバーでフェードイン・ウィンドウ外で即非表示・アイコン→ウィンドウ移動は維持・**クリックでピン留め**（常時表示扱い、再クリックで解除）。旧 3 トリガー（Click/MouseOver/ClickThenFade）と `CopilotWindowFadeSeconds`・`TrayIconMode`・`ShowOnStartup` は**廃止**。
- **トレイアイコンは有効ウィンドウごとに専用**: `_statusIcon`（Claude/Codex リング）／`_copilotIcon`（Copilot 縦%バー）。「1アイコン either/or（`TrayIconMode`）」は**撤回**。
- **両窓 OFF 時はコントロール用アイコン**を1つだけ表示（`_controlIcon`）。ウィンドウは出さず、左クリック＝設定／右クリック＝共有メニュー。常に最低1アイコンは可視。
- **右クリックメニューは5項目固定**: `今すぐ更新` / `Claude/Codexステータス表示モード`▶(通常/コンパクト/ミニマム) / `GitHubCopilot表示モード`▶(常時表示/ホバー表示) / `設定` / `終了`。現在値にチェック・切替は即時保存＋反映。ログイン/初回設定/接続テスト等は設定ダイアログ側。
- **Copilot 配色**: `CopilotAccent`（Green/Blue(既定)/Sky/Purple/Slate）は**バーのみ**に反映＝**カードの%バー＋トレイ縦%バー**の通常色(80%未満)（`UsageTheme.AccentColor(value, baseColor)` 経由・トレイは `Lighten(accent,0.25)`）。**`n%` の文字色は黒系固定（`PrimaryText`）・`使用済み` は薄いグレー固定（`MutedText`）で配色設定の影響を受けない**。**80/95 の橙/赤エスカレーションはバー側で維持**（severity が上書き）。装飾グラスのスレートピルは固定。配色の対象は第4弾「装飾のみ」→第5弾「数値も」→**第6弾でバーのみ＋数値黒・`使用済み`薄グレー固定**に更新。
- **Copilot カード上部**: Octicons Copilot アイコン＋`GitHub Copilot`（1行目）＋**プラン名サブ行**（2行目・小さめ薄め）。**「タイトルそのものをプラン名にする」旧仕様は廃止**。主要表示フォントは `Moralerspace`（インストール時のみ使用・同梱せず・無ければ Segoe UI へフォールバック）。
- **取得ゲート**: Copilot provider は `CopilotProviderEnabled`(=`CopilotWindowEnabled`)、Claude/Codex provider は `ClaudeProviderEnabled`/`CodexProviderEnabled`(=`ClaudeCodexWindowEnabled` ＋ `VisibleServices` の個別サービス表示)。OFF のものは provider を生成せず取得しない。
- **OFF サービスの stale 値除去**: 表示用 snapshot/fallback（`BuildFallbackSnapshot`）・トレイアイコン（`DetermineState` 入力）・ツールチップは `IsServiceEnabledForDisplay` でフィルタし、無効化済みサービスの古い警告/危険状態を出さない。`last_usage.json` 自体は改変しない（表示段階でフィルタ）。
- **旧 settings.json 移行**（`SettingsStore.Load`・一度きり）: `ShowOnStartup=false`→`ClaudeCodexDisplayMode=HoverPreview`、`CopilotWindowTrigger`(任意値)→`CopilotDisplayMode=HoverPreview`、`VisibleServices` の `"GitHub Copilot"` または `TrayIconMode="Copilot"`→`CopilotWindowEnabled=true`、`VisibleServices` は Claude/Codex のみへ正規化。`CopilotWindowFadeSeconds` は無視（廃止）。
- **初回設定**: 認証は **PAT + `GITHUB_TOKEN` 環境変数方式**（アプリ内ログイン/OAuth/Device Flow は未実装）。設定の `初回設定` ウィザード（`GitHubCopilotSetupForm`）の構成は **「GitHub のトークン作成ページを開く」**（押下で下部テキスト領域に**トークン作成手順**を表示: Token name 例 `TokenChecker` / Expiration 推奨 `90 days` / Permissions に `Plan` を追加し `Read-only` を確認 / `Generate token`）＋ **「環境変数の設定方法を表示」**＋ **「接続テスト」**＋ **「閉じる」**。**`権限の説明を開く` ボタンや `PermissionsDocUrl` は存在しない**（権限の説明は作成手順テキストに統合）。**token はアプリ内入力させない・保存しない・表示/ログ/結果に出さない**（接続テストは使用量数値＋安全な定型文のみ）。`GITHUB_TOKEN` 未設定時は CopilotWindow が「初回設定」へ誘導。
- **不変**: 桁は整数（共有モデル非侵襲）、80/95 閾値は `UsageTheme` に集約、保存は `settings.json`/`last_usage.json`/`copilot_usage.json` の3点のみ（数値・日付のみ・トークン非保存）。Copilot カードは値スワップが固定ボックス＝無ガタ（ホバー詳細はメイン表示エリアのみで切替）。ピン留め/常時表示の Copilot 窓は外周1px枠＋外クリックで非表示（トレイ/共有メニュー操作とは非競合）。

詳細な実装手順・経緯はプランファイル `14-copilot-hazy-panda.md`、変更履歴は §0 を参照。

---

## 0. 実装時の改訂（専用ウィンドウ＋専用トレイ・2026-06-05）

実装着手時にユーザー指示で **UI を §5「通常モードの専用カード」から大幅変更**した。Core（§4）・設定の骨子（§6）・Poc（§8）・プライバシー不変条件（§9）はそのまま有効。差分は以下。

> ⚠ **この第1弾の以下3点は、後述『第2弾』でさらに置換済み**（最新は冒頭「現行仕様サマリ」が正）: ①「ウィンドウ表示トリガー（`CopilotWindowTrigger`：Click/MouseOver/ClickThenFade）＋`CopilotWindowFadeSeconds`」→ **Always / HoverPreview の2種類**に置換・両設定とも廃止。②「トレイは1アイコン（`TrayIconMode` で either/or）」→ **有効ウィンドウごとの専用アイコン＋両窓OFF時のコントロールアイコン**に置換。③「provider ゲート＝`VisibleServices` に "GitHub Copilot" or `TrayIconMode==Copilot`」→ **`CopilotWindowEnabled`（Claude/Codex は `ClaudeCodexWindowEnabled`＋個別表示）** に置換。以下の §0 第1弾本文は経緯保存のため残置。

- **桁**: 整数表示で確定（ユーザー選択）。`RateLimitWindow.Used`(long, 丸め済み)のまま、共有モデル非侵襲（§2/§10 不変条件を完全遵守）。詳細は `{used:N0} / {allowance:N0} 使用済み`。
- **専用ウィンドウ**: `CopilotWindow`（新 Form。Claude/Codex のステータス窓とは独立）。通常は `66% 使用済み` を大きく表示、**ウィンドウ全体ホバー or キーボードフォーカス**で `4,627 / 7,000 使用済み` に切替。メイン行は固定サイズ Label の `.Text` 差し替えのみ＝バー割合・リセット表記・ウィンドウサイズ不変・無ガタ。`StatusForm` の通常モードに Copilot カードは追加しない（§5 を置換）。
- **ウィンドウ表示トリガー（Copilot 窓のみ）**: `CopilotWindowTrigger`（Click / MouseOver / ClickThenFade）。フェード秒数 `CopilotWindowFadeSeconds`（既定5・設定可）。ホバー/フォーカス中はフェード一時停止。
- **トレイアイコン**: 1 アイコンのまま `TrayIconMode`（ClaudeCodex / Copilot）で「どちらか一方」を設定選択。Copilot は表示領域を縦長に使った **縦%バー**（プラン上限比）。→ §2/§13 の「トレイアイコン対象外」は撤回。
- **provider 可視性ゲート（§7 の拡張）**: `AppSettings.CopilotProviderEnabled = IsServiceVisible("GitHub Copilot") || TrayIconMode==Copilot`。どちらかの consumer が有効な時だけ provider を組み込む（両方オフなら billing 非アクセス）。
- **共有化**: 80/95 閾値の単一真実として `UsageTheme`（`AccentColor`/`BrandUsageColor`/しきい値定数）を新設。`StatusForm.UsageAccentColor`/`BrandUsageColor` と `TrayIconRenderer.DetermineState` はこれに委譲。`UsageBarControl` を `StatusForm` から独立ファイルへ抽出し CopilotWindow と共有。
- **非 Available 文言**: Copilot 専用（`FriendlyMessage` の「再ログイン」系は使わない）。`NotLoggedIn`→GITHUB_TOKEN 案内 / `Unauthorized`+診断 `(403)`→権限・課金確認 / それ以外の `Unauthorized`(401)→無効・期限切れ。

### 第2弾（2026-06-05・追加改訂）

実機確認後のユーザー指示で UI/設定/トレイをさらに再設計（詳細はプランファイル `14-copilot-hazy-panda.md` の「追加要件 第2弾」）。要点のみ:

- **追加サブ情報**: 100%到達予測（平均ペース）＋本日9:00以降の増分。差分計算用に **`copilot_usage.json`**（数値・日付のみ）を新設＝保存許可ファイルは `settings.json` / `last_usage.json` / `copilot_usage.json` の3点（§9 更新済み）。
- **Copilot カード見た目**: タイトル＝**プラン名**（`Copilot Pro` 等。サービス名は設定/ツールチップ側）、右上バッジは小さく淡色、メイン行は「値（大）＋『使用済み』（約55%・淡色・ベースライン揃え）」のカスタム描画（ホバーは値のみ差し替え＝無ガタ）。
- **表示方法は2種類に置換**（`Always` / `HoverPreview`）。旧 `CopilotWindowTrigger`（3種）・`CopilotWindowFadeSeconds`・`TrayIconMode`・`ShowOnStartup` は**廃止**。表示方法は**ウィンドウごと**。
- **ウィンドウ独立 on/off**（Claude/Codex 窓・Copilot 窓）。OFF は非表示＋当該 provider を取得しない（`ClaudeProviderEnabled`/`CodexProviderEnabled`/`CopilotProviderEnabled` ゲート）。
- **トレイは窓ごとの専用アイコン**（status リング / copilot 縦%バー）。**両窓OFF時のみ control アイコン**を1つ出し、設定・終了へ必ず到達できる（ウィンドウは出さない）。旧「1アイコン either/or」は撤回。
- **設定画面を3区分**（共通 / Claude·Codex / GitHub Copilot）に再構成。Claude/Codex の個別サービス表示トグルは残置。
- 旧 `VisibleServices` の `"GitHub Copilot"` は `SettingsStore.Load` で `CopilotWindowEnabled` に一度きり移行。

### 第3弾（2026-06-06・初回設定ウィザード）

- **認証方式は PAT + `GITHUB_TOKEN` 環境変数のまま**（アプリ内ログイン／OAuth／Device Flow は**未実装**）。必要権限は **fine-grained PAT の User permissions: Plan = read**。
- **新規 `GitHubCopilotSetupForm`**（1画面ウィザード）: 説明（「この画面では token を入力しない／GitHub で作成し env `GITHUB_TOKEN` に設定」）＋ボタン「GitHub のトークン作成ページを開く」（定数 URL・押下時に下部テキスト領域へ **fine-grained PAT 作成手順**を表示: Token name 例 `TokenChecker` / Expiration 推奨 `90 days` / Permissions に `Plan` を追加し `Read-only` / `Generate token` / コピーした token を env `GITHUB_TOKEN` へ）／「環境変数の設定方法を表示」（PowerShell 例＋`<your-token>` 置換・再起動・非保存の注記）／「接続テスト」／「閉じる」。`SettingsForm` の GitHub Copilot 設定に **`初回設定`／`接続テスト`** ボタンを追加。`権限の説明を開く` ボタンは作成手順の表示に統合して削除。
- **接続テスト**は既存 `GitHubCopilotUsageProvider` を再利用（`RunConnectionTestAsync(int? allowance)` に集約）。**token はアプリ内入力させない・保存しない・画面/ログ/診断/結果に出さない**。結果は**使用量の数値と安全な定型文のみ**（`ProviderStatus` ＋ マスク済み Message の `(403)` 判定のみ使用、生 Message は非表示）。テスト中はボタンを無効化し二重実行防止。
- **`GITHUB_TOKEN` 未設定時**: CopilotWindow は `GITHUB_TOKEN が未設定です` ＋サブテキスト `設定画面の「初回設定」から手順を確認してください` を表示（**token 入力欄は作らない**）。

### 第4弾（2026-06-06・トレイメニュー再構成／アイコン／配色）

- **右クリックメニューを5項目に固定**: `今すぐ更新` / `Claude/Codexステータス表示モード`▶(通常/コンパクト/ミニマム＝`DisplayMode`、現在値にチェック) / `GitHubCopilot表示モード`▶(常時表示/ホバー表示＝`CopilotDisplayMode`、現在値にチェック) / `設定` / `終了`。**ログイン/ログアウト・認証再確認・初回設定・接続テスト・旧「○○を表示」「表示モード」は撤去**（設定ダイアログ側へ集約）。3つの NotifyIcon（status/copilot/control）すべてに同じ `ContextMenuStrip` を共有。両窓OFFでも control アイコンから到達可能。
- **表示モード切替の即時反映**: `SetDisplayMode`（コンテンツ）/`SetCopilotDisplayMode`（Always/HoverPreview）とも `settings.json` 保存＋`ApplySettings`/`ApplyWindowModes`＋トレイ再描画（キャッシュ snapshot から・再取得なし）。`常時表示` は対象窓を表示しピンを解除して統一、`ホバー表示` はピン以外は非表示でアイコンホバー表示。各サブメニューは対象ウィンドウ OFF 時は無効表示。
- **Copilot トレイアイコンの角**: `TrayIconRenderer.DrawCopilotBar` の角丸半径を pill（`barWidth*0.45`）から `Math.Max(1.5, Math.Min(barWidth*0.22, size*0.12))` に縮小（角を出す）。輪郭線を `Math.Max(1.2, size*0.055)` に明確化。小サイズで潰れず DPI でも比例。**80%/95% の色変化は不変**。
- **Copilot 配色設定**: `enum CopilotAccent { Slate, Blue, Sky }` ＋ `AppSettings.CopilotAccentColor()`。設定ダイアログの「配色」コンボで選択し、`CopilotWindow.ApplyAccent` でカードのブランド色（`PaintGlassCard` の左アクセントピル＋淡い tint）に反映。**装飾色のみ**で 80/95 severity（数字・バー＝`UsageTheme`）には不干渉。トレイの縦%バーは severity 配色のまま（角のみ変更）。〔**第5弾で「配色＝本体色」に置換**。下記参照。〕

### 第5弾（2026-06-06・Copilot カード5点修正）

実機フィードバックによる5点（いずれも App 側の表示/設定のみ。Core プロバイダ/パーサ・共有モデル・`GITHUB_TOKEN` 経路・`copilot_usage.json` スキーマ・`UsageTheme` の 80/95 閾値定義は不変）。

- **#1 バッジ見切れ＋小型化**: `_badge` フォント `8F→7.5F`。`CopilotCard.ApplyBadge` を「Label 実描画に合わせたパディング込み実測（`TextRenderer.MeasureText(text,font)`・`NoPadding` を付けない）」で幅・高さとも採寸しタイトル行へ縦配置。高DPI 対策で `y=Math.Max(12, 12+(22-height)/2)`（行頭を下限化＝浮き上がり防止）、高さは文字追従（縦切れ防止・右余白へ下方展開）。
- **#2 「使用済み」縮小**: `MainUsageControl` のサフィックス比 `0.55→0.46`（ベースライン揃え・固定ボックス無ガタは維持）。
- **#3 100%到達予測の起点**: `CopilotUsageTracker.PredictFull` を **UTC1日0時 → ローカル暦の当月1日0時**起点に変更（`nowLocal.Offset`・`resetLocal=monthStartLocal.AddMonths(1)`・`fullLocal` を直接返却）。算出時点の利用割合からの線形外挿（`月初 + 経過日数×cap/usedNow`）。※実リセットは UTC1日=JST9:00 のため起点は約9h 早いが、**ユーザー明示要件「ローカル暦の当月1日0時を起点」を優先**（影響は月初9hのみ・到達日は僅かに“遅め”側・軽微）。
- **#4 配色＝本体色の設定化**（第4弾の「装飾のみ」を**置換**）: `enum CopilotAccent { Green=0,Blue,Sky,Purple,Slate }` に再定義（`JsonStringEnumConverter` は名前保存ゆえ旧 `Blue/Sky/Slate` も解釈可）。`CopilotAccentColor()` は**本体ベース色**を返す。`UsageTheme.AccentColor(double?)` は `AccentColor(value, Good)` へ委譲し、新オーバーロード `AccentColor(value, baseColor)`（`>=95 Bad / >=80 Warning / else baseColor`）で**数値・カードバー（`CopilotWindow.SetAccent`）・トレイ縦%バー（`CreateCopilotIcon(…, accentBase)`＝`Lighten(accent,0.25)`）**の 80%未満色を変更。**80/95 エスカレーションは色設定に関わらず維持**。装飾グラスのスレートピル（`_brand`）は固定。Claude/Codex 側（`AccentColor(value)`）は不変。設定コンボは Green/Blue/Sky/Purple/Slate。**注: この第5弾の時点では Green=0 を既定としていたが、現行実装（`AppSettings.cs`）では `CopilotAccent.Blue` が既定値（`Normalize()` の未定義フォールバックも Blue）に変更済み。**
- **#5 HoverPreview のホバー切替**: `CopilotWindow` に **Visible 中のみ動く 120ms ポーリング**（`_hoverPoll`→`RefreshHover`）を追加。HoverPreview は非アクティブ表示＋カーソル直下出現で `MouseEnter/Leave` が飛ばず、イベント駆動の %↔詳細値スワップが漏れるのを補完。`OnVisibleChanged` で表示時 Start／非表示時 Stop（teardown 中は `Disposing||IsDisposed` ガードで Timer/`_card` 非タッチ）、`Dispose(bool)` で停止破棄。Always/HoverPreview とも同一挙動・差分時のみ反映で無ガタ。Context 側 leave ポーリングとは独立。

### 第6弾（2026-06-06・Copilot ウィンドウ UI 調整 9点）

App 側の表示/トレイ配線のみ。Core プロバイダ/パーサ・共有モデル・`GITHUB_TOKEN` 経路・`copilot_usage.json` スキーマ・初回設定ウィザード・`UsageTheme` の 80/95 閾値定義は不変。

- **#1 フォント = Moralerspace（同梱せず・install-conditional）**: `UsageTheme.CreateCopilotFont`/`CopilotFontFamily`/`CopilotFontFellBack` を追加。候補（`Moralerspace`→各 variant）を一度だけ解決しキャッシュ、無ければ `Segoe UI` へフォールバック（例外時も再フォールバック＝クラッシュしない）。Copilot カードの主要表示（タイトル/プラン名/数値/`使用済み`/バッジ/サブ情報/詳細トグル）を経由。`_detailBox` は Consolas 据え置き。
- **#2 `正常取得`/`使用済み` を +1pt**: バッジ `7.5F→8.5F`、サフィックス `valueSize*0.46f → +1f`（≈6.9→7.9pt）。`n%`(15pt)より明確に小・見切れ優先。
- **#3 ホバー判定＝メイン表示エリアのみ**: `RefreshHover` を `_card.MainLineScreenBounds`（`_mainLine` の画面矩形）包含判定に変更。120ms ポーリング＋Enter/Leave 維持。Context 側 `HoverLeaveCheck`（`Form.Bounds` で表示/非表示）とは独立。
- **#4 ピン留め/常時表示時の外周1px枠**: `CopilotCard._pinned`＋`SetPinned`、`OnPaint` でカード角丸縁に `UsageTheme.PinnedBorder`（半透明スレート）1px。Context が `UpdateCopilotPinnedAppearance`（`bordered = Enabled && Visible && (Always || Pinned)`）を show/hide/pin/mode 変更で反映。ホバー・グランス（未ピン）は枠なし。
- **#5 外クリック非表示**: `Form.Deactivate` 方式（グローバルフック不使用）。クリック表示/ピンで窓がアクティブ化→外側クリックで Deactivate→`OnCopilotDeactivated` が stuck（`Pinned||Always`）かつ表示中なら `HidePopup`＋`Pinned=false`。`_copilotIcon.MouseDown` と `_contextMenu.Opening/Closed` で抑制（トレイ/メニュー操作と非競合）。**起動時 Always（未クリック・非アクティブ）は本経路では消えない**（初回操作後に有効）。
- **#6 配色＝バーのみ**: `n%` を `UsageTheme.PrimaryText`（黒系固定）、`使用済み` を `UsageTheme.MutedText`（薄グレー固定・`MainUsageControl` 内）。配色は**カードバー＋トレイ%バー**のみ（`_bar.AccentColor = AccentColor(percent,_accent)`、トレイ据え置き）。`SetAccent` はバー色のみ再計算。80/95 エスカレーションはバーで維持（第5弾の「数値も配色」を更新）。
- **#7 外周余白**: `FormPadding 12→9`（窓サイズ 318×222）。
- **#8 左仕切りからの余白**: `ContentLeft 14→18`（`contentWidth=268`）。左ピル（x8〜12）→内容(18) を 6px に。タイトル/数値/バー/サブの開始位置を 18 に統一、バッジ右アンカー据え置き。
- **#9 上部刷新**: タイトルを固定「GitHub Copilot」＋新 `_planSub`（プラン名・小さめ薄め）。アイコンは private static `CopilotGlyph`（Octicons `copilot-16` の `d` を埋め込み）＋最小 SVG パスパーサ `SvgPath`（`M/L/H/V/C/A/Z`・弧は endpoint→center で `AddArc`、`FillMode.Winding`）で一度だけ `GraphicsPath` 化しカード `OnPaint` で 16px スレート塗り（外部通信・フォントファイル追加なし）。2行ヘッダ化で `CardBaseHeight 186→204`。

### 第7弾（2026-06-06・テーマ: ライト/ダーク/システム連動）

Windows の色モードに応じた表示切替を追加。**反映は起動時のみ**（実行中ライブ切替なし・設定変更/Windows 切替は次回起動で反映）。Core/token・`copilot_usage.json`・初回設定ロジック・80/95 閾値定義・トレイアイコン描画は不変。

- **設定**: `AppSettings.ThemeMode { System(既定)/Light/Dark }`＋設定「共通設定」にコンボ＋「(再起動で反映)」注記。`Normalize`/`Clone` 反映。
- **配色基盤**: `UsageTheme` をパレット化（`Palette` record の `Light`/`Dark`＋`_active`、`Apply(bool dark)`、`IsDark`）。色は `static readonly` → `static` プロパティ（`=> _active.X`）化（呼び出し記法不変）。`SubtleText`/`ClaudeBrand`/`CodexBrand`（旧 StatusForm 専用）を昇格。`PaintGlassCard` の白固定グロス/内側境界/上方向ライトンをパレット駆動（ダークは低 alpha＝白帯にならない）。ダークパレットは Surface/Card/各テキスト/Track/Detail を反転、Good/Warning/Bad・ブランドはダーク用に明度調整。
- **検出/適用**: 新 `WindowsTheme`（registry `AppsUseLightTheme==0`、欠落時ライト）で System を解決。`Program.Main` の `Initialize()` 前に `UsageTheme.Apply(dark)`＋`Application.SetColorMode(dark?Dark:Classic)`（標準コントロール＝設定ダイアログをダーク化）。`DpiUnaware` と独立。
- **窓**: `CopilotWindow` は `UsageTheme` 参照のみで自動追従。`StatusForm` は重複色フィールドを `UsageTheme` への前方委譲（プロパティ）に置換＋複製 `PaintGlassCard` を `UsageTheme.PaintGlassCard` へ統一（未使用 `Separator`・dead `Tint/Lighten` 削除）。`GitHubCopilotSetupForm` の出力 TextBox を `UsageTheme.DetailBackground`/`SecondaryText` に。
- **設定ダイアログ**: ログイン状態色を `UsageTheme.StatusColor`（テーマ対応）に。ボタンは `FlatStyle.System`（ダーク見栄え）。`OnHandleCreated` でダーク時タイトルバー（`DwmSetWindowAttribute(20)`・Win11+/失敗無視）。レイアウトは共通設定 GroupBox を +40 して全体を下げ、ダイアログ高さ 706→746。
- **トレイアイコン**: 現状維持（暗背景向けパレットのまま）。
- **検証**: DrawToBitmap で CopilotWindow/StatusForm をライト/ダーク両方描画し目視（ダークで面暗・文字白・グロス非破綻・バー/リング/ブランド色が読める）。設定ダイアログのダーク化は実機目視。

---

## 1. 背景と目的

TokenCheckerWin は Claude Code / Codex の使用率（5時間・週次のレート制限）を Windows トレイに常駐表示する .NET 10 WinForms アプリ。本ブランチでは **GitHub Copilot の AI Credits 当月消費**を、3つ目のサービスとして **ステータス窓の通常モードに専用カード**で表示できるようにする。

GitHub Copilot は 2026-06-01 から従量課金（AI Credits）へ移行した。Claude/Codex のような**リアルタイムのレート制限ウィンドウ（utilization% + resets_at）は存在せず**、個人トークンで取得できるのは **当月の AI Credits 消費量**のみ。月次の**上限（同梱クレジット枠）・残量は API では公開されていない**ため、上限は**ユーザーがプランを選ぶ（または手入力する）ことで App 側が与える**。

### 確定した方針（ユーザー合意事項）

| 項目 | 決定 |
|---|---|
| スコープ | ステータス窓の **通常モードのみ**。専用カードを1枚追加 |
| 表示内容 | **count-based**（クレジット建て）で **使用量・上限・残量**（＋割合・バー） |
| 上限の取得 | **設定でプラン選択**（Pro=1,500 / Pro+=7,000 / Max=20,000）。加えて **Custom 手入力上限**も可 |
| トークン | 環境変数 **`GITHUB_TOKEN` のみ**（読み取り専用・非保存・非出力）。未設定は `NotLoggedIn` |
| 既定 | **オプトイン（既定オフ）**。`VisibleServices` 既定は `["Claude","Codex"]` のまま |
| 対象外（今回） | コンパクト/ミニマムモード、**トレイアイコン・ツールチップ**（percent 前提のため後続ブランチ） |

---

## 2. スコープ

### 含む（in scope）
- Core: `GitHubCopilotUsageProvider` + `GitHubBillingUsageParser` を本ブランチに実装（AI Credits 対応のマッピング）。
- App: 通常モードの `CopilotCard`、設定（表示トグル＋プラン選択＋Custom 上限）、プロバイダ配線。
- Poc: `--github-copilot` / `--github-copilot --raw` ランナーを本ブランチに同梱（実アカウントでの schema/権限エラー確認用）。
- README: GitHub Copilot 節を追加。

### 含まない（out of scope, 今回）
- コンパクト/ミニマムモードへの Copilot 表示。
- トレイアイコン（`TrayIconRenderer`）・ツールチップ（`BuildTooltip`）への Copilot 反映。
- Organization / Enterprise 管理の Copilot 利用量（個人 billing endpoint には出ない）。
- 共有モデル（`ServiceUsage`/`RateLimitWindow`/`ProviderStatus`）の変更。

---

## 3. アーキテクチャとデータフロー

既存の `IUsageProvider → UsageAggregator → StatusForm` に**非侵襲**で3つ目のプロバイダを追加する。共有モデル・`UsageAggregator`・`LastUsageStore`・`DiagnosticMasker` は変更しない（すでに汎用・サービス名キー・Message null 化済み）。

**プロバイダはオプトイン時のみ実行する**（§7）。`VisibleServices` に `"GitHub Copilot"` が含まれないとき `GitHubCopilotUsageProvider` は aggregator に**入れない**＝`/user`・billing endpoint には**一切アクセスしない**。表示 ON のときだけ provider list を組み込み、設定変更で aggregator を再構築する。

```
GitHubCopilotUsageProvider (Core)  ← VisibleServices に "GitHub Copilot" がある時だけ aggregator に組み込む
  ServiceName = "GitHub Copilot"
  env GITHUB_TOKEN 読取（無→NotLoggedIn）
   → GET /user で login 解決
   → GET /users/{login}/settings/billing/ai_credit/usage?year&month   ← 第一候補
        404/未対応時のみ → GET /users/{login}/settings/billing/usage?year&month  ← フォールバック
   → usageItems[] から「Copilot AI Credits 行」を堅牢判定で抽出し当月消費(クレジット)を集計
   → 月次 window を1つ返す:
        Used = 集計クレジット(丸め),  Limit/Remaining/UsedPercent = null,
        ResetAtUtc = 翌暦月1日UTC(推定),  WindowDurationMins = 43200(月次の識別子)
        │
        ▼
UsageAggregator.CaptureAsync()  ← 既存。例外は個別 Error 化（変更不要）
        │
        ▼
StatusForm.CopilotCard (App, 通常モードのみ)
  設定の CopilotPlan/Custom → 同梱クレジット(上限) を「表示時にオーバーレイ」
  上限あり: 「Used / 上限 credits  残 R (P%)」＋バー（UsageAccentColor で 80/95% 配色）
  上限なし(None): 「Used credits 使用（当月集計）」＋「設定でプランを選ぶと上限・残量を表示」
```

**上限を Core に持たせない理由**: 上限は UI 設定であり API 由来ではない。Core は API が返した `Used` だけを持つ純粋な状態に保つ。App が表示時にプラン上限を重ねることで、(a) 設定でプランを変えても**再取得なしで即反映**、(b) `last_usage.json` には `Used` だけが残り上限は混入しない。

---

## 4. プロバイダ設計（Core）

`src/TokenChecker.Core/Providers/GitHubCopilot/` に配置。POC 実装（ClaudeUsageProvider の流儀に準拠: static `HttpClient` + per-call linked CTS、happy path で例外を投げない、全診断は `DiagnosticMasker` 経由）を踏襲しつつ、AI Credits 対応に作り替える。

### 4.1 エンドポイント戦略
- 共通ヘッダ: `Authorization: Bearer {token}`、`User-Agent: TokenCheckerWin/{version}`、`Accept: application/vnd.github+json`、`X-GitHub-Api-Version: 2026-03-10`（移行に応じて要調整・定数化）。
- 手順:
  1. `GET https://api.github.com/user` → `login` 解決（失敗時は status を `MapFailure` でマップ）。
  2. **第一候補**: `GET https://api.github.com/users/{login}/settings/billing/ai_credit/usage?year={year}&month={month}`。
  3. 第一候補が **404（not found / 未対応）** のときのみ **フォールバック**: `GET .../settings/billing/usage?year={year}&month={month}`。
  4. それ以外の失敗（401/403/429/5xx 等）はフォールバックせず即マップ（認証・レート・サーバ起因でありエンドポイント差ではないため）。
- `year`/`month` は `DateTimeOffset.UtcNow` から算出（当月）。

### 4.2 Copilot AI Credits 行の堅牢判定（狭めた判定）
正規化ヘルパ `Normalize(s)` = null/未設定は**空文字**、それ以外は `ToLowerInvariant()` の上で空白・`_`・`-` を除去（既存 POC の `NormalizeSku` と同じ null 安全規約）。例: `"Copilot AI Credits"→"copilotaicredits"`、`"AI Credit"→"aicredit"`、`"ai-credits"→"aicredits"`、`"Copilot Premium Request"→"copilotpremiumrequest"`、`"Requests"→"requests"`。これにより `unitType` 不在の行でも `unitIsGenericCredits`（完全一致）・`isPremiumRequest`（`=="requests"`）が誤判定しない。

**設計判断（レビュー反映）**: `product=="Copilot"` だけ、または `unitType=="credits"` だけでは AI Credits とみなさない。Copilot には premium request（`unitType=requests`）など AI Credits 以外の行も載るため、まず premium request 行を**明示的に除外**したうえで、AI-credit を示す確かなシグナルがある行だけを拾う。

シグナル定義:
- `unitIsAiCredits(unitType)` = `Normalize(unitType)` が `"aicredit"` を含む（`ai-credits`/`aicredits`/`aicredit` を吸収）。**強いシグナル**（単独で該当可）。
- `skuIsAiCredit(sku)` = `Normalize(sku)` が `"aicredit"` を含む。**強いシグナル**（単独で該当可）。
- `productIsAiCredit(product)` = `Normalize(product)` が `"aicredit"` を含む。**強いシグナル**（単独で該当可）。
- `unitIsGenericCredits(unitType)` = `Normalize(unitType)` が `"credits"` または `"credit"` に**完全一致**。**弱いシグナル**（単独では拾わない）。
- `productIsCopilot(product)` = `Normalize(product)` が `"copilot"` を含む。**弱いシグナル**（単独では拾わない）。

除外（最優先・該当したら無条件で対象外）:
- `isPremiumRequest(item)` = `Normalize(sku)` が `"premiumrequest"` を含む **または** `Normalize(unitType)` == `"requests"`。

判定:
```
IsCopilotAiCreditUsage(item) =
   !isPremiumRequest(item)
   && (
        unitIsAiCredits(unitType)                                   // ai-credits 単位（強・単独可）
     || skuIsAiCredit(sku) || productIsAiCredit(product)            // AI Credit を示す sku/product（強・単独可）
     || ( unitIsGenericCredits(unitType)                            // 汎用 credits 単位（弱）は…
          && (productIsCopilot(product)                             // …Copilot/AI-credit 文脈が
              || skuIsAiCredit(sku) || productIsAiCredit(product))) //    ある時だけ許容
      )
```

帰結（レビュー指摘の充足）:
- `product=="Copilot"` のみ（ai-credit を示す unit/sku なし）→ **拾わない**（item 1）。
- `unitType=="credits"` のみ（Copilot/AI-credit を示す product/sku なし）→ **拾わない**（item 2）。`product` が Copilot、または `sku`/`product` が AI Credit を示す時だけ許容（弱シグナルの文脈条件。bare `product=="Copilot"` も「Copilot を示す product」として文脈成立とみなす）。
- `unitType=="requests"` または `sku` が premium request → **必ず除外**（item 1）。

補足: 第一候補（`ai_credit/usage` 専用 endpoint）の `usageItems` は本質的に全行が Copilot AI Credits（`unitType` が `ai-credits` か `credits`）で、行は `product=="Copilot"` か ai-credit を示す unit/sku を伴う前提なので上記判定を素通りする。仮に第一候補で `unitType=="credits"` かつ Copilot マーカーの無い行が来た場合は弱シグナルの文脈条件に外れて**取りこぼす**が、これは `--raw` 実測（§8/§11）で実フィールドを確認し最終確定する（推測で広げない）。フォールバックの汎用 endpoint では上記判定が AI Credits 行のみを選び、premium request・Actions 等は除外する。AI Credits を使うのは現状 Copilot のみ、という前提を docs に明記。

### 4.3 集計（主ルート / フォールバック、すべて `decimal`）
**金額→クレジット換算とその合計は `double` を使わず `decimal` で行う**（cent 計算の二進浮動小数点誤差を避ける）。`GitHubBillingUsageParser` は credits 計算に使う `grossQuantity`/`quantity`/`grossAmount` を `decimal?` で捕捉する。取得経路も `decimal` に統一し、`JsonNode.GetValue<decimal>()` / `decimal.TryParse`（POC の `GetDouble` 相当の `GetDecimal` ヘルパ）で読む＝一度も `double` に materialize しない。

各該当行のクレジット値（`decimal`）:
```
rowCredits (decimal) =
    ((unitIsAiCredits(unitType) || unitIsGenericCredits(unitType)) && grossQuantity != null)
                          ? grossQuantity                 // 主ルート: credits/ai-credits 単位の grossQuantity（クレジット直値）
  : (grossAmount != null) ? grossAmount * 100m            // 確認用フォールバック: 1 credit = $0.01（decimal リテラル 100m）
  : (grossQuantity ?? quantity ?? 0m)                     // 最後の保険
```
- `usedExact (decimal) = Σ rowCredits`、`Used(クレジット, long) = (long)Math.Round(usedExact, MidpointRounding.AwayFromZero)`。**丸めは合計後に1回だけ** `AwayFromZero` で行う（行ごとには丸めない）。
- `netQuantity`/`netAmount` は集計に**使わない**（`netAmount` は同梱枠適用後＝超過課金額。`netQuantity` は POC で 0/null）。
- 精密値はマスク済み Message に `usedExact=<decimal>; unit=credits;` で出す（共有モデル非侵襲）。`usedExact` は `decimal` を `CultureInfo.InvariantCulture` で整形。

### 4.4 window / リセット（暦月固定と断定しない）
- 返す window は1つ: `Name="GitHub Copilot AI Credits"`, `Used=<credits>`, `Limit=null`, `Remaining=null`, `UsedPercent=null`, `WindowDurationMins=43200`, `ResetAtUtc=<翌暦月1日0時UTCの推定値>`。
- `ResetAtUtc` は **API 由来ではなく暦月近似の推定**。UI はこれを断定的なカウントダウンにせず「当月集計（暦月近似・推定リセット）」として扱う（§5.3）。
- 請求サイクルが署名日基準のユーザーでは暦月とずれ得る点を docs に明記。

### 4.5 ステータスマッピング
| 事象 | ProviderStatus | 備考 |
|---|---|---|
| `GITHUB_TOKEN` 未設定 | `NotLoggedIn` | UI はトークン設定を案内（§5.2） |
| `/user` or billing が 401 | `Unauthorized`（要約 `unauthorized(401)`） | カードは「トークン無効/期限切れ」文言（§5.2） |
| `/user` or billing が 403 | `Unauthorized`（要約 `unauthorized(403)`） | fine-grained PAT の Plan:Read 不足が最頻。Enhanced Billing 対象外/個人課金外/Org管理の可能性も。`/user` 段の 403 も含め `(403)` で権限確認文言（§5.2） |
| 403/429 でレート制限シグナル（Retry-After / x-ratelimit-remaining:0 / body "rate limit"） | `RateLimited` | body はクラス判定のみ・Message 非掲載 |
| ai_credit が 404 | （フォールバックへ） | フォールバックも 404 等なら `Error`（`notFound(404)`） |
| 5xx / timeout / JSON/shape 異常 | `Error` | |
| 200 だが usageItems が配列でない | `Error`（`unexpectedShape`） | 「0 使用」と誤確定しない |
| 200 かつ usageItems 空配列 | `Available`（Used=0） | 当月消費なしは正当。`itemsTotal=0; itemsCopilot=0` |
| 200 かつ usageItems あり・Copilot AI Credits 行なし | `Available`（Used=0） | 汎用 billing fallback が Actions 等の別製品のみ返すケースを含む。`itemsTotal>0; itemsCopilot=0`（診断は §4.6） |
| 200 かつ Copilot AI Credits 行あり | `Available`（Used=Σ） | `itemsTotal>0; itemsCopilot>0` |

- CLI を起動しないため `NotInstalled` は返さない。
- 要約トークン `unauthorized(401)`/`unauthorized(403)` は `MapFailure` が返し、診断 Message（`userApi=`/`billing=`）に載る。カードはこのトークンに `(403)` を含むかで 401/403 のメッセージを切り替える（§5.2）。共有 `ProviderStatus` に HTTP コード欄は足さない（共有モデル非侵襲）ため、この要約トークンが 401/403 を伝える唯一の経路。

### 4.6 診断 Message とプライバシー
- `DiagnosticMasker.Mask(.., 160)` を通した `key=value` 短文のみ。例: `tokenPresent=true; tokenSource=env; userApi=ok; loginResolved=true; endpoint=ai_credit; billing=available; itemsTotal=..; itemsCopilot=..; usedExact=..; unit=credits;`。
- トークン・login 値・repositoryName・URL・メール・絶対パスは**一切含めない**。

---

## 5. UI 設計（通常モードの CopilotCard）【旧案・廃止 — §0／現行仕様サマリで置換】

> この §5 は「ステータス窓の通常モードに Copilot カードを追加する」旧設計であり**不採用**。現行は独立した `CopilotWindow`（専用ウィンドウ＋専用トレイ＋ Always/HoverPreview）。以下は経緯保存のため残置。

`StatusForm` は現在「Claude/Codex の2サービス固定」。Copilot 用に**専用 nested 型 `CopilotCard`** を追加し、通常モードにのみ配線する。

### 5.1 配線
- フィールド追加: `private readonly CopilotCard _copilotCard;`、`private bool _showCopilot;`、上限保持用 `private int? _copilotAllowance;`。
- `ApplySettings`: `_showCopilot = settings.IsServiceVisible("GitHub Copilot");`、`_copilotAllowance = settings.CopilotCreditAllowance();` を保持し、カードへ渡す。
- `UpdateSnapshot`: `"GitHub Copilot"` サービスを名前で取得し、Claude/Codex と同様にフォールバック（`lastSuccessfulSnapshot` から Available を補完）。`_copilotCard.Update(copilot, fallbackCopilot, _copilotAllowance)`。
- `ApplyVisibilityForMode`: `_copilotCard.Visible = _displayMode == DisplayMode.Normal && _showCopilot;`（compact/minimum では常に非表示）。
- `LayoutNormal`: Claude→Codex→**Copilot** の順に縦積み（`_showCopilot` のとき高さ加算）。`SetLoading` にも Copilot を追加。

### 5.2 カード内容（count-based）
- タイトル「GitHub Copilot」＋ブランド色（GitHub 系。Claude=青/Codex=紫と差別化。具体値は実装時に決定し、`ClaudeBrand`/`CodexBrand` と同様に定数化）。
- **上限あり（Plan 選択 or Custom）**: メイン行 `"{Used:N0} / {Allowance:N0} credits"`、サブに `"残 {Remaining:N0}（{Percent:0}%）"`、横長 `UsageBarControl`（`AccentColor`/数字色は既存 `UsageAccentColor(percent)` 流用＝**80%≥橙 / 95%≥赤**で閾値一致）。`percent = min(100, Used/Allowance*100)`、`remaining = max(0, Allowance−Used)`。
- **上限なし（Plan=None かつ Custom 未設定）**: `"{Used:N0} credits 使用（当月集計）"` ＋ 補助文 `"設定でプランを選ぶと上限・残量を表示します"`（割合・バーは出さない）。
- 状態が非 Available のとき: バッジ＋**カード専用メッセージ**を出す。既存 `FriendlyMessage` の「ログインしてください」「再ログインしてください」は GITHUB_TOKEN 運用（CLI ログインではなく env の PAT）に合わないため**使わない**。Copilot カードは状態→文言を自前で持つ:
  - `NotLoggedIn` → `"環境変数 GITHUB_TOKEN に PAT(Plan:Read) を設定してください"`。
  - `Unauthorized` かつ診断 Message に `(403)` を含む（= 403）→ `"GITHUB_TOKEN の Plan(Read) 権限、個人課金対象、Enhanced Billing 対象を確認してください"`。
  - `Unauthorized` かつ上記以外（= 401）→ `"GITHUB_TOKEN が無効または期限切れです"`。
  - `RateLimited`/`Error` はカード専用の簡潔文（前回成功値があればその旨を添える）。
- `詳細を表示`/`詳細を隠す`＋マスク済み診断は既存 ServiceCard と同様に備える。

### 5.3 リセット/当月の表示（推定）
- サブテキスト: `"当月集計 · 暦月リセット目安 {M}/1（推定）"`。既存 `ResetTimeFormatter.Format`（断定的な「あとX分（HH:mmリセット）」）は**使わない**。`ResetAtUtc` は推定値として「目安」表記に留める。

### 5.4 触らない UI
- `TrayIconRenderer`（トレイアイコン）、`TrayApplicationContext.BuildTooltip`（ツールチップ）、`UsageAccentColor`/`BrandUsageColor` の閾値定義、コンパクト/ミニマムの各パネルは**変更しない**。

---

## 6. 設定（App）

### 6.1 `AppSettings`
- 追加 enum: `internal enum CopilotPlan { None = 0, Pro = 1, ProPlus = 2, Max = 3, Custom = 4 }`。
- 追加プロパティ: `public CopilotPlan CopilotPlan { get; set; } = CopilotPlan.None;`、`public int CopilotCustomCredits { get; set; }`（Custom 時の上限。0/負値は「未設定」扱い）。
- **プラン定数の一元管理**（flex 変動時にここだけ直す。CLAUDE.md の「真実は1箇所」流儀）:
  ```
  // 同梱 AI クレジット(月)。flex 配分は市場で変動し得るため将来改訂可能性あり。変更はこの1表のみ。
  Pro = 1500, ProPlus = 7000, Max = 20000
  ```
- `public int? CopilotCreditAllowance()` → `None`:null、`Pro/ProPlus/Max`:上記定数、`Custom`:`CopilotCustomCredits > 0 ? CopilotCustomCredits : null`。
- `Normalize()`: 許可リストに **"GitHub Copilot"** を追加（現在 Claude/Codex のみ通す `Where(...)` を拡張）。`CopilotPlan` が未定義値なら `None`。`CopilotCustomCredits` を `Max(0, ..)` にクランプ。
- `Clone()`: `CopilotPlan` / `CopilotCustomCredits` をコピー。
- 既定 `VisibleServices = ["Claude","Codex"]` は**据え置き**（Copilot は既定オフ）。

### 6.2 `SettingsForm`
- 「表示対象」セクションに **`GitHub Copilot` チェックボックス**（`_showCopilot`）を追加。
- **プラン選択 `ComboBox`**（None/Pro/Pro+/Max/Custom）＋ **Custom 上限入力**（`NumericUpDown` か `TextBox`、Custom 選択時のみ有効化）。
- `LoadSettings`/`ToSettings` に Copilot トグル・プラン・Custom 上限を読み書き。`ToSettings` で `visible` に `"GitHub Copilot"` を条件追加。
- レイアウト: 既存は絶対座標。コントロール追加に伴い**以降の Y 座標とダイアログ高さ（現 462）を再配置**する（認証セクション・OK/キャンセルを下方へシフト）。

### 6.3 永続化
- `SettingsStore` は `JsonStringEnumConverter` 使用済み → 新 enum はそのまま `settings.json` に保存される（追加変更不要）。破損時は既定で起動（既存挙動）。

---

## 7. プロバイダ配線（App）— 表示 ON 時のみ実行（レビュー反映）【旧案・現行はウィンドウ on/off ゲート】

> この §7 の「`VisibleServices` に "GitHub Copilot" がある時だけ provider を組み込む」配線は**旧案**。現行は **`CopilotWindowEnabled`**（Claude/Codex は **`ClaudeCodexWindowEnabled` ＋個別サービス表示**）で provider をゲートし、`NotifyIcon` は**有効ウィンドウごとに専用**（両窓OFF時のみコントロール用1つ）。下記本文は経緯保存のため残置。

GitHub Copilot は**個人トークンで外部 API を叩く**ため、表示 OFF（`VisibleServices` に `"GitHub Copilot"` 無し）の間は provider を**そもそも生成・実行しない**。`UsageAggregator` は不変の provider list を持つだけ（`IDisposable` ではない・provider は static `HttpClient` のみで可変状態なし）なので、設定変更時に**作り直す**のが安全。

- `_aggregator` を `readonly` から外し、`BuildAggregator(AppSettings)` で組み立てる。Claude/Codex は常に含め、Copilot は `settings.IsServiceVisible("GitHub Copilot")` の時だけ含める:
  ```
  private static UsageAggregator BuildAggregator(AppSettings settings)
  {
      var providers = new List<IUsageProvider>
      {
          new ClaudeUsageProvider(),
          new CodexUsageProvider()
      };
      if (settings.IsServiceVisible("GitHub Copilot"))
      {
          providers.Add(new GitHubCopilotUsageProvider());
      }
      return new UsageAggregator(providers);
  }
  ```
  - コンストラクタ: `_aggregator = BuildAggregator(_settings);`。
  - 設定保存時は `_settings = form.ToSettings(...)` の**前に旧可視性** `var wasCopilotVisible = _settings.IsServiceVisible("GitHub Copilot");` を控え、代入後に `!= IsServiceVisible(...)` で変化したら `_aggregator = BuildAggregator(_settings);` で再構築してから `RefreshAsync()`（`_settings` は in-place で上書きされるため、旧値の事前退避が必要）。`UsageAggregator` は安価・無状態なので、判定を省いた**無条件再構築でも可**。これにより**表示 ON にした時だけ** `/user`・billing endpoint へアクセスし、OFF にしたら以後アクセスしない。
  - 前提: この可視性ゲートは §6.1 の `Normalize()` 許可リストに `"GitHub Copilot"` を追加してあること（現状の `Normalize()` は Claude/Codex 以外を捨てるため、未追加だと `IsServiceVisible("GitHub Copilot")` が常に false になり provider が**永久に組み込まれない**）。
- `RefreshAsync` の**全体失敗フォールバック**（`catch` 節で Claude/Codex の Error エントリを作る箇所）は、ハードコードの2件固定をやめ**現在の provider 構成に合わせて**生成する。Copilot が表示 ON の時だけ `"GitHub Copilot"` の Error エントリを足す（OFF の時は足さない＝表示もしないので整合）。
- フォールバック保持 `_lastSuccessfulServices`（サービス名キー）・`LastUsageStore`（Message null・数値のみ）は**汎用のまま**で Copilot にも自動適用（変更不要）。表示 OFF の間は Copilot のスナップショットが来ないので UI には出ない（`_showCopilot=false` でカード非表示・§5.1）。

---

## 8. Poc `--github-copilot` / `--raw` ランナー（本ブランチに同梱）

実アカウントで移行後 schema と権限エラーを安全に確認するため、POC ランナーを本ブランチへ移植する。

- 追加: `src/TokenChecker.Poc/GitHubCopilot/GitHubCopilotPocRunner.cs`、変更: `src/TokenChecker.Poc/Program.cs`（`--github-copilot` で分岐し、既定の Claude+Codex 出力は不変）。
- `--github-copilot`（構造化）: `GitHubCopilotUsageProvider` を `UsageAggregator` 経由で実行し JSON 出力（既定の POC と同じ JSON オプション）。
- `--github-copilot --raw`（schema 実測）: 候補エンドポイント（`ai_credit/usage` を第一に、必要なら `usage`・`premium_request/usage`）を叩き、**body は一切出力せず**、次のみ出力:
  - HTTP `status`、`x-ratelimit-remaining`、`x-github-api-version`
  - `loginResolved`（真偽のみ。login 値は出さない。URL は `{login}` 置換）
  - `usageItems` の**ホワイトリスト項目のみ**:
    - 文字列フィールド `product` / `sku` / `unitType` は**各々 `DiagnosticMasker.Mask(value, 120)` を通して出力**（万一 PII 的な値が混じっても email→`<email>`・path→`<path>`・`token=`等→`<redacted>`・長い英数字塊→`<redacted>` でマスクされる）。現行 POC の `PrintItems` はこれらを素のまま出力しているため、移植時に各フィールドへ `Mask` を追加する。null/未設定の値は `Mask` に通さず `(null)` 表記を維持する（`Mask` は null/空白を `""` に潰すため、これを通すと「フィールド不在」と「空文字」の区別＝schema 確認の手がかりが消える）。
    - 数値フィールド `quantity` / `grossQuantity` / `grossAmount` / `netQuantity` / `netAmount` と判定結果 `copilot`（`IsCopilotAiCreditUsage` の bool）は数値・真偽のためそのまま出力。
  - URL・login・`repositoryName`・未知フィールド・**body 全体は一切出力しない**（ホワイトリスト外は出さない）。
- **トークン・秘匿情報は raw でも出さない**（保存もしない）。

---

## 9. プライバシー不変条件（厳守）

- トークンは env から**読むだけ**。settings.json 等へ**保存しない**、UI/ツールチップ/ログ/raw 出力に**出さない**。入力欄も設けない。
- 診断文字列はすべて `DiagnosticMasker.Mask` を通す（email→`<email>`、path→`<path>`、`token=`/`secret=`/`key=`/`bearer=`→`<redacted>`、長い英数字塊→`<redacted>`）。マスク規則は Core の `DiagnosticMasker` 一元管理（コピーを増やさない）。
- アプリが書き込むのは **`settings.json`（設定のみ）/ `last_usage.json`（数値の使用率のみ・Message は null 化）/ `copilot_usage.json`（Copilot 差分計算用・数値と日付のみ＝対象月・最終取得UTC・当月使用クレジット・当日9:00窓の基準値。トークン・login・URL・path・email は保存しない）** の3ファイルだけ。資格情報ストアへは書かない。
- 公式 REST API のみ。Web スクレイピング禁止。

---

## 10. 変更予定ファイル（確定）

### 新規（Core）
- `src/TokenChecker.Core/Providers/GitHubCopilot/GitHubCopilotUsageProvider.cs`（2段エンドポイント＋`decimal` 集計（§4.3）＋ステータス。`MapFailure` は 401→`unauthorized(401)` / 403→`unauthorized(403)` の要約トークンを返す（§4.5/§5.2）。usageItems 配列ありで Copilot AI Credits 行 0 件は `Available`/Used=0/`itemsCopilot=0`（§4.5））
- `src/TokenChecker.Core/Providers/GitHubCopilot/GitHubBillingUsageParser.cs`（`GrossAmount` 捕捉を追加、credits 計算用フィールド（`grossQuantity`/`quantity`/`grossAmount`）は `decimal?` 取得、狭めた堅牢判定 `IsCopilotAiCreditUsage`（premium request / `unitType=requests` を明示除外・§4.2）、候補フィールド全捕捉）

### 新規（Poc）
- `src/TokenChecker.Poc/GitHubCopilot/GitHubCopilotPocRunner.cs`（`--raw` の `product`/`sku`/`unitType` は `DiagnosticMasker.Mask(value,120)` 経由で出力・§8。`copilot=` フラグは `IsCopilotAiCreditUsage`）

### 変更（App ＋ Poc ＋ doc）
- `src/TokenChecker.App/AppSettings.cs`（`CopilotPlan` enum・`CopilotCustomCredits`・`CopilotCreditAllowance()`・定数一元化・`Normalize()` 許可リスト・`Clone()`）
- `src/TokenChecker.App/SettingsForm.cs`（Copilot トグル＋プラン選択＋Custom 上限＋レイアウト再配置）
- `src/TokenChecker.App/StatusForm.cs`（`CopilotCard` 追加・通常モード配線。Copilot 専用の非 Available 文言＝`NotLoggedIn`／401／403 を出し分け、`FriendlyMessage` の「再ログイン」系は使わない・§5.2）
- `src/TokenChecker.App/TrayApplicationContext.cs`（`BuildAggregator(settings)` で Copilot を**可視性で条件配線**・設定変更時に aggregator 再構築・全体失敗フォールバックも可視性連動で Copilot を出し分け・§7）
- `src/TokenChecker.Poc/Program.cs`（`--github-copilot` 分岐）
- `README.md`（GitHub Copilot 節: 有効化手順／env PAT（Plan:Read）／プラン選択・Custom／制約: Enhanced Billing Platform・個人課金のみ・上限はプラン既知値・通常モードのみ・トレイ非対応・暦月近似）

### 変更しない（設計上の不変点）
- `TrayIconRenderer.cs` / ツールチップ（`BuildTooltip`）
- 共有モデル `ServiceUsage.cs` / `RateLimitWindow.cs` / `ProviderStatus.cs`
- `UsageAggregator.cs` / `LastUsageStore.cs` / `SettingsStore.cs` / `DiagnosticMasker.cs`
- `StatusForm` のコンパクト/ミニマム各パネル、`UsageAccentColor`/`BrandUsageColor` の閾値定義

---

## 11. 検証計画【旧案・廃止 — テストスイートと CI ゲートに置き換え済み】

> この §11 は「テストプロジェクトは無し」前提の旧検証計画であり、現在は `dotnet test`（xUnit）＋ GitHub Actions CI に置き換え済み（`notes/2026-06-07-test-suite-plan.md` 参照）。以下は経緯保存のため残置。

1. 再ビルド前に実行中インスタンス停止: `Stop-Process -Name TokenChecker -Force -ErrorAction SilentlyContinue`。
2. `dotnet build`（ソリューション全体）が通ること。
3. `dotnet run --project src/TokenChecker.Poc -- --github-copilot`（トークン未設定なら `NotLoggedIn`）。
4. ユーザーが PAT を設定して `--github-copilot --raw` を1回実行し、**移行後の実フィールド**（product/sku/unitType/grossQuantity/grossAmount …）を確認 → 集計フィールドの最終確定。
5. `dotnet run --project src/TokenChecker.App -- --show-status` で通常モードを開く:
   - Copilot 表示 OFF（既定）: カードは出ず、`--raw` ログや HTTP 計測で `/user`・billing への**アクセスが発生しない**ことを確認（item 3）。設定で ON にすると以後アクセスが始まり、OFF に戻すと止まる。
   - `GITHUB_TOKEN` 未設定（Copilot ON）: Copilot カードがトークン設定の案内文を表示。
   - 設定で Copilot ON＋プラン選択（および Custom）: 使用量/上限/残量/割合・バーが崩れず表示。
   - Plan=None: 使用量のみ＋プラン選択の案内。
   - 401/403 文言（item 7）: 無効トークンで `"GITHUB_TOKEN が無効または期限切れです"`、権限不足（403）で `"GITHUB_TOKEN の Plan(Read) 権限、個人課金対象、Enhanced Billing 対象を確認してください"` が出ること（「再ログイン」系が出ないこと）。
6. 設定 `settings.json` を一時変更した場合は確認後に元へ復元。最終的な見た目はユーザーに目視確認を依頼。

---

## 12. 既知の制約・リスク

- **上限はプラン既知値（または手入力）**。「プラン値はハードコードしない」方針からの**意図的逸脱**（ユーザー合意）。flex 配分変動で陳腐化し得る → 定数1箇所集約＋Custom で緩和。
- **集計フィールドは移行後 schema 未実測**。`unitType==ai-credits`/`credits` の `grossQuantity` を主、`grossAmount×100m`（`decimal`）を確認用フォールバックとし、`--raw` の実測で確定する（推測でハードコードしない）。判定は狭めて premium request 行を除外する（§4.2）。
- **リセット日は暦月近似の推定**。請求サイクルが署名日基準だと暦月とずれ得る。API から取得できないため UI は「当月集計（推定）」表記。
- Enhanced Billing Platform 対象外 / Org・Enterprise 管理の Copilot は個人 billing endpoint に現れない（403/404 になり得る）。
- `X-GitHub-Api-Version` は移行に伴い要調整の可能性。

---

## 13. 今回対象外・将来課題

- コンパクト/ミニマムモードおよびトレイアイコン/ツールチップへの Copilot 反映（UI 方針を整理した後続ブランチ）。
- AI Credits の overage（超過課金）表示や budget（追加支出上限）連携。
- 請求サイクル（署名日基準）の正確なリセット日取得手段が公開された場合の対応。

---

## 14. 実装順序（writing-plans へ引き継ぐ際の目安）

1. Core: `GitHubBillingUsageParser`（堅牢判定＋`GrossAmount`）→ `GitHubCopilotUsageProvider`（2段エンドポイント＋集計＋ステータス）。
2. Poc: `GitHubCopilotPocRunner` ＋ `Program.cs` 分岐（schema 実測の足場）。
3. App: `AppSettings`（enum/allowance/Normalize/Clone）→ `SettingsForm`（UI）→ `StatusForm`（CopilotCard）→ `TrayApplicationContext`（配線）。
4. README 追記。
5. `dotnet build` ＋ POC ＋ `--show-status` 目視。

> 注: 本ブランチでは**ユーザーの明示指示があるまで実装・コミット・push・tag を行わない**。Codex レビューは節目でユーザーが別途依頼する。
