# MyCode Code RAG 架构设计

日期：2026-07-21
状态：设计稿（待实现）

---

## 设计原则

- **共享基础设施，不共享业务实现** — `IEmbeddingService`、`IVectorStore` 全模块复用；业务模型和服务完全隔离
- **保持三项目结构** — 不新增 Class Library，所有代码放在 Core / Infrastructure / Client
- **模块按命名空间和目录隔离** — RAG 全部放在 `Core/Rag/` 和 `Infrastructure/Services/Rag/`，不混入 Memory 代码
- **接口抽象，可替换** — 每个模块面向接口编程，实现可随时切换

---

## 目录结构

### Core/Rag/

```
AI.Assistant.Core/
├── Rag/Interfaces/
│   ├── IProjectScanner.cs         文件扫描
│   ├── IIndexComparer.cs          变更检测
│   ├── IChunkStrategy.cs          切分策略
│   ├── IChunkManager.cs           策略路由
│   ├── ICodeIndexStore.cs         索引存储
│   ├── ICodeRetriever.cs          语义检索
│   └── ICodeIndexer.cs            索引协调
│
├── Rag/Models/
│   ├── CodeFile.cs                待索引的文件
│   ├── CodeChunk.cs               切分后的代码块
│   ├── CodeChunkType.cs           块类型枚举
│   ├── FileChangeSet.cs           文件变更集
│   ├── IndexFileRecord.cs         已索引文件记录
│   ├── IndexResult.cs             索引结果
│   ├── SearchResult.cs            检索结果
│   └── SearchSource.cs            检索来源枚举
│
└── Rag/Options/
    └── RagOptions.cs              配置
```

### Infrastructure/Rag/

```
AI.Assistant.Infrastructure/
└── Services/Rag/
    ├── Scanner/
    │   ├── ProjectScanner.cs           IProjectScanner 实现
    │   └── IndexComparer.cs            IIndexComparer 实现
    │
    ├── Chunking/
    │   ├── ChunkManager.cs             IChunkManager 实现
    │   └── Strategies/
    │       ├── FileChunkStrategy.cs     兜底整文件切分
    │       └── ... (后续: CodeChunkStrategy, MarkdownChunkStrategy)
    │
    ├── Storage/
    │   └── CodeIndexStore.cs           ICodeIndexStore 实现 (Qdrant + MSSQL)
    │
    ├── Retrieval/
    │   └── CodeRetriever.cs            ICodeRetriever 实现
    │
    └── Indexing/
        └── CodeIndexer.cs              ICodeIndexer 实现
```

---

## 接口定义

### IProjectScanner

```csharp
public interface IProjectScanner
{
    /// <summary>扫描项目目录，返回当前所有文件信息（含 FileHash）</summary>
    Task<IList<CodeFile>> ScanProjectAsync(string projectPath, CancellationToken ct);
}
```

### IIndexComparer

```csharp
public interface IIndexComparer
{
    /// <summary>对比当前文件列表与历史索引记录，生成变更集</summary>
    Task<FileChangeSet> CompareAsync(
        IList<CodeFile> currentFiles,
        IList<IndexFileRecord> previousIndex,
        CancellationToken ct);
}
```

### IChunkStrategy

```csharp
public interface IChunkStrategy
{
    /// <summary>该策略支持的文件扩展名列表</summary>
    string[] SupportedExtensions { get; }

    /// <summary>将单个文件切分为多个语义块</summary>
    IAsyncEnumerable<CodeChunk> ChunkAsync(CodeFile file, CancellationToken ct);
}
```

### IChunkManager

```csharp
public interface IChunkManager
{
    /// <summary>根据文件扩展名自动选择策略并执行切分</summary>
    IAsyncEnumerable<CodeChunk> ChunkAsync(CodeFile file, CancellationToken ct);
}
```

### ICodeIndexStore

```csharp
public interface ICodeIndexStore
{
    /// <summary>保存一批 CodeChunk（upsert 语义）</summary>
    Task SaveChunksAsync(IEnumerable<CodeChunk> chunks, CancellationToken ct);

    /// <summary>删除指定文件的所有 chunks（增量更新时调用）</summary>
    Task DeleteChunksByFileAsync(string filePath, CancellationToken ct);

    /// <summary>删除整个项目的索引数据（重建时调用）</summary>
    Task DeleteProjectAsync(string projectPath, CancellationToken ct);

    /// <summary>获取项目已索引的文件记录（用于增量对比）</summary>
    Task<IList<IndexFileRecord>> GetIndexedFilesAsync(string projectPath, CancellationToken ct);
}
```

