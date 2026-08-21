# Example queries and responses

> Part of [OperationsCopilot](../README.md), a demonstration project.
> See [Limits and caveats](../README.md#limits-and-caveats) before reusing any of it.

> **On latency.** The `latencyMs` values below are from Azure OpenAI. A local model is far
> slower: on an M-series Mac, qwen2.5:14b answers a two-tool question in roughly **two minutes**,
> almost all of it model inference — the tool calls themselves were 518 ms and 390 ms in that
> same run. Local costs nothing and keeps data on your machine; it does not match hosted latency.

The seeded dataset is fixed (26 products, 52 stock rows, ~1,850 sales lines over 180 days), so
these figures are reproducible. Sales dates are anchored to the current date, so date ranges shift
with when you run it. Answer prose is illustrative — the model writes it — but the `citations`,
`toolCalls` and figures below are real output shapes from real data.

## 1. Database only

```bash
curl -s -X POST http://localhost:5080/api/chat -H 'Content-Type: application/json' \
  -d '{"message":"Which products are running low on stock?"}'
```

```jsonc
{
  "answer": "Eight stock rows are at or below their reorder threshold, worst shortfall first:\n\n| SKU | Product | Warehouse | On hand | Threshold | Short by |\n|---|---|---|---|---|---|\n| HT-4002 | Ironvale Ratchet Screwdriver | WH-EU-01 | 13 | 54 | 41 |\n| EL-2005 | Nordsen Laser Distance Meter 60m | WH-AP-01 | 7 | 32 | 25 |\n| HT-4001 | Ironvale Combination Spanner Set 12pc | WH-NA-01 | 12 | 36 | 24 |\n| CN-5002 | Torqline Impact Bit Set 32pc | WH-EU-01 | 38 | 61 | 23 |\n| EL-2002 | Voltek Thermal Camera TC-120 | WH-EU-01 | 5 | 14 | 9 |\n| PT-1004 | Corvex Reciprocating Saw | WH-NA-01 | 11 | 19 | 8 |\n| CN-5001 | Corvex Cutting Disc 125mm (25 pack) | WH-EU-01 | 70 | 72 | 2 |\n| SE-3004 | Aeroshield Respirator Half Mask | WH-AP-01 | 27 | 28 | 1 |\n\nCN-5001 and SE-3004 have only just crossed their thresholds; the top four are well below.",
  "conversationId": "0199a3f1c4d27b8e9f6a12b4c8e05d3a",
  "citations": [],
  "toolCalls": [
    {
      "pluginName": "Operations",
      "functionName": "GetLowStockProducts",
      "arguments": { "limit": "25" },
      "durationMs": 34,
      "succeeded": true,
      "name": "Operations.GetLowStockProducts"
    }
  ],
  "latencyMs": 1180,
  "usage": { "promptTokens": 1642, "completionTokens": 268, "totalTokens": 1910 }
}
```

`citations` is empty by design: those figures came from the database, not from a document. Citing
them would misrepresent where they came from.

## 2. Knowledge base only

```bash
curl -s -X POST http://localhost:5080/api/chat -H 'Content-Type: application/json' \
  -d '{"message":"What is the restocking fee on opened goods that are returned?"}'
```

```jsonc
{
  "answer": "Opened goods in resalable condition can be returned within 30 days subject to a 15% restocking fee [1]. Business customers on a framework agreement get a 60-day window and are exempt from the fee on their first two returns each quarter [1].",
  "conversationId": "0199a3f1d80e7c42b1359ae7f0c26d18",
  "citations": [
    {
      "reference": "[1]",
      "sourceFile": "returns-and-warranty-policy.md",
      "documentTitle": "Aurora Supply Co. Returns and Warranty Policy",
      "heading": "Standard Return Window",
      "excerpt": "Customers may return unopened goods within 30 calendar days of delivery for a full refund. Opened goods in resalable condition may be returned within 30 days subject to a 15% restocking fee. After 30 days, returns are accepted only under warranty. Business customers on a framework agreement have a 60-day return window…",
      "score": 0.5003
    }
  ],
  "toolCalls": [
    {
      "pluginName": "KnowledgeBase",
      "functionName": "SearchKnowledgeBase",
      "arguments": { "query": "restocking fee opened goods returns" },
      "durationMs": 12,
      "succeeded": true,
      "name": "KnowledgeBase.SearchKnowledgeBase"
    }
  ],
  "latencyMs": 940
}
```

## 3. Both, combined — the interesting case

```bash
curl -s -X POST http://localhost:5080/api/chat -H 'Content-Type: application/json' \
  -d '{"message":"Which products need reordering, and how much should I order according to our policy?"}'
```

```jsonc
{
  "answer": "Eight stock rows are at or below their reorder point. Policy is to order back to the reorder threshold **plus one full lead-time cycle of demand** \u2014 ordering only up to the threshold is called out as the most common cause of repeat stockouts [1].\n\n| SKU | Product | Warehouse | On hand | Threshold | Short by | Supplier |\n|---|---|---|---|---|---|---|\n| HT-4002 | Ironvale Ratchet Screwdriver | WH-EU-01 | 13 | 54 | 41 | Ironvale Tooling |\n| EL-2005 | Nordsen Laser Distance Meter 60m | WH-AP-01 | 7 | 32 | 25 | Nordsen Electronics |\n| HT-4001 | Ironvale Combination Spanner Set 12pc | WH-NA-01 | 12 | 36 | 24 | Ironvale Tooling |\n| CN-5002 | Torqline Impact Bit Set 32pc | WH-EU-01 | 38 | 61 | 23 | Torqline Industrial |\n| EL-2002 | Voltek Thermal Camera TC-120 | WH-EU-01 | 5 | 14 | 9 | Voltek Instruments |\n| PT-1004 | Corvex Reciprocating Saw | WH-NA-01 | 11 | 19 | 8 | Corvex Power |\n| CN-5001 | Corvex Cutting Disc 125mm (25 pack) | WH-EU-01 | 70 | 72 | 2 | Corvex Power |\n| SE-3004 | Aeroshield Respirator Half Mask | WH-AP-01 | 27 | 28 | 1 | Aeroshield Protective |\n\nFour of these are **critical** rather than merely low, meaning at or below half the threshold [2]: HT-4002 (13 of 54), EL-2005 (7 of 32), HT-4001 (12 of 36) and EL-2002 (5 of 14). Those escalate to the Operations Director and the supplier account manager immediately, rather than the usual two business days.",
  "conversationId": "0199a3f2118c7d4a86b0e5f39c714a02",
  "citations": [
    {
      "reference": "[1]",
      "sourceFile": "inventory-policy.md",
      "documentTitle": "Aurora Supply Co. Inventory and Replenishment Policy",
      "heading": "Replenishment Triggers",
      "excerpt": "Purchase orders should bring stock back to the reorder threshold plus one full lead-time cycle of demand, not merely back to the threshold. Ordering only to the threshold is the single most common cause of repeat stockouts.",
      "score": 0.4118
    },
    {
      "reference": "[2]",
      "sourceFile": "inventory-policy.md",
      "documentTitle": "Aurora Supply Co. Inventory and Replenishment Policy",
      "heading": "Reorder Thresholds",
      "excerpt": "Every product carries a per-warehouse reorder threshold. Stock is considered low when quantity on hand is at or below that threshold, and critical when it is at or below half the threshold.",
      "score": 0.3874
    }
  ],
  "toolCalls": [
    { "pluginName": "Operations", "functionName": "GetLowStockProducts", "arguments": { "limit": "25" }, "durationMs": 31, "succeeded": true, "name": "Operations.GetLowStockProducts" },
    { "pluginName": "KnowledgeBase", "functionName": "SearchKnowledgeBase", "arguments": { "query": "how much to order when stock falls below reorder threshold" }, "durationMs": 14, "succeeded": true, "name": "KnowledgeBase.SearchKnowledgeBase" }
  ],
  "latencyMs": 2470
}
```

The agent called two tools of its own accord, applied the written rule to the specific rows it
found, and worked out which of them cross the "critical" line the policy defines — a threshold
that exists only in the Markdown, and is nowhere in the database or the code. Nothing told it to
do any of that.

## 4. Sales analysis

```bash
curl -s -X POST http://localhost:5080/api/chat -H 'Content-Type: application/json' \
  -d '{"message":"How did each category sell over the last 30 days?"}'
```

The tool returns real aggregates like these:

| Category | Revenue | Units | Order lines |
|---|---|---|---|
| Power Tools | 249,377.14 | 1,108 | 45 |
| Electronics | 205,719.10 | 1,309 | 56 |
| Safety Equipment | 136,480.47 | 2,063 | 74 |
| Hand Tools | 102,414.01 | 2,203 | 56 |
| Consumables | 49,007.96 | 1,572 | 88 |

## 5. Follow-up in the same conversation

```bash
curl -s -X POST http://localhost:5080/api/chat -H 'Content-Type: application/json' \
  -d '{"message":"And how should we price the discontinued one?","conversationId":"0199a3f2118c7d4a86b0e5f39c714a02"}'
```

Pass back the `conversationId` and the agent gets the earlier turns, so "the discontinued one"
resolves. History is capped at 12 turns with a one-hour sliding expiry.

## More questions to try

```text
Tell me about PT-1001.
What needs reordering in the Rotterdam warehouse?
Who has to approve a 20% discount?
How long is the warranty on safety equipment?
What is the standard lead time for a Tier 2 supplier?
Is the stock figure for EL-2002 still trustworthy under our cycle counting policy?
Are any low-stock items at the critical level our inventory policy defines?
PT-1006 is discontinued — how should I price the remaining stock?
```

---
