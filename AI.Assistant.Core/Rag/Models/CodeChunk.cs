using AI.Assistant.Core.Rag.Interfaces;

namespace AI.Assistant.Core.Rag.Models;

public class CodeChunk : IKnowledgeChunk
{
    public string Id { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string VectorId { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public CodeChunkType ChunkType { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public string? Namespace { get; set; }
    public string? ClassName { get; set; }
    public string? MethodName { get; set; }
    public string? SymbolName { get; set; }
    public string ProjectPath { get; set; } = string.Empty;
    public DateTime IndexedAt { get; set; }

    public SourceType SourceType => SourceType.Code;
    public string SourceUri => FilePath;

    private IReadOnlyDictionary<string, string>? _metadata;
    public IReadOnlyDictionary<string, string> Metadata =>
        _metadata ??= BuildMetadata();

    private Dictionary<string, string> BuildMetadata()
    {
        var d = new Dictionary<string, string>
        {
            ["language"] = Language,
            ["chunk_type"] = ChunkType.ToString(),
            ["start_line"] = StartLine.ToString(),
            ["end_line"] = EndLine.ToString()
        };
        if (Namespace is not null) d["namespace"] = Namespace;
        if (ClassName is not null) d["class_name"] = ClassName;
        if (MethodName is not null) d["method_name"] = MethodName;
        if (SymbolName is not null) d["symbol_name"] = SymbolName;
        return d;
    }
}
