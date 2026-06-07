# テストコード整備プラン（2026-06-07）: xUnit テストスイート ＋ CI ゲート

> 注: 本ファイルにあった第1弾〜第7弾（GitHub Copilot プロバイダ／専用ウィンドウ／表示方法／初回設定ウィザード／Copilot カード調整／テーマ／ダークフラット化／Free プラン追加）の旧仕様は **すべて実装・コミット済みで v0.7.0 としてリリース済み**のため削除した。現在オープンな計画は以下のテストスイート整備のみ。

## Context

このリポジトリには現在テストが一切無い（`CLAUDE.md`「テストプロジェクトは無い（`dotnet test` 対象なし）。検証はビルド + POC + 実起動の目視で行う。」）。今後の追加開発（プロバイダ改修・閾値・プライバシー・Copilot ロジック）で**回帰を機械的に止める**ため、アプリの不変条件を固定する **xUnit 自動テスト** を新設する。さらにユーザー要望「**通らないと完了しないもの**」を満たすため **GitHub Actions CI** を追加し、push / PR ごとに `dotnet test` を走らせて失敗を赤チェックで強制する（＋ `CLAUDE.md` に commit/release 前の `dotnet test` グリーン必須を明記）。

確定事項（ユーザー回答）: フレームワーク = **xUnit**、ゲート = **GitHub Actions CI**。

## 守るべき不変条件（テストが固定する対象）
- **80% / 95% 閾値**（`UsageTheme.WarningPercent=80` / `CriticalPercent=95` と `AccentColor` の境界）。
- **プライバシー masking**（`DiagnosticMasker.Mask`: email/path/`token=`/`secret=`/`key=`/`authorization=`/`bearer=`/長い英数字塊 → マスク、`maxLength` 切詰め）。
- **Copilot allowance**（`AppSettings.CopilotCreditAllowance`: **Free=200** / Pro=1500 / Pro+=7000 / Max=20000 / Custom / None）と `CopilotPlanTitle`。
- **Copilot 予測 ＋ 本日9:00増分**（`CopilotUsageTracker`: ローカル暦1日起点予測・到達/非到達/AlreadyFull・baseline・マイナス無し・月替わり破棄）。
- **設定マイグレーション**（`SettingsStore.ApplyLegacyMigrations`: CompactMode→DisplayMode 等、フィールド不在ゲート）。
- **プロバイダ失敗の分離**（`UsageAggregator`: 1プロバイダ例外→そのサービスのみ `Error`、他は継続）。
- **設定 Normalize / Clone**（interval クランプ・VisibleServices を Claude/Codex のみに整流・enum 既定化・配列独立コピー）。

## アーキテクチャ
`tests/` 配下に **2つの xUnit プロジェクト**（アセンブリの TFM に合わせる）:
- `tests/TokenChecker.Core.Tests`（`net10.0`）→ Core の **public API** を直接テスト（シーム不要）。
- `tests/TokenChecker.App.Tests`（`net10.0-windows` ＋ `UseWindowsForms`）→ App の **internal ロジック**を `InternalsVisibleTo` 経由でテスト。

`TokenCheckerWin.sln` に両プロジェクトを登録し、`dotnet test`（ソリューション）で一括実行。`.github/workflows/ci.yml` が push/PR で `dotnet test` を windows-latest 上で実行（App.Tests が `net10.0-windows` のため **windows-latest 必須**）。

## 新規ファイル

### テストプロジェクト
- `tests/TokenChecker.Core.Tests/TokenChecker.Core.Tests.csproj`
  - `Directory.Build.props` から `net10.0` 継承。`<IsPackable>false</IsPackable>`。パッケージ: `Microsoft.NET.Test.Sdk` / `xunit` / `xunit.runner.visualstudio` / `coverlet.collector`。`ProjectReference` → Core。
- `tests/TokenChecker.App.Tests/TokenChecker.App.Tests.csproj`
  - `<TargetFramework>net10.0-windows</TargetFramework>` ＋ `<UseWindowsForms>true</UseWindowsForms>`（WinForms の App と `System.Drawing.Color` 参照に必要）で `Directory.Build.props` の net10.0 を上書き。同じテストパッケージ。`ProjectReference` → App。

