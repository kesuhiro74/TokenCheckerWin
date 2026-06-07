# TokenCheckerWin テスト結果

- 実行: `dotnet test` (Debug, --no-build) / 日付ラベル 2026-06-07
- **結果: ✅ ALL PASSED**
- 合計 **102** 件 / 合格 **102** / 失敗 **0**

## プロジェクト別

| プロジェクト | 合計 | 合格 | 失敗 | スキップ |
|---|---:|---:|---:|---:|
| TokenChecker.Core.Tests (net10.0) | 40 | 40 | 0 | 0 |
| TokenChecker.App.Tests (net10.0-windows) | 62 | 62 | 0 | 0 |

## テストクラス別（合格 / 合計）

| テストクラス | 合格 | 合計 |
|---|---:|---:|
| AppSettingsTests | 37 | 37 |
| CopilotUsageTrackerTests | 12 | 12 |
| DiagnosticMaskerTests | 12 | 12 |
| GitHubBillingUsageParserTests | 24 | 24 |
| SettingsStoreMigrationTests | 7 | 7 |
| UsageAggregatorTests | 4 | 4 |
| UsageThemeTests | 6 | 6 |

## 失敗したテスト

なし。全テスト合格。

## 添付（同フォルダ outputs/tests/）
- `core.trx` / `app.trx` — Visual Studio / VSTest で開ける機械可読レポート
- `test-output.txt` — 各テスト名を含む詳細コンソールログ全文
