# 実験: GitHub Copilot 利用状況プロバイダ POC

TokenCheckerWin に将来 GitHub Copilot 用 `IUsageProvider` を追加できるかを検証する **実験的 POC** の作業ディレクトリです。
本 POC のゴールは「個人トークンで GitHub 公式 REST API から Copilot の利用状況を取得し、その JSON を標準出力に出せるか」を確かめることだけです。**UI 統合は対象外**で、`TokenChecker.App` には一切手を入れません。

## このブランチ

- ブランチ: `feature/github-copilot-provider-poc`（`main` を壊さない）
- 候補プロダクトコード: `src/TokenChecker.Core/Providers/GitHubCopilot/`
- POC ランナー: `src/TokenChecker.Poc/GitHubCopilot/`（既定の POC 出力は変更しない。`--github-copilot` フラグでのみ起動する opt-in）

## 何を取得するか（対象スコープ）

- **対象は「個人アカウントに直接課金される Copilot 利用量」のみ。**
  `GET /users/{login}/settings/billing/usage` は、その個人アカウントに請求される利用量を返します。
  **Organization / Enterprise が管理・課金する Copilot 利用量（org/ent 配下のシート消費）は、この個人 billing endpoint には現れません。**
- GitHub には Claude / Codex のような **リアルタイムのレート制限ウィンドウ**（`utilization%` + `resets_at`）は **存在しません**。
  個人トークンで取得できるのは **月次の billing/usage 消費量** です。月次の「残量(remaining)」は API では公開されていません（プラン値のハードコードはしません）。

## 対象 API（取り違え注意）

本 POC が叩くのは **billing usage API** だけです:

- `GET https://api.github.com/user` … 認証ユーザーの `login` 解決用
- `GET https://api.github.com/users/{login}/settings/billing/usage?year&month` … 当月の利用量（`usageItems[]`）

次のものは **本 POC のスコープ外** です（混同しないこと）:

- 旧 Copilot metrics API: `/orgs/{org}/copilot/usage`、`/orgs/{org}/copilot/metrics`（非推奨/廃止系）
- 新 Copilot usage metrics API: `/orgs/{org}/copilot/metrics/reports/...`（集計 CSV のダウンロードリンクを返す、org/ent 管理者向け）
- → **Organization / Enterprise の利用状況分析（旧・新いずれの metrics API も）はスコープ外。** 本 POC は個人の billing usage API のみを対象とします。

## 認証とプラットフォーム要件（重要）

- **認証情報は環境変数 `GITHUB_TOKEN` からのみ読み取ります。** ファイルや認証ストアからは読みません。トークンを source / json / appsettings / ログ / 本ドキュメントに **絶対に書かない**でください。
- billing usage endpoint は **Enhanced Billing Platform（拡張請求基盤）対象**のアカウントでのみ動作します。
- **fine-grained PAT** で叩く場合は、**User permissions の「Plan」を Read** に設定する必要があります。
- **`403` / `404` / `503` は「権限不足」だけが原因とは限りません。** 対象外アカウント（Enhanced Billing Platform 未対象）や、未対応プラットフォーム／一時的なサービス状態の可能性もあります。POC ではこれらを区別せず、まず生レスポンスとステータスを観測して切り分けます。

## AI Credits に関する注意（2026-06-01）

- **2026-06-01** から全 Copilot プランが従量課金（AI Credits）へ移行します。billing usage のフィールド名・概念が変わり得るため、POC は **生 JSON ダンプ経路**（`--raw`）を備え、スキーマを決め打ちしません。
- **AI Credits の対象は主に Chat / CLI / Cloud Agent などです。コード補完（code completion）と Next Edit Suggestions は、有料プランでは AI Credits の対象外**です。したがって billing usage に現れる Copilot 消費は補完系を含まない可能性が高い点に留意してください。
- ドキュメントでは相対表現（「明日」等）を使わず、**絶対日付（例: 2026-06-01）**で記載します。

## 実行方法

リポジトリのルート（`C:\dev\TokenCheckerWin`）から実行します（`src/...` 相対パスはルート以外だと `MSB1009` で落ちます）。

```powershell
# 1) 既定 POC（Claude + Codex のみ。本実験では変更されない）
dotnet run --project src/TokenChecker.Poc

# 2) Copilot プロバイダを構造化 JSON で実行（トークン未設定なら NotLoggedIn になる）
dotnet run --project src/TokenChecker.Poc -- --github-copilot

# 3) 生スキーマ確認（マスク済みの本文 + 各 usageItem の product/sku/unitType/netQuantity/netAmount を一覧）
dotnet run --project src/TokenChecker.Poc -- --github-copilot --raw
```