### テストクラス（Core.Tests・シーム不要）
- `DiagnosticMaskerTests.cs` — 5 規則＋切詰め＋null/空＋複合文字列＋`maxLength`(160/400)。
- `GitHubBillingUsageParserTests.cs` — `Normalize`、`IsCopilotAiCreditUsage`（premium-request 除外／strong unit/sku/product／weak generic は copilot 文脈時のみ true／文脈無し generic → false）、`CreditsForRow`（3段フォールバック）、`ParseUsageItems`/`TryParseUsageItems`（配列／不在→false／非配列→false）、`TryGetLogin`、**合計の decimal 加算→1回 AwayFromZero 丸め**の検証。
- `UsageAggregatorTests.cs` — `FakeUsageProvider`（`IUsageProvider` 実装）で: 全成功→全 Available／1つ例外→当該のみ `Error`＋安全 Message・他は Available／空プロバイダ→空 Services／`CapturedAtUtc` セット／キャンセル時 `OperationCanceledException` 再スロー（握り潰さない）。

### テストクラス（App.Tests・`InternalsVisibleTo` 経由）
- `AppSettingsTests.cs` — `CopilotCreditAllowance`（**Free=200** ほか全分岐・Custom 0/負→null・未定義 enum→null）、`CopilotPlanTitle`（Free→"Copilot Free" ほか・Custom 書式）、`IsServiceVisible`（大小無視/null/空）、provider gates 各組合せ（窓 off は全 gate false）、`Normalize`（interval クランプ／VisibleServices を Claude/Codex のみ・"GitHub Copilot" 除去・大小無視 dedupe・null→[]／enum 未定義→既定／`CopilotCustomCredits` Max(0,..)／`CompactMode` ミラー）、`Clone`（全項目コピー・配列独立）。
- `UsageThemeTests.cs` — `WarningPercent==80`・`CriticalPercent==95`、`AccentColor(value)` 境界を **`UsageTheme.Good/Warning/Bad/MutedText` と比較**（79.9→Good／80→Warning／94.9→Warning／95→Bad／100→Bad／150 クランプ→Bad／null・NaN・±Inf→MutedText）、`AccentColor(value, baseColor)` は <80 で baseColor・80/95 で Warning/Bad。
- `CopilotUsageTrackerTests.cs` — **temp-file ストアシーム**経由。**「now」はローカル壁時計から構築**（`new DateTime(...,DateTimeKind.Local)` → `new DateTimeOffset(localDt)`）して**ランナー TZ 非依存**にする（dev=JST／CI=UTC でも同結果）。予測（no-allowance/used<=0/elapsed<0.5→Insufficient、used>=cap→AlreadyFull、リセット跨ぎ→NotThisMonth、到達→ReachesThisMonth＋日付<翌月ローカル1日）。本日増分（初回 baseline 無→null＝未計測、pre-09:00→post-09:00 同窓→delta=差分、使用量減→0 でマイナス無し、allowance 有→% 算出、stale 前サンプル→未計測、月替わり→baseline 破棄）。
- `SettingsStoreMigrationTests.cs` — `SettingsStore.ApplyLegacyMigrations(json, settings)`（internal 化）に手組み JSON ＋ AppSettings を渡して全分岐: CompactMode→DisplayMode（DisplayMode 不在時のみ）、ShowOnStartup:false→ClaudeCodex/Copilot HoverPreview、CopilotWindowTrigger 在→Copilot HoverPreview、VisibleServices に "GitHub Copilot"→`CopilotWindowEnabled=true`、TrayIconMode:"Copilot"→true、各 NEW フィールド在→非マイグレート。**SettingsStore 全体の Load は path シーム回避のため対象外**（migration は static メソッド直叩きで十分）。

