namespace AI.Assistant.Core.Rag.Options;

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

    /// <summary>向量检索默认返回条数</summary>
    public int DefaultTopK { get; set; } = 5;

    /// <summary>向量检索单次上限，防止客户端传过大值</summary>
    public int MaxTopK { get; set; } = 20;

    /// <summary>是否填充 RagQueryResult.DebugInfo（用于 UI 调试 / 测试验证）</summary>
    public bool EnableDebugInfo { get; set; } = false;

    /// <summary>是否输出 ILogger Debug 级别 RAG 日志（用于开发日志）</summary>
    public bool EnableDebugLog { get; set; } = false;

    /// <summary>Score 最低阈值（0 = 不过滤）。RagQueryService 在 Retriever 返回后过滤</summary>
    public double MinimumScoreThreshold { get; set; } = 0.0;

    /// <summary>RAG 触发关键词列表。用户消息包含任一关键词则触发向量检索</summary>
    public string[] RagKeywords { get; set; } =
    [
        "接口", "类", "方法", "实现", "定义", "函数",
        "代码", "文件", "在哪里", "哪个", "如何",
        "interface", "class", "method", "function", "code", "file",
        "项目", "架构", "模块", "组件", "依赖", "流程",
        "链路", "关系", "设计模式", "原理", "调用", "服务"
    ];
}