### ICodeRetriever

```csharp
public interface ICodeRetriever
{
    /// <summary>向量语义搜索</summary>
    Task<IList<SearchResult>> VectorSearchAsync(string query, int topK = 5, CancellationToken ct = default);

    /// <summary>混合搜索（预留）</summary>
    Task<IList<SearchResult>> HybridSearchAsync(string query, int topK = 5, CancellationToken ct = default);
}
```

### ICodeIndexer

```csharp
public interface ICodeIndexer
{
    /// <summary>全量索引项目</summary>
    Task<IndexResult> IndexProjectAsync(string projectPath, CancellationToken ct = default);

    /// <summary>增量索引：只处理变更文件</summary>
    Task<IndexResult> IncrementalIndexAsync(string projectPath, CancellationToken ct = default);
}
```

---

## 模型定义

### CodeFile

```csharp
public class CodeFile
{
    public string FilePath { get; set; }
    public string Content { get; set; }
    public string Language { get; set; }         // "csharp", "markdown" 等
    public string Encoding { get; set; }          // "utf-8", "gb2312"
    public string FileHash { get; set; }          // Scanner 计算，Comparer 比较
    public DateTime LastModifiedTime { get; set; }
}
```

### CodeChunk

```csharp
public class CodeChunk
{
    public string Id { get; set; }
    public string VectorId { get; set; }          // Qdrant point ID
    public string FilePath { get; set; }
    public string Content { get; set; }
    public string Language { get; set; }
    public CodeChunkType ChunkType { get; set; }

    public int StartLine { get; set; }
    public int EndLine { get; set; }

    // Roslyn 元数据（第一版为空，预留字段）
    public string? Namespace { get; set; }
    public string? ClassName { get; set; }
    public string? MethodName { get; set; }
    public string? SymbolName { get; set; }

    public string ProjectPath { get; set; }
    public DateTime IndexedAt { get; set; }
}
```

### CodeChunkType

```csharp
public enum CodeChunkType
{
    File,
    Namespace,
    Class,
    Interface,
    Enum,
    Struct,
    Method,
    Property,
    Field,
    Event,
    Record
}
```

### IndexFileRecord

```csharp
public class IndexFileRecord
{
    public string FilePath { get; set; }
    public string FileHash { get; set; }
    public DateTime LastModifiedAt { get; set; }
    public DateTime IndexedAt { get; set; }
}
```

### FileChangeSet

```csharp
public class FileChangeSet
{
    public IList<CodeFile> Added { get; set; } = [];
    public IList<CodeFile> Modified { get; set; } = [];
    public IList<IndexFileRecord> Deleted { get; set; } = [];
    public bool HasChanges => Added.Count > 0 || Modified.Count > 0 || Deleted.Count > 0;
}
```

### IndexResult

```csharp
public class IndexResult
{
    public bool Success { get; set; }
    public int FilesScanned { get; set; }
    public int ChunksCreated { get; set; }
    public int FilesDeleted { get; set; }
    public IList<string> Errors { get; set; } = [];
    public TimeSpan Duration { get; set; }
}
```

### SearchResult

```csharp
public class SearchResult
{
    public CodeChunk Chunk { get; set; } = null!;
    public float Score { get; set; }
    public SearchSource Source { get; set; } = SearchSource.Vector;
}
```

### SearchSource

```csharp
public enum SearchSource { Vector, BM25, Hybrid }
```

---

## 配置

### RagOptions

```csharp
public class RagOptions
{
    public string QdrantCollectionName { get; set; } = "code_rag";
    public int MaxChunkSize { get; set; } = 512;
    public int ChunkOverlap { get; set; } = 50;
    public string[] SupportedExtensions { get; set; } =
        [".cs", ".xaml", ".json", ".md", ".xml"];

    public string[] IgnoreFolders { get; set; } =
        ["bin", "obj", ".git", ".vs", "node_modules", ".mimocode", ".cache"];

    public string[] IgnoreExtensions { get; set; } =
        [".exe", ".dll", ".pdb", ".cache", ".suo", ".user"];
}
```

### appsettings.json 补充

```json
{
  "Rag": {
    "QdrantCollectionName": "code_rag",
    "MaxChunkSize": 512,
    "ChunkOverlap": 50,
    "SupportedExtensions": [".cs", ".xaml", ".json", ".md", ".xml"],
    "IgnoreFolders": ["bin", "obj", ".git", ".vs", "node_modules"],
    "IgnoreExtensions": [".exe", ".dll", ".pdb"]
  }
}
```