## App 側シーム（非挙動・最小）
- `src/TokenChecker.App/SettingsStore.cs`: `private static void ApplyLegacyMigrations` → **`internal static`**（1語のみ）。
- `src/TokenChecker.App/CopilotUsageTracker.cs`:
  - `CopilotUsageStore` に `internal CopilotUsageStore(string path) { _path = path; }` を追加（既定 ctor 併存）。
  - `CopilotUsageTracker` に `internal CopilotUsageTracker(CopilotUsageStore store)` を追加し、既定 ctor を `public CopilotUsageTracker() : this(new CopilotUsageStore()) {}` に。
  - **`copilot_usage.json` のスキーマ（レコード項目）は不変**。テストが実ファイルを汚さないための path 注入のみ。
- `src/TokenChecker.App/TokenChecker.App.csproj`: `<ItemGroup><InternalsVisibleTo Include="TokenChecker.App.Tests" /></ItemGroup>`（Core 側は public のため不要）。

## ソリューション / CI / ドキュメント
- `TokenCheckerWin.sln`: `dotnet sln add` で 2 テストプロジェクトを登録。
- `.github/workflows/ci.yml`（新規）: `on: [push, pull_request]`（branches: main）／`runs-on: windows-latest`／steps: checkout → `actions/setup-dotnet`（`global.json` の 10.0.100 を解決）→ `dotnet restore` → `dotnet build -c Release --no-restore` → `dotnet test -c Release --no-build --verbosity normal`。
- `CLAUDE.md`: 「テストプロジェクトは無い…」の行を差し替え → `tests/` にテストあり・`dotnet test`（ソリューション）が **commit/release 前にグリーン必須**・CI が push/PR で実行・本スイートが上記不変条件を固定するので該当領域変更時は緑を維持、と明記。
- （任意）`README.md` に短い「テスト」節（`dotnet test`）。
- `.gitignore` は `TestResults/`・`bin/`・`obj/` 既に無視済み＝変更不要。

## パッケージ
net10 互換の安定版を実装時に `dotnet add package` で解決し csproj に固定: `Microsoft.NET.Test.Sdk`(17.x) / `xunit`(2.9.x) / `xunit.runner.visualstudio`(2.8.x) / `coverlet.collector`(6.x)。

## 検証（エンドツーエンド）
1. `Stop-Process -Name TokenChecker -Force -EA SilentlyContinue`（実行中だと exe ロックでビルド失敗）。
2. `dotnet build`（ソリューション）→ 0/0（テスト2件も含めてビルド）。
3. `dotnet test`（リポジトリルート）→ 全件グリーン（目安 80〜100 件）。2回実行して TZ/並列の不安定が無いことを確認。
4. TZ 非依存の確認: tracker テストはローカル壁時計から now を構築するため JST/UTC 双方で同結果。
5. CI YAML が push で発火し緑チェックが出ること（次回 push で確認）。
6. 不変条件が実際に assert されているか（テスト名で確認）: masking 規則・80/95 境界・Free=200。
7. `git status`: 追加は test ファイル＋小シーム＋sln/ci/CLAUDE.md のみ。Core provider/parser の挙動・`copilot_usage.json` スキーマ・token 経路は不変。

## スコープ外（明示）
- ネットワーク/プロセス起動/実クレデンシャル読取を伴うテストは作らない。Claude/Codex/Copilot プロバイダの HTTP/プロセス status-mapping は async IO に埋め込まれ単体化困難なため**今回は対象外**（純粋な `GitHubBillingUsageParser` のみ）。将来 `MapFailure` 等を public 抽出すれば拡張可（メモのみ）。
- `copilot_usage.json` スキーマ・token 処理・80/95 閾値**値**の変更は行わない。

## リスク / 注意
- 本番コード変更は **小シーム3点（可視性1語＋internal ctor 2つ）＋ csproj の InternalsVisibleTo 1行**のみ。すべて非挙動・スキーマ/トークン経路不変。
- App.Tests は `net10.0-windows`（WinForms App 参照）。よって CI は windows-latest。
- 決定性: tracker 各テストは固有 temp ファイル＋ローカル壁時計構築で、xUnit のクラス並列でも安定。
