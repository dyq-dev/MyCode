namespace AI.Assistant.Core.Rag.Context;

/// <summary>单个 Chunk 的调试信息</summary>
public class RagChunkDebugInfo
{
    /// <summary>文件路径（相对项目根）</summary>
    public string FilePath { get; init; } = "";

    /// <summary>起始行号</summary>
    public int StartLine { get; init; }

    /// <summary>结束行号</summary>
    public int EndLine { get; init; }

    /// <summary>向量检索相似度分数</summary>
    public float Score { get; init; }

    /// <summary>编程语言</summary>
    public string? Language { get; init; }

    /// <summary>Chunk 类型（File / Method / Class / Interface 等）</summary>
    public string ChunkType { get; init; } = "";
}
