# テストコード整備プラン（2026-06-07）: xUnit テストスイート ＋ CI ゲート

**Status: 完了（v0.8.x 時点で全テスト実装済み・CI 稼働中）**

## 概要

このファイルは xUnit テストスイートの新設計画として作成された。計画した不変条件テスト（80%/95% 閾値・プライバシーマスキング・Copilot allowance・予測ロジック・設定マイグレーション・プロバイダ失敗分離・Normalize/Clone）はすべて実装済み。GitHub Actions CI（`.github/workflows/ci.yml`）が push/PR ごとに windows-latest 上で `dotnet test` を実行するゲートも稼働中。

## 現在の tests/ 配下ファイル一覧

### TokenChecker.Core.Tests（`net10.0`）
- `DiagnosticMaskerTests.cs` — masking 規則（email/path/token=/secret=/key=/bearer=/英数字塊）＋切詰め・null/空・maxLength
- `DiagnosticMaskerEdgeTests.cs` — エッジケース補完
- `GitHubBillingUsageParserTests.cs` — Normalize・IsCopilotAiCreditUsage・decimal 合計・TryParseUsageItems
- `UsageAggregatorTests.cs` — 全成功/1例外→Error/空プロバイダ/キャンセル
- `ClaudeUsageStatusMapperTests.cs` — HTTP ステータスマッピング
- `CodexAccountClassifierTests.cs` — account.type 判定

### TokenChecker.App.Tests（`net10.0-windows`）
- `AppSettingsTests.cs` — CopilotCreditAllowance（Free=200 ほか）・CopilotPlanTitle・IsServiceVisible・provider gates・Normalize・Clone
- `UsageThemeTests.cs` — WarningPercent==80・CriticalPercent==95・AccentColor 境界
- `CopilotUsageTrackerTests.cs` — 予測（ローカル暦1日起点・到達/非到達/AlreadyFull/Insufficient）・本日増分（baseline・マイナス無し・月替わり破棄）
- `SettingsStoreMigrationTests.cs` — ApplyLegacyMigrations 全分岐
- `LocalizationTests.cs` — Strings テーブルの EN/JA 双方非空・キー対応確認
- `TodayDeltaSeverityTests.cs` — 当日ペース閾値（4%/5%）の色分岐
- `TrayBurnMarkTests.cs` — 本日バーン警告マークの表示条件
- `DetermineStateTests.cs` — TrayIconRenderer.DetermineState の Normal/Warning/Danger/Error 分岐
