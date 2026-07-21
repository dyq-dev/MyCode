# AGENTS.md

## Project

Local AI Assistant Demo — .NET 8 WPF client (MVVM) with a pluggable LLM backend. Chat works today via Ollama or any OpenAI-compatible cloud API. Memory (fact extraction + session summary) and Code RAG (ProjectScanner phase) are implemented.

## Build & Run

```
dotnet build AI.Assistant.slnx
dotnet run --project AI.Assistant.Client
dotnet test AI.Assistant.Tests
```

Requires the Windows Desktop SDK (`net8.0-windows`). Will not build or run on Linux/macOS. Tests run on any platform.

## Solution Structure

```
AI.Assistant.slnx
├── AI.Assistant.Core/                # net8.0 — Models + Interfaces only (no dependencies)
│   ├── Interfaces/                   # IChatService, IEmbeddingService, IVectorStore, IMemoryFilter
│   ├── Models/                       # ChatMessage, ExtractedFact, MemoryFilterResult, KnowledgeContext
│   └── Rag/                          # Code RAG module (namespace-isolated)
│       ├── Interfaces/               # IProjectScanner
│       ├── Models/                   # CodeFile
│       └── Options/                  # RagOptions
│
├── AI.Assistant.Infrastructure/      # net8.0 — All service implementations + DI
│   ├── Services/
│   │   ├── (Memory, Chat, Embedding, VectorStore)
│   │   └── Rag/                      # Code RAG implementations
│   │       ├── Scanner/              # ProjectScanner
│   │       ├── Chunking/             # WholeFileChunkStrategy + ChunkManager
│   │       ├── Storage/              # CodeIndexStore (IVectorStore + IQdrantIndexStorage)
│   │       ├── Retrieval/            # (planned)
│   │       └── Indexing/             # CodeIndexer
│   └── Extensions/
│       ├── ServiceCollectionExtensions.cs    # AddInfrastructure()
│       └── RagServiceCollectionExtensions.cs # AddRag()
│
├── AI.Assistant.Client/              # net8.0-windows — WPF app (MVVM)
│
└── AI.Assistant.Tests/               # net8.0 — xUnit tests
    └── ProjectScannerTests.cs        # 18 tests covering filter, hash, encoding
```

Dependency direction: Client → Infrastructure → Core (Core depends on nothing). Tests → Core + Infrastructure.

## Memory Module

- Three-layer retrieval: Session Summary (MSSQL) → Facts (Qdrant payload) → Raw text (Qdrant + MSSQL)
- `IMemoryFilter` — rule-based, skips greetings/thanks/meaningless content
- Fact extraction via LLM with `[category] content` structured output
- Categories: user_profile, preference, project, technical, requirement, experience, other
- `SessionSummaryRecord`: Version, CreatedAt, UpdatedAt tracking

## Code RAG Module (Phase 1)

- `IProjectScanner` — scans project dir, filters by `RagOptions`, computes SHA256, detects encoding (UTF-8/BOM/fallback)
- Architecture doc: `docs/superpowers/specs/2026-07-21-code-rag-design.md`
- Future phases: ChunkManager + IChunkStrategy → ICodeIndexStore → ICodeRetriever → ICodeIndexer
- Shared infrastructure: `IEmbeddingService`, `IVectorStore` (reused from Core, collection: `code_rag`)

## LLM Provider Configuration (appsettings.json)

Chat is selected at runtime via `LLM:ChatProvider` / `LLM:EmbeddingProvider` = `"Ollama"` or `"Cloud"`. Values are read in `App.xaml.cs:30-49` and bound into `InfrastructureOptions` (see `ServiceCollectionExtensions.cs`). Chat and Embedding providers are independent — each can be local Ollama or a different cloud.

- Chat implementations: `OllamaChatService` (JSONL stream, `api/chat`) and `OpenAICompatibleChatService` (SSE stream, `chat/completions`, adds `Bearer` auth).
- `IChatService` exposes both `SendAsync` (non-streaming) and `StreamAsync` (IAsyncEnumerable). The UI only uses `StreamAsync`.

## Key Conventions

- **MVVM**: Use `[ObservableProperty]` / `[RelayCommand]` from CommunityToolkit.Mvvm. Never implement `INotifyPropertyChanged` by hand.
- **DI**: Wired in `App.xaml.cs` via `Host.CreateDefaultBuilder` + `AddInfrastructure(...)`. `MainViewModel` and `MainWindow` are singletons; `ConversationViewModel` is transient.
- **Shared infra, isolated business**: `IEmbeddingService`/`IVectorStore` are reused across Memory and RAG; business models and services are fully separated by namespace (`Rag/` vs flat `Models/`).
- **Demo fallback**: If no `IChatService` is registered, `ConversationViewModel` runs a fake "[Demo] 收到消息" reply — keep this path working when changing send logic.

## Implementation Status

- `IChatService`: ✅ implemented (Ollama + OpenAI-compatible).
- `IEmbeddingService` (`OllamaEmbeddingService`, `OpenAICompatibleEmbeddingService`): ✅ implemented.
- `IVectorStore` (`QdrantVectorStore`): ✅ implemented (gRPC, session isolation filter fixed).
- `IMemoryFilter`: ✅ implemented (rule-based).
- `MemoryService`: ✅ fact extraction + session summary + three-layer retrieval.
- `IProjectScanner`: ✅ implemented (18 tests).
- `IIndexComparer` (`IndexComparer`): ✅ implemented (O(n) dict-based, case-insensitive, 16 tests).
- `IChunkStrategy` (`WholeFileChunkStrategy`) + `IChunkManager` (`ChunkManager`): ✅ implemented (24 tests).
- `ICodeIndexStore` (`CodeIndexStore`): ✅ implemented (16 tests, via `IVectorStore` + `IQdrantIndexStorage`).
- `ICodeIndexer` (`CodeIndexer`): ✅ implemented (20 tests, upsert-first cleanup-last).
- `ICodeRetriever`: 📋 designed, not yet implemented.

## Gotchas

- `appsettings.json` is loaded with `optional: false` (`App.xaml.cs:21`); a missing/malformed file crashes startup. It is copied to the output dir via the Client `.csproj`.
- Cloud API keys go in `appsettings.json` (plaintext) — there is no secret management; do not commit real keys.
- Default models differ between code (`gemma3:1b`) and `appsettings.json` (`qwen2.5:1.5b`); the config value wins.
- Streaming reads use `HttpCompletionOption.ResponseHeadersRead` + line-by-line parsing; JSON parse failures are swallowed per-line (return null) so a bad chunk is skipped rather than aborting the stream.

## Dependencies

- CommunityToolkit.Mvvm 8.4.0
- Microsoft.Extensions.DependencyInjection / Hosting / Http 8.0.1
- Microsoft.Extensions.Configuration.Json (via Host defaults) for appsettings
- Dapper 2.1.79, Microsoft.Data.SqlClient 5.2.2
- Qdrant.Client 1.18.1 (gRPC)
- xUnit 2.9.3, Microsoft.NET.Test.Sdk 17.14.1 (tests only)
