# GitHub Copilot POC — 調査ログ

実行して分かったことを記録するテンプレートです。**トークンや個人情報は書かないでください**（種別のみ可）。
`--raw` 出力を貼るときは **手動で再マスク**してから貼ること。

## 前提（毎回確認）

- 対象は **個人アカウントに直接課金される Copilot 利用量のみ**。Organization / Enterprise 管理の利用量は含まない。
- 対象 API は **billing usage API**（`/users/{login}/settings/billing/usage`）。旧/新いずれの **Copilot metrics API はスコープ外**。
- billing usage endpoint は **Enhanced Billing Platform 対象**アカウントのみ。**fine-grained PAT は User permissions の「Plan: Read」が必要**。
- **`403` / `404` / `503` は権限不足とは限らず**、対象外アカウント・未対応プラットフォーム・一時障害の可能性もある。
- **2026-06-01** から従量課金（AI Credits）へ移行。**AI Credits の対象は主に Chat / CLI / Cloud Agent 等で、コード補完と Next Edit Suggestions は有料プランでは対象外**。

## 観測ログ: 2026-05-31

`--github-copilot`（構造化）を実行。**Status = `Unauthorized`**。

観測した Message（マスク済み・そのまま記録可）:

```
tokenPresent=true; tokenSource=env; userApi=ok; loginResolved=true; billing=unauthorized(403);
```

**解釈**: `GITHUB_TOKEN` 自体は有効で `GET /user` は成功（`userApi=ok` / `loginResolved=true`）。
しかし billing usage API が **403** を返している（`billing=unauthorized(403)`）。考えられる原因:

1. **権限不足**: トークンに billing/Plan 権限が無い。fine-grained PAT の場合は **User permissions の「Plan: Read」が未付与**。
2. **Enhanced Billing Platform 対象外**: 当該アカウントが拡張請求基盤に未対応。
3. **個人課金ではない**: Copilot が **Organization / Enterprise 管理**で、個人 billing endpoint には現れない。

→ POC としては「認証は通るが個人 billing usage は本トークンでは取得不可（403）」を確認。
次アクションは下記「次アクション」を参照（Plan: Read 付き fine-grained PAT で再検証）。

## 実行環境

- 実行日(UTC): 2026-05-31
- 使用トークンの**種別のみ**: <!-- 値は書かない。今回使用したトークンの種別を記入（classic PAT / fine-grained PAT / GitHub App） -->
- fine-grained の場合の権限: <!-- 今回: Plan 権限は未確認/未付与の可能性 -->
- Copilot プラン種別: <!-- 例: Pro / Pro+ / Business(org) / 不明 -->

## エンドポイント別の結果

| エンドポイント | HTTP ステータス | 備考 |
|---|---|---|
| `GET /user` | 200 | login 解決成功（`userApi=ok`） |
| `GET /users/{login}/settings/billing/usage` | 403 | `billing=unauthorized(403)`。Plan:Read 不足 / 対象外 / Org・Ent 管理の可能性 |
| `GET /users/{login}/settings/billing/premium_request/usage` | <!-- 未確認（--raw で確認） --> | best-effort 確認 |
| `GET /users/{login}/settings/billing/usage/summary` | <!-- 未確認（--raw で確認） --> | best-effort 確認 |

## 観測した実フィールド（usageItems）

`--raw` で観測した各 item の実フィールドを記録（最終マッピング決定の材料）。**値は最小限・再マスク済みで**:

| product | sku | unitType | netQuantity | netAmount | Copilot 該当? |
|---|---|---|---|---|---|
| <!-- 例: Copilot --> | | | | | |

- `product` の distinct 値: <!-- 例: ["Copilot", "Actions", ...] -->
- Copilot 相当の判定に使えそうなフィールド: <!-- product? sku? -->

## 暫定マッピングの妥当性

- 現状の暫定: `product`/`sku` に "copilot" を緩く含む行を `Used = Σ(netQuantity ?? quantity)` で集計。
- 観測を踏まえた最終マッピング案: <!-- raw 確認後に記入 -->

## 2026-06-01 課金変更の影響

- 変更前(〜2026-05-31)に観測したフィールド: <!-- -->
- 変更後(2026-06-01〜)に観測したフィールド差分: <!-- AI Credits 関連の product/sku 変化など -->

## 結論 / 次アクション

- 個人トークンで Copilot 利用量を取得できたか: **条件付き不可（2026-05-31 時点）**。`/user` は成功するが billing usage は 403。
- 次アクション（最優先）: **fine-grained PAT に User permissions「Plan: Read」を付与**したトークンで再検証する（手順は README「403 が出たときの再検証」参照）。
  - 再検証で 200 が返れば → 個人課金で取得可能。`--raw` で `product/sku` を確認し最終マッピングを確定。
  - なお 403/404 が続く場合 → Enhanced Billing Platform 対象外、または Org/Enterprise 管理（個人 billing には現れない）。その場合この POC のアプローチでは個人利用量は取得不可。
- UI 統合に進む価値があるか: <!-- 再検証の結果を見て判断 -->
- 残課題: <!-- 再検証結果、product/sku 実値、最終マッピング -->
