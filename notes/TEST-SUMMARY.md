# TokenCheckerWin テスト結果

- 実行: `dotnet test` (Debug) / 日付ラベル 2026-06-11（refactor/organize ブランチ）
- **結果: ✅ ALL PASSED**
- 合計 **179** 件 / 合格 **179** / 失敗 **0**

## プロジェクト別

| プロジェクト | 合計 | 合格 | 失敗 | スキップ |
|---|---:|---:|---:|---:|
| TokenChecker.Core.Tests (net10.0) | 73 | 73 | 0 | 0 |
| TokenChecker.App.Tests (net10.0-windows) | 106 | 106 | 0 | 0 |

## テストクラス別（合格 / 合計）

| テストクラス | 合格 | 合計 |
|---|---:|---:|
| ClaudeUsageStatusMapperTests | 13 | 13 |
| CodexAccountClassifierTests | 7 | 7 |
| CodexErrorSummarizerTests | 5 | 5 |
| DiagnosticMaskerEdgeTests | 8 | 8 |
| DiagnosticMaskerTests | 12 | 12 |
| GitHubBillingUsageParserTests | 24 | 24 |
| UsageAggregatorTests | 4 | 4 |
| AppSettingsTests | 37 | 37 |
| AtomicFileTests | 4 | 4 |
| CopilotUsageTrackerTests | 12 | 12 |
| DetermineStateTests | 9 | 9 |
| LocalizationTests | 12 | 12 |
| SettingsStoreMigrationTests | 7 | 7 |
| TodayDeltaSeverityTests | 9 | 9 |
| TrayBurnMarkTests | 10 | 10 |
| UsageThemeTests | 6 | 6 |

## 失敗したテスト

なし。全テスト合格。

## 添付（リポジトリの outputs/tests/、git 追跡外）
- `core.trx` / `app.trx` — Visual Studio / VSTest で開ける機械可読レポート
- `test-output.txt` — テスト実行のコンソールログ全文
