# Model providers

> Part of [OperationsCopilot](../README.md), a demonstration project.
> See [Limits and caveats](../README.md#limits-and-caveats) before reusing any of it.

Two settings, chosen independently:

```json
"Ai": {
  "ChatProvider":      "Ollama",   // Ollama | AzureOpenAI
  "EmbeddingProvider": "Ollama"    // Ollama | AzureOpenAI | Deterministic
}
```

| Provider | Chat | Embeddings | Notes |
|---|:---:|:---:|---|
| `Ollama` | yes | yes | Local, free, offline. Default. |
| `AzureOpenAI` | yes | yes | Entra ID or API key. |
| `Deterministic` | — | yes | Hashed bag-of-words, in process. Testing only. |

Mixing is deliberate and useful: a local chat model with Azure embeddings keeps retrieval quality
while cutting the per-token cost that dominates, and the reverse is handy when you have cloud
chat but want indexing to stay on your machine.

**`Deterministic` is not available for chat.** Retrieval has a usable local stand-in; deciding
which tools to call does not. That constraint is enforced at startup rather than discovered on
the first request.

## Ollama

The chat model **must support tool calling** — this agent does nothing without it. `qwen2.5`,
`llama3.1`, `llama3.2` and `mistral-nemo` do; many small general-purpose models do not, and will
fail by answering from thin air instead of calling a tool.

```bash
ollama pull qwen2.5:14b        # chat, supports tool calling
ollama pull nomic-embed-text   # embeddings, 768 dimensions
```

Ollama is reached through its **OpenAI-compatible API** at `/v1`, not its native one, so it goes
through the same Semantic Kernel connector as Azure OpenAI. Automatic function calling therefore
takes an identical code path on both providers — one behaviour to reason about instead of two.

## Embedding dimensions must match the model

This is the one setting that will bite you. A pgvector column has a fixed width, and models
disagree:

| Model | Dimensions |
|---|---|
| `nomic-embed-text` | 768 |
| `mxbai-embed-large`, `bge-m3` | 1024 |
| `all-minilm` | 384 |
| `text-embedding-3-small` | 1536 |
| `text-embedding-3-large` | 3072 |

Set `Ollama:EmbeddingDimensions` or `AzureOpenAI:EmbeddingDimensions` to match. If you get it
wrong the app fails on the first embedding call with a message naming the real cause, rather than
surfacing an opaque Postgres type error later.

**Switching embedding providers rebuilds the vector column and re-indexes automatically.**
`VectorSchema` compares the configured width against the live column on startup and, when they
differ, drops the index, clears `document_chunks`, alters the column, and rebuilds — after which
the indexer repopulates from the Markdown source. Clearing is not a shortcut: vectors from two
different models are not comparable, so a mixed index returns nonsense. Re-indexing after a model
change is mandatory however the schema is managed.

Note that pgvector will not build an HNSW index above **2000 dimensions**. Above that the column
still works but searches become sequential scans; the startup log says so explicitly.

## Model capability and combined questions

The evaluation suite turned up a finding worth stating plainly, because it decides which model
you should run.

Combining a database tool with the knowledge base in one turn — the thing this project exists to
demonstrate — needs a model that will chain two tool calls before answering. Measured on this
repository's own golden set, over all 14 labelled questions:

| Model | Mean recall | Mean precision | Fully correct | Combined questions chained |
|---|---|---|---|---|
| `qwen2.5:7b` | 0.857 | 1.000 | 10 / 14 | **0 / 4** |
| `qwen2.5:14b` | **1.000** | **1.000** | **14 / 14** | **4 / 4** |

The 7b row is not bad work: it picks the right single tool essentially every time and never once
called a tool it did not need. All four of its failures are the same failure. Asked *"which
products need reordering, and how much should I order according to our policy?"* it calls
`GetLowStockProducts`, stops, and writes the ordering rule from memory — an answer that reads as
confident and cites nothing, which is the exact failure this design exists to prevent.

That is not a prompting problem. The system prompt was made explicitly procedural about it — a
numbered two-check rule with a worked example of this precise question — and 7b's behaviour did
not change. 14b chains reliably with the same prompt.

So the default is **qwen2.5:14b**, which answers every question in the golden set correctly.
Drop to 7b only if single-tool questions are all you need; it is noticeably faster.

The wider point is that this is invisible to every other kind of test. Unit tests, integration
tests and the offline evaluations all pass on both models, because none of them involve the model
choosing anything. Only the live tier catches it, which is why it is worth being able to run for
free.

---
