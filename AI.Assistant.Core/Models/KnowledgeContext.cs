namespace AI.Assistant.Core.Models;

/// <summary>
/// 知识库检索结果（预留，暂未实现 RAG）
/// </summary>
public class KnowledgeContext
{
    public IList<string> RelevantChunks { get; set; } = [];
    public bool HasContent => RelevantChunks.Count > 0;
}