---

## DI 注册

### 模块化注册

```csharp
// Infrastructure/Extensions/RagServiceCollectionExtensions.cs

public static class RagServiceCollectionExtensions
{
    // 统一入口
    public static IServiceCollection AddRag(
        this IServiceCollection services,
        Action<RagOptions>? configure = null)
    {
        var options = new RagOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);

        return services
            .AddRagScanner()
            .AddRagChunking()
            .AddRagStorage()
            .AddRagRetrieval()
            .AddRagIndexing();
    }

    public static IServiceCollection AddRagScanner(this IServiceCollection services)
    {
        services.AddSingleton<IProjectScanner, ProjectScanner>();
        services.AddSingleton<IIndexComparer, IndexComparer>();
        return services;
    }

    public static IServiceCollection AddRagChunking(this IServiceCollection services)
    {
        services.AddSingleton<IChunkManager, ChunkManager>();
        services.AddSingleton<IChunkStrategy, FileChunkStrategy>();
        // 后续增加策略只需再加一行：
        // services.AddSingleton<IChunkStrategy, CodeChunkStrategy>();
        return services;
    }

    public static IServiceCollection AddRagStorage(this IServiceCollection services)
    {
        services.AddSingleton<ICodeIndexStore, CodeIndexStore>();
        return services;
    }

    public static IServiceCollection AddRagRetrieval(this IServiceCollection services)
    {
        services.AddSingleton<ICodeRetriever, CodeRetriever>();
        return services;
    }

    public static IServiceCollection AddRagIndexing(this IServiceCollection services)
    {
        services.AddSingleton<ICodeIndexer, CodeIndexer>();
        return services;
    }
}
```

### 集成到 AddInfrastructure

```csharp
// 在 ServiceCollectionExtensions.AddInfrastructure 末尾添加：
services.AddRag(options =>
{
    options.QdrantCollectionName = "code_rag";
    // 可从 InfrastructureOptions 继承部分默认值
});
```

### 基础设施复用关系

```
IVectorStore ──┬── MemoryService (collection: "memories")
               └── CodeIndexStore (collection: "code_rag")
                         ↑
IEmbeddingService ───────┘  (所有模块共用 Embedding)

IChatService → 只用于聊天/事实提取，RAG 不依赖
```

---

## 索引流程

### 全量索引

```
IndexProjectAsync
  ├─ ProjectScanner.ScanProjectAsync
  ├─ IndexComparer.CompareAsync (空历史 → 全量 Added)
  ├─ 对每个 Added/Modified 文件：
  │   ├─ ChunkManager.ChunkAsync → CodeChunk[]
  │   └─ EmbeddingService.EmbedBatchAsync → float[][]
  └─ CodeIndexStore.SaveChunksAsync
```

### 增量索引

```
IncrementalIndexAsync
  ├─ ProjectScanner.ScanProjectAsync
  ├─ CodeIndexStore.GetIndexedFilesAsync
  ├─ IndexComparer.CompareAsync → FileChangeSet
  ├─ 对 Added/Modified 文件：Chunk → Embed → Save
  └─ 对 Deleted 文件：CodeIndexStore.DeleteChunksByFileAsync
```

### 检索流程

```
VectorSearchAsync(query)
  ├─ EmbeddingService.EmbedAsync(query) → queryVector
  ├─ IVectorStore.SearchAsync("code_rag", queryVector, topK, filter)
  └─ 组装 SearchResult（Chunk + Score + Source.Vector）
```

---

## 后续扩展点

| 能力 | 需要做什么 |
|------|-----------|
| Roslyn 代码块切分 | 新增 `CodeChunkStrategy : IChunkStrategy`，注册到 DI |
| BM25 检索 | `ICodeRetriever` 已有 `HybridSearchAsync`，新增 BM25 实现 |
| SQLite 代替 Qdrant | 新增 `SqLiteCodeIndexStore`，实现 `ICodeIndexStore` |
| OpenAI/Azure Embedding | `IEmbeddingService` 已有云端实现，直接复用 |
| 文件监听自动索引 | 新增 `FileWatcherIndexer`（或集成到 `ICodeIndexer`） |
| 索引远程项目 | `IProjectScanner` 可扩展 SSH/HTTP 扫描实现 |
