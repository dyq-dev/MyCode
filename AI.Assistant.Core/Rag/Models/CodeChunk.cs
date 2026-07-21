namespace AI.Assistant.Core.Rag.Models;

public class CodeChunk
{
    public string Id { get; set; } = string.Empty;
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
}
