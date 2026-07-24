# Code RAG 端到端验证计划

日期：2026-07-24
状态：待执行

---

## 环境要求

| 组件 | 要求 | 验证方式 |
|---|---|---|
| Ollama | 运行中，`qwen2.5:1.5b` + `bge-small-zh-v1.5` 已拉取 | `ollama list` |
| Qdrant | 运行中，gRPC 端口 6334 | 浏览器访问 `http://localhost:6333/dashboard` |
| MSSQL | 运行中，`AIAssistant` 数据库存在 | SSMS / `sqlcmd` |
| .NET 8 SDK | `net8.0-windows` 可用 | `dotnet --list-sdks` |

## 前置步骤

### 1. 索引 MyCode 项目

在 `App.xaml.cs` 的 `ConfigureServices` 中添加 RAG 注册（`AddRag` + `AddRagChatIntegration`），
确保 `EnableDebugInfo = true` 和 `MinimumScoreThreshold` 按需设置。

调用索引代码（参考 `CodeIndexerTests` 构造，使用真实服务）：

```
var scanner = host.Services.GetRequiredService<IProjectScanner>();
var chunkManager = host.Services.GetRequiredService<IChunkManager>();
var indexStore = host.Services.GetRequiredService<ICodeIndexStore>();
var embedding = host.Services.GetRequiredService<IEmbeddingService>();
var indexer = new CodeIndexer(scanner, chunkManager, indexStore, embedding);

// 全量索引 MyCode 自身代码
var result = await indexer.IndexProjectAsync(@"D:\MyCode\demo\MyCodeDemo");
Console.WriteLine($"Indexed: {result.IndexedCount}, Failed: {result.FailedCount}, Duration: {result.Duration}");
```

### 2. 查询测试

```
var retriever = host.Services.GetRequiredService<ICodeRetriever>();
var contextBuilder = host.Services.GetRequiredService<IRagContextBuilder>();
var ragQuery = host.Services.GetRequiredService<IRagQueryService>();

var qResult = await ragQuery.QueryAsync("测试问题");
// 检查 qResult.DebugInfo
```

---

## 20 个代码问题

### 架构理解（4 题）

1. **Q:** "这个项目的整体架构是怎样的？各层之间如何依赖？"
   - **预期:** 应检索到包含项目分层描述的文档或代码中 Architecture 相关注释
   - **Debug 预期:** Triggered=true, ≥1 chunk

2. **Q:** "项目中使用了哪些设计模式？举三个例子"
   - **预期:** 应定位到 MVVM（ViewModel）、装饰器（RagChatService）、策略（IChunkStrategy）等模式
   - **Debug 预期:** Triggered=true, ≥3 chunks

3. **Q:** "Chat Memory 和 Code RAG 是什么关系？它们共享哪些基础设施？"
   - **预期:** 应定位到 `IEmbeddingService`、`IVectorStore` 等共享接口
   - **Debug 预期:** Triggered=true, ≥2 chunks

4. **Q:** "从用户输入消息到 AI 回复，RAG 链路经过哪些组件？"
   - **预期:** RagChatService → IRagQueryService → ICodeRetriever → ICodeQueryStore → IRagContextBuilder
   - **Debug 预期:** Triggered=true, 应检索到 `RagChatService`、`RagQueryService`、`CodeRetriever`

### 文件定位（4 题）

5. **Q:** "IChatService 接口定义在哪个文件？"
   - **预期:** `AI.Assistant.Core/Interfaces/IChatService.cs`
   - **Debug 预期:** FilePath 包含 `Interfaces\IChatService.cs`

6. **Q:** "找到 RagContextBuilder 的实现文件和所在命名空间"
   - **预期:** `Infrastructure/Services/Rag/Context/RagContextBuilder.cs`，`AI.Assistant.Infrastructure.Services.Rag.Context`
   - **Debug 预期:** FilePath 包含 `RagContextBuilder.cs`

7. **Q:** "ProjectScanner 类在哪个文件中？它实现了哪个接口？"
   - **预期:** `Infrastructure/Services/Rag/Scanner/ProjectScanner.cs`，实现 `IProjectScanner`
   - **Debug 预期:** FilePath 包含 `ProjectScanner.cs`

8. **Q:** "appsettings.json 文件在哪里？里面配置了哪些 LLM 相关设置？"
   - **预期:** `Client/appsettings.json`，包含 ChatProvider、EmbeddingProvider、Ollama/Cloud 等配置
   - **Debug 预期:** FilePath 包含 `appsettings.json`

### 方法定位（4 题）

9. **Q:** "IndexProjectAsync 方法在哪里定义？它的参数和返回值是什么？"
   - **预期:** `ICodeIndexer.cs` 接口，参数 `string projectPath, CancellationToken`，返回 `Task<IndexResult>`
   - **Debug 预期:** 应检索到 `ICodeIndexer.cs` 或 `CodeIndexer.cs`

