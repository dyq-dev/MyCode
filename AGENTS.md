# AGENTS.md

## Project

Local AI Assistant Demo — .NET 8 WPF client (MVVM) with a pluggable LLM backend. Chat works today via Ollama or any OpenAI-compatible cloud API; RAG (embeddings + Qdrant) is scaffolded but not yet implemented.

## Build & Run

```
dotnet build AI.Assistant.slnx
dotnet run --project AI.Assistant.Client
```

Requires the Windows Desktop SDK (`net8.0-windows`). Will not build or run on Linux/macOS. No tests are configured.

## Solution Structure

```
AI.Assistant.slnx
├── AI.Assistant.Core/           # net8.0 — Models + Interfaces only (no dependencies)
│   ├── Models/                  # ChatMessage, Conversation, MessageRole, ChatMessageViewModel
│   └── Interfaces/              # IChatService, IEmbeddingService, IVectorStore
├── AI.Assistant.Infrastructure/ # net8.0 — HTTP service implementations + DI
│   ├── Services/                # Ollama*, OpenAICompatible* (chat), Qdrant* (placeholder)
│   └── Extensions/              # AddInfrastructure() + InfrastructureOptions
└── AI.Assistant.Client/         # net8.0-windows — WPF app
    ├── ViewModels/              # MainViewModel, ConversationViewModel (CommunityToolkit.Mvvm)
    ├── Views/                   # MainWindow (ChatGPT-style layout)
    ├── Converters/              # BoolToVisibilityConverter
    ├── Themes/                  # Generic.xaml
    └── appsettings.json         # LLM provider + Qdrant config (required, not optional)
```

Dependency direction: Client → Infrastructure → Core (Core depends on nothing).

## LLM Provider Configuration (appsettings.json)

Chat is selected at runtime via `LLM:ChatProvider` / `LLM:EmbeddingProvider` = `"Ollama"` or `"Cloud"`. Values are read in `App.xaml.cs:30-49` and bound into `InfrastructureOptions` (see `ServiceCollectionExtensions.cs`). Chat and Embedding providers are independent — each can be local Ollama or a different cloud.

- Chat implementations: `OllamaChatService` (JSONL stream, `api/chat`) and `OpenAICompatibleChatService` (SSE stream, `chat/completions`, adds `Bearer` auth).
- `IChatService` exposes both `SendAsync` (non-streaming) and `StreamAsync` (IAsyncEnumerable). The UI only uses `StreamAsync`.

## Key Conventions

- **MVVM**: Use `[ObservableProperty]` / `[RelayCommand]` from CommunityToolkit.Mvvm. Never implement `INotifyPropertyChanged` by hand.
- **DI**: Wired in `App.xaml.cs` via `Host.CreateDefaultBuilder` + `AddInfrastructure(...)`. `MainViewModel` and `MainWindow` are singletons; `ConversationViewModel` is transient.
- **Demo fallback**: If no `IChatService` is registered, `ConversationViewModel` runs a fake "[Demo] 收到消息" reply — keep this path working when changing send logic.

## Implementation Status (what throws)

- `IChatService`: ✅ implemented (Ollama + OpenAI-compatible).
- `IEmbeddingService` (`OllamaEmbeddingService`, `OpenAICompatibleEmbeddingService`): ❌ `NotImplementedException`.
- `IVectorStore` (`QdrantVectorStore`): ❌ `NotImplementedException`. RAG endpoints (`/collections/{name}/points`) are stubbed.

Do NOT assume placeholder services are finished — they will throw at runtime.

## Gotchas

- `appsettings.json` is loaded with `optional: false` (`App.xaml.cs:21`); a missing/malformed file crashes startup. It is copied to the output dir via the Client `.csproj`.
- Cloud API keys go in `appsettings.json` (plaintext) — there is no secret management; do not commit real keys.
- Default models differ between code (`gemma3:1b`) and `appsettings.json` (`qwen2.5:1.5b`); the config value wins.
- Streaming reads use `HttpCompletionOption.ResponseHeadersRead` + line-by-line parsing; JSON parse failures are swallowed per-line (return null) so a bad chunk is skipped rather than aborting the stream.

## Dependencies

- CommunityToolkit.Mvvm 8.4.0
- Microsoft.Extensions.DependencyInjection / Hosting / Http 8.0.1
- Microsoft.Extensions.Configuration.Json (via Host defaults) for appsettings
