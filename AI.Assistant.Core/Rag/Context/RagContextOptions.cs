namespace AI.Assistant.Core.Rag.Context;

/// <summary>RAG 上下文构建器配置</summary>
public class RagContextOptions
{
    /// <summary>最大代码片段数（按 Score 优先取 Top N）</summary>
    public int MaxChunks { get; set; } = 5;

    /// <summary>每个文件最多保留的代码片段数，防止单个文件占满上下文</summary>
    public int MaxChunksPerFile { get; set; } = 3;

    /// <summary>上下文最大估算 token 数</summary>
    public int MaxContextTokens { get; set; } = 2000;

    /// <summary>是否按文件分组（同文件的块连续输出）</summary>
    public bool GroupByFile { get; set; } = true;

    /// <summary>是否在代码块前标注行号范围</summary>
    public bool ShowLineNumbers { get; set; } = true;

    /// <summary>引导说明文字，空则不添加</summary>
    public string Prefix { get; set; } =
        "以下是代码仓库中与问题相关的上下文：";
}