トークンを設定して実行する場合（**履歴・画面にトークンを残さない**）:

```powershell
# 例: 既に環境にある GITHUB_TOKEN を使う。無ければ安全に入力する。
$env:GITHUB_TOKEN = (Read-Host -AsSecureString | ConvertFrom-SecureString -AsPlainText)
dotnet run --project src/TokenChecker.Poc -- --github-copilot
```

## 403 が出たときの再検証（fine-grained PAT + Plan: Read）

2026-05-31 の検証では `/user` は成功（認証は有効）するものの、billing usage API が **403** を返した
（Message: `userApi=ok; loginResolved=true; billing=unauthorized(403);`）。原因は **権限不足 / Enhanced Billing Platform 対象外 / Org・Enterprise 管理** のいずれか。まず権限不足を切り分けるため、**Plan: Read を付けた fine-grained PAT** で再検証する。

手順:

1. GitHub → **Settings → Developer settings → Personal access tokens → Fine-grained tokens → Generate new token**
   （URL: `https://github.com/settings/personal-access-tokens`）
2. **Resource owner** を **自分の個人アカウント**にする（個人課金の利用量を見たいので、Organization を選ばない）。
3. **Permissions → Account permissions → 「Plan」** を **Access: Read-only** に設定する（＝ User permissions の **Plan: Read**）。
   - これが billing/usage 系エンドポイントを個人アカウントで読むために必要な権限。
4. トークンを発行（**値はコピーしてもどこにも保存しない**）。
5. そのトークンで再実行（**画面・履歴・この会話にトークンを残さない**形で）:

   ```powershell
   $env:GITHUB_TOKEN = (Read-Host 'Paste fine-grained PAT (hidden)' -AsSecureString | ConvertFrom-SecureString -AsPlainText)
   dotnet run --project src/TokenChecker.Poc -- --github-copilot
   dotnet run --project src/TokenChecker.Poc -- --github-copilot --raw
   Remove-Item Env:GITHUB_TOKEN -ErrorAction SilentlyContinue
   ```

6. 結果の読み方:
   - **`Available`** → 個人課金で取得可能。`--raw` で `product/sku/unitType/netQuantity/netAmount` を確認し、最終的な Copilot マッピングを `findings.md` に確定する。
   - **まだ `Unauthorized`（`billing=unauthorized(403)`）/ `Error`（`billing=notFound(404)`）** → 権限ではなく **Enhanced Billing Platform 対象外**、または Copilot が **Org/Enterprise 管理**（個人 billing endpoint には現れない）の可能性が高い。その場合、本 POC の「個人 billing usage」アプローチでは取得できない（組織メトリクスはスコープ外）。

> 注: fine-grained PAT が billing 系で使えるかはプラットフォーム移行（2026-06-01 の従量課金化）に伴い変わり得る。403/404/503 は権限だけでなく対象外アカウント・未対応プラットフォーム・一時障害の可能性もある点に留意。

### 再検証の結果（2026-05-31）

- **User permissions「Plan: Read」を付与した fine-grained PAT で `Available` になった。** 通常 PAT では billing usage が 403 だったが、Plan: Read 付与で 200 を取得。
- 取得できた当月の **Copilot Premium Request の利用量 = 1488.9 requests**（Copilot 該当 17 件の `quantity` 合計）。
- **確定した Copilot 利用量マッピング**:
  - Copilot 判定 = `product==copilot` **かつ** `sku∈{Copilot Premium Request, copilot_premium_request}` **かつ** `unitType==requests`（厳格一致）。
  - 利用量 = `quantity`（無ければ `grossQuantity`）の合計。**`netQuantity` は 0/null のため集計に使わない**。`netAmount`（=0）は割引後金額であり課金額ではない。
  - `RateLimitWindow.Used`（`long?`）は丸め値、精密な小数は診断 Message の `usedExact=1488.9; unit=requests;` に出す（共有モデルは非侵襲で維持）。
  - 月次の上限（Limit/Remaining/UsedPercent）は API 非公開のため `null`。`ResetAtUtc` は翌月1日 0時 UTC の計算値。
- 詳細は `findings.md` を参照。

## プライバシー規則（厳守）

- トークン・OAuth 認証情報・メール・絶対パスを、出力・ログ・保存ファイル・本リポジトリのどこにも書かない。
- 診断文字列はすべて `TokenChecker.Core.DiagnosticMasker.Mask(value, maxLength)` を通す。
- `--raw` の出力をこのフォルダの `findings.md` に貼るときは、**手動で再マスクしてから**貼る（`login`・`repositoryName`・URL 中のユーザー名なども落とす）。
- 公式 REST API のみ。Web スクレイピング禁止。