10. **Q:** "VectorSearchAsync 方法在 CodeRetriever 中如何实现？它依赖哪些服务？"
    - **预期:** 先 Embedding，再 ICodeQueryStore.SearchAsync，最后过滤排序
    - **Debug 预期:** FilePath 包含 `CodeRetriever.cs`

11. **Q:** "RagContextBuilder 的 BuildAsync 方法有哪些配置选项？"
    - **预期:** MaxChunks、MaxChunksPerFile、MaxContextTokens、GroupByFile、ShowLineNumbers、Prefix
    - **Debug 预期:** FilePath 包含 `RagContextOptions.cs` 或 `RagContextBuilder.cs`

12. **Q:** "MemoryService 的 EnsureReadyAsync 方法在哪里？它初始化哪些资源？"
    - **预期:** `Infrastructure/Services/MemoryService.cs`，初始化 Qdrant Collection、MSSQL 表
    - **Debug 预期:** FilePath 包含 `MemoryService.cs`

### 模块关系（4 题）

13. **Q:** "CodeIndexer 依赖了哪些接口？它是如何协调索引流程的？"
    - **预期:** IProjectScanner → IChunkManager → ICodeIndexStore → IEmbeddingService
    - **Debug 预期:** ≥3 chunks 覆盖这些接口

14. **Q:** "RagChatService 是如何与 IRagQueryService 配合的？Context 角色如何映射？"
    - **预期:** RagChatService 在 StreamAsync/SendAsync 中调用 IRagQueryService，Context 映射为 System
    - **Debug 预期:** FilePath 包含 `RagChatService.cs`

15. **Q:** "MemoryService 使用了哪些服务？画出它的依赖链"
    - **预期:** IEmbeddingService + IVectorStore + MemoryRepository + IChatService + IMemoryFilter
    - **Debug 预期:** ≥3 chunks

16. **Q:** "CodeRetriever 的完整查询链路是怎样的？从 Embedding 到向量搜索"
    - **预期:** query → IEmbeddingService.EmbedAsync → float[] → ICodeQueryStore.SearchAsync → RetrievedCodeChunk[]
    - **Debug 预期:** FilePath 包含 `CodeRetriever.cs`

### 重构建议（4 题）

17. **Q:** "RagOptions 中 DefaultTopK 和 MaxTopK 可能产生冲突，如何优化配置设计？"
    - **预期:** 需要定位 `RagOptions.cs`，分析 DefaultTopK ≤ MaxTopK 的校验逻辑
    - **Debug 预期:** FilePath 包含 `RagOptions.cs`

18. **Q:** "IChatService 的 SendAsync 和 StreamAsync 实现代码重复度如何？是否有优化空间？"
    - **预期:** 两个方法共享 RAG 查询逻辑，可抽取公共的 `QueryAndBuildHistoryAsync` 方法
    - **Debug 预期:** FilePath 包含 `RagChatService.cs`

19. **Q:** "CodeIndexer 的 IndexProjectAsync 和 IncrementalIndexAsync 有哪些公共逻辑？"
    - **预期:** 扫描 → 切分 → 向量化 → 存储的流程相同，差异在变更检测和清理策略
    - **Debug 预期:** FilePath 包含 `CodeIndexer.cs`

20. **Q:** "如果未来需要有多个不同配置的 RagQueryService 实例（不同阈值/不同关键词集），当前设计如何支持？"
    - **预期:** 当前用 `AddSingleton<IRagQueryService, RagQueryService>()` 不支持多实例。可改用 Keyed DI 或工厂模式
    - **Debug 预期:** FilePath 包含 `RagQueryService.cs`

---

## Debug 输出解读

当 `EnableDebugInfo = true` 时，每次查询返回 `RagQueryResult.DebugInfo`，格式示例：

```
DebugInfo:
  Query:         "IChatService 接口定义在哪个文件？"
  Triggered:     true
  Keyword:       "接口"
  Threshold:     0.50
  RawFound:      4             ← Retriever 返回的原始数量
  AfterFilter:   3             ← 阈值过滤后数量
  BuilderUsed:   2             ← ContextBuilder 最终使用
  Tokens:        340           ← 估算 Token
  Retrieval:     45ms          ← Embedding + Qdrant 搜索耗时
  Build:         2ms           ← 上下文拼接耗时
  Chunks:
    #1 Score 0.92  Interfaces\IChatService.cs:5-28           (File, csharp)
    #2 Score 0.85  Models\ChatMessage.cs:1-19                 (File, csharp)
    #3 Score 0.67  Extensions\ServiceCollectionExtensions.cs:17-30 (File, csharp)
```

### 通过标准

| 指标 | 期望值 |
|---|---|
| 触发率 | 20 题全部 Triggered=true |
| 平均 Chunks | ≥2 per query |
| 最低 Score | ≥0.3（Threshold=0.0 时），或 ≥Threshold 设置值 |
| Top-1 准确率 | 80% 以上问题的第一个 Chunk 直接命中目标文件 |
| 检索耗时 | Embedding + Search < 500ms |
| 无上下文误触发 | 非代码问题（如"你好"）HasContext=false |
