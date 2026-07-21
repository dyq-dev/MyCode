namespace AI.Assistant.Core.Rag.Models;

public class IndexFileRecord
{
    public string FilePath { get; set; } = string.Empty;
    public string FileHash { get; set; } = string.Empty;
    public DateTime LastModifiedAt { get; set; }
    public DateTime IndexedAt { get; set; }
}
