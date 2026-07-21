namespace AI.Assistant.Core.Rag.Models;

public class IndexResult
{
    public bool Success { get; set; }
    public int FilesScanned { get; set; }
    public int FilesAdded { get; set; }
    public int FilesModified { get; set; }
    public int FilesDeleted { get; set; }
    public int FilesFailed { get; set; }
    public int ChunksCreated { get; set; }
    public IList<string> Errors { get; set; } = [];
    public TimeSpan Duration { get; set; }
}
