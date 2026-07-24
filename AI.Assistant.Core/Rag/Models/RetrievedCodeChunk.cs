namespace AI.Assistant.Core.Rag.Models;

/// <summary>检索到的代码块结果，包含原始分块和相似度分数</summary>
public class RetrievedCodeChunk
{
    /// <summary>代码分块（内容、位置、语言等元数据）</summary>
    public CodeChunk Chunk { get; init; } = null!;

    /// <summary>与查询向量的余弦相似度分数（越高越相关）</summary>
    public float Score { get; init; }
}
