namespace AI.Assistant.Core.Rag.Models;

/// <summary>嵌入后的代码块，包含原始分块信息和对应的向量</summary>
public class EmbeddedChunk
{
    /// <summary>原始代码分块（内容、位置、语言等元数据）</summary>
    public CodeChunk Chunk { get; init; } = null!;

    /// <summary>经过 Embedding 模型生成的向量表示</summary>
    public float[] Vector { get; init; } = null!;
}
