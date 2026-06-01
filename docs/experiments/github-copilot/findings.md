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

## 観測ログ: 2026-05-31（再検証: Plan: Read 付き fine-grained PAT）

**User permissions の「Plan: Read」を付与した fine-grained PAT** で再実行したところ、**Status = `Available`** になった。
個人課金の Copilot 利用量が個人 billing usage endpoint から取得できることを確認。

観測した Message（マスク済み・現行実装に対応）:

```
tokenPresent=true; tokenSource=env; userApi=ok; loginResolved=true; billing=available; itemsTotal=17; itemsCopilot=17; usedExact=1488.9; unit=requests;
```

> 診断ハードニング（`Harden GitHub Copilot POC diagnostics`）後の形式。丸め値 `used` と `resetAt=computed` は **`RateLimitWindow` 側（`Used`=1489 / `ResetAtUtc`）に持たせ、Message からは省略**した（`DiagnosticMasker.Mask` の 160 文字上限に収め、構造化データとの重複を避けるため）。精密な小数は `usedExact=1488.9; unit=requests;` として Message にのみ出る。

`--raw` で各 `usageItem` の実フィールドを確認した結果:

- **product**: `copilot` / `Copilot`
- **sku**: `Copilot Premium Request` / `copilot_premium_request`
- **unitType**: `Requests` / `requests`
- **quantity**: 実消費量（リクエスト数）。Copilot 該当行の合計 = **1488.9 requests**
- **grossQuantity**: quantity と同系の総量フィールド（quantity が無い場合のフォールバックに使用）
- **netQuantity**: **0 または null** → 利用量集計には**使わない**
- **netAmount**: **0** → 課金額ではなく割引後金額

**確定マッピング**: 月次利用量 = **1488.9 requests**（`quantity` 合計、無ければ `grossQuantity`）。
`RateLimitWindow.Used`（`long?`）は丸め値 `1489`、`ResetAtUtc` は翌月1日 UTC の計算値。精密値 `1488.9` は Message の `usedExact=1488.9; unit=requests;` に出す（共有モデルは非侵襲で維持）。

### App 統合の懸念（スコープ外・将来課題）

既存 UI（トレイ／ステータス窓）は **Claude/Codex の 5時間・週次の使用率（`UsedPercent`）** を前提に配色・バー・リングを描く。一方 Copilot は **月次の Premium Requests（件数）** で、API が上限を出さないため **`UsedPercent=null` / `Limit=null` / `Remaining=null`**。このままでは既存 UI に**そのまま載せられない**（割合エスカレーションが成立しない）。App 統合する場合は「当月の Copilot Premium Request 消費量」という**件数ベースの別表示**が要る。本 POC では UI 統合はスコープ外とし、将来課題として記録する。

## 実行環境

- 実行日(UTC): 2026-05-31
- 使用トークンの**種別のみ**: fine-grained PAT（再検証時。値は書かない）
- fine-grained の場合の権限: **User permissions「Plan: Read」付与**（これで billing usage が 200 に変化）
- Copilot プラン種別: <!-- 例: Pro / Pro+ / Business(org) / 不明 -->

## エンドポイント別の結果

| エンドポイント | HTTP ステータス | 備考 |
|---|---|---|
| `GET /user` | 200 | login 解決成功（`userApi=ok`） |
| `GET /users/{login}/settings/billing/usage`（通常 PAT） | 403 | `billing=unauthorized(403)`。Plan:Read 不足が原因 |
| `GET /users/{login}/settings/billing/usage`（**Plan:Read 付き fine-grained PAT**） | **200** | `billing=available; itemsTotal=17; itemsCopilot=17;`。取得成功 |

## 観測した実フィールド（usageItems）

`--raw` で観測した各 item の実フィールドを記録（最終マッピング決定の材料）。**値は最小限・再マスク済みで**:

| product | sku | unitType | quantity | grossQuantity | netQuantity | netAmount | Copilot 該当? |
|---|---|---|---|---|---|---|---|
| `copilot` / `Copilot` | `Copilot Premium Request` / `copilot_premium_request` | `Requests` / `requests` | 実消費量（合計 1488.9） | 総量（quantity 無し時のフォールバック） | **0 / null** | **0** | ✅ |

- Copilot 該当 17 件の `quantity` 合計 = **1488.9 requests**。
- 判定に使うフィールド: **product（=copilot）＋ sku（=Copilot Premium Request 系）＋ unitType（=requests）の3点**で厳格化。
- **集計に使うフィールド**: `quantity`（無ければ `grossQuantity`）。**`netQuantity` は 0/null のため使わない**。`netAmount` は 0（割引後金額であり課金額ではない）。

## 暫定マッピングの妥当性

- 旧暫定（不採用）: `product`/`sku` に "copilot" を緩く含む行を `Used = Σ(netQuantity ?? quantity)` で集計。
  → `netQuantity` が **0（非 null）** だと `??` が `quantity` にフォールバックせず 0 が加算される不具合があった。
- **確定マッピング（採用）**:
  1. Copilot 判定 = `product==copilot` **かつ** `sku∈{Copilot Premium Request, copilot_premium_request}`（空白/アンダースコアを除去して正規化比較） **かつ** `unitType==requests`。
  2. `Used = Σ(quantity ?? grossQuantity ?? 0)`。`netQuantity` は使わない。
  3. `RateLimitWindow.Used`（`long?`）は丸め値（例 1489）、精密値は Message に `usedExact=1488.9; unit=requests;`。
  4. `Limit / Remaining / UsedPercent` は引き続き `null`（API が枠を出さない）。
  5. `ResetAtUtc` は翌月1日 0時 UTC の計算値、`WindowDurationMins=43200`（月次の公称値）。

## 2026-06-01 課金変更の影響

- 変更前(〜2026-05-31)に観測したフィールド: <!-- -->
- 変更後(2026-06-01〜)に観測したフィールド差分: <!-- AI Credits 関連の product/sku 変化など -->

## 結論 / 次アクション

- 個人トークンで Copilot 利用量を取得できたか: **取得可能（2026-05-31 時点）**。ただし条件あり:
  - **通常 PAT では billing usage が 403**（権限不足）。
  - **User permissions「Plan: Read」を付与した fine-grained PAT で 200 Available**。月次 Copilot Premium Request = **1488.9 requests** を取得。
  - 前提として **Enhanced Billing Platform 対象アカウント**かつ **個人課金**（Org/Enterprise 管理の利用量はこの endpoint には現れない）。
- 最終マッピング: 上記「暫定マッピングの妥当性 → 確定マッピング」で確定。`quantity`/`grossQuantity` 採用、`netQuantity` 不使用。
- UI 統合に進む価値があるか: リアルタイムのレート制限窓は無く**月次消費量のみ**のため、Claude/Codex と同じ「5時間/週次の使用率」UI には乗らない。出すなら「当月の Copilot Premium Request 消費量」という別表示になる点を踏まえて別途判断。
- 残課題: Copilot プラン種別ごとの月次上限（Limit）は API 非公開のため未取得。2026-06-01 の従量課金移行後にフィールド差分を再観測する。
