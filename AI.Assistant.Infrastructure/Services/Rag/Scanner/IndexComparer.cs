using AI.Assistant.Core.Rag.Interfaces;
using AI.Assistant.Core.Rag.Models;

namespace AI.Assistant.Infrastructure.Services.Rag.Scanner;

public class IndexComparer : IIndexComparer
{
    public FileChangeSet Compare(IList<CodeFile> scannedFiles, IList<IndexFileRecord> indexedRecords)
    {
        var scannedByPath = new Dictionary<string, CodeFile>(scannedFiles.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var file in scannedFiles)
        {
            scannedByPath[file.FilePath] = file;
        }

        var indexedByPath = new Dictionary<string, IndexFileRecord>(indexedRecords.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var record in indexedRecords)
        {
            indexedByPath[record.FilePath] = record;
        }

        var added = new List<CodeFile>();
        var modified = new List<CodeFile>();

        foreach (var (path, file) in scannedByPath)
        {
            if (indexedByPath.Remove(path, out var existing))
            {
                if (!string.Equals(existing.FileHash, file.FileHash, StringComparison.OrdinalIgnoreCase))
                {
                    modified.Add(file);
                }
            }
            else
            {
                added.Add(file);
            }
        }

        var deleted = indexedByPath.Values.ToList();

        return new FileChangeSet
        {
            Added = added,
            Modified = modified,
            Deleted = deleted
        };
    }
}
