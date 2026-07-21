namespace AI.Assistant.Core.Rag.Models;

public class FileChangeSet
{
    public IList<CodeFile> Added { get; set; } = [];
    public IList<CodeFile> Modified { get; set; } = [];
    public IList<IndexFileRecord> Deleted { get; set; } = [];

    public bool HasChanges => Added.Count > 0 || Modified.Count > 0 || Deleted.Count > 0;
}
