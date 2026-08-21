# The evaluation suite

> Part of [OperationsCopilot](../README.md), a demonstration project.
> See [Limits and caveats](../README.md#limits-and-caveats) before reusing any of it.

Retrieval quality and tool selection are measurable. Treating them as a matter of judgement is how
a RAG system quietly degrades: someone edits the system prompt, retrieval gets worse, and nobody
notices until a user is confidently told the wrong warranty period.

The suite runs in two tiers.

## Offline tier — always runs, free, deterministic

Uses the deterministic embedding provider and a scripted chat model, so it is identical on every
machine and costs nothing. This is what runs on every commit.

| Suite | What it measures |
|---|---|
| `RagRetrievalEvaluationTests` | Recall@5, MRR and top-1 accuracy over 15 labelled queries |
| `ScoreDistributionTests` | The similarity floor, against measured on-topic vs off-topic scores |
| `ToolCatalogueEvaluationTests` | That every tool and parameter is described well enough to be chosen correctly |
| `AgentPipelineTests` | The full turn: tool → recorder → citations → response, with a scripted model |
| `OperationsRepositoryTests` | The three database tools against real PostgreSQL |

Current measured retrieval performance:

```
MRR                 0.922
Recall@5            0.967
Top-1 accuracy      0.867
Cases               15
```

Thresholds are set **below** observed performance with headroom (MRR ≥ 0.80, recall ≥ 0.80,
top-1 ≥ 0.70), so ordinary variation does not fail the build while a genuine regression does.

`RagRetrievalEvaluationTests` prints a per-query table, so a failure tells you *which* question
broke:

```
PASS  rr=1.00  recall@5=1.00  top=inventory-policy.md          How is the reorder threshold calculated?
PASS  rr=1.00  recall@5=1.00  top=supplier-management.md       What is the standard lead time for a Tier 2 supplier?
PASS  rr=0.50  recall@5=1.00  top=product-catalog-guide.md     What should we do when a product goes out of stock?
```

> **Read the offline retrieval numbers for what they are.** They measure *lexical* retrieval,
> because the deterministic provider matches on shared vocabulary. They prove chunking, indexing,
> ranking and filtering work; a real embedding model should comfortably beat them. A failure here
> means the pipeline broke, not that the model got worse.

## Live tier — needs a real model

Runs against whichever provider the machine has: Azure OpenAI when `AZURE_OPENAI_ENDPOINT` is
set, otherwise a local Ollama server with the configured chat model pulled. It skips only when
neither is available.

That matters more than it sounds. Tying this tier to cloud credentials meant almost nobody ran
it; with Ollama it costs nothing, so it can run before every prompt or tool-description change —
which is exactly when tool selection silently degrades.

`LiveToolSelectionEvaluationTests` runs 14 labelled questions against the real model and scores
which tools it chose:

- **Mean recall ≥ 0.80** — did it call the tools it needed? A miss means an invented answer.
- **Mean precision ≥ 0.65** — did it avoid calling ones it did not need? An extra call only costs
  latency, so this bar is deliberately lower.
- **≥ 50% of combined questions used both a database tool and the knowledge base** — answering
  half a combined question is the failure mode that matters most, because the reply reads as
  authoritative while the rule, or the data, was invented.

Thresholds allow slack: model output is not deterministic, and a suite that fails one run in five
teaches people to ignore it. The per-question output matters more than the pass/fail — it shows
*where* a model is weak, not just that it is.

The two bars separate cleanly in practice. qwen2.5:7b clears recall and precision comfortably
(0.857 and 1.000) while failing the combined-questions bar outright at 0/4, because it never
chains two tools. That is the split described in
[model capability](model-providers.md#model-capability-and-combined-questions), and it is exactly the kind of thing
a single pass/fail number would have hidden.

Be aware of the run time locally: the full live tier takes about **13 minutes** against
qwen2.5:14b on an M-series Mac, since it puts 20-odd questions through a local model. It is a
coffee-break run, not something for a pre-commit hook. CI excludes it explicitly with
`--filter "Category!=LiveModel"`.

Two further live checks assert the answer itself, not just the tool choice: a policy question must
produce a citation from the right document, and a stock question must name the products the tool
actually returned — compared against the database rather than a hardcoded list.

## Extending the golden sets

Both live in one file each, deliberately:

- Retrieval: [`RetrievalGoldenSet.cs`](../tests/OperationsCopilot.EvaluationTests/Rag/RetrievalGoldenSet.cs)
- Tool selection: [`ToolSelectionGoldenSet.cs`](../tests/OperationsCopilot.EvaluationTests/Tools/ToolSelectionGoldenSet.cs)

When a real question answers badly, add it. A regression that is not in the golden set is a
regression nobody notices.

---
