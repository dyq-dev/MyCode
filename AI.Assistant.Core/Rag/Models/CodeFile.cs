namespace AI.Assistant.Core.Rag.Models;

public class CodeFile
{
    public string FilePath { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Encoding { get; set; } = "utf-8";
    public string FileHash { get; set; } = string.Empty;
    public DateTime LastModifiedTime { get; set; }
}
