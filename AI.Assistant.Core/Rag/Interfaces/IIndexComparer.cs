using AI.Assistant.Core.Rag.Models;

namespace AI.Assistant.Core.Rag.Interfaces;

public interface IIndexComparer
{
    FileChangeSet Compare(IList<CodeFile> scannedFiles, IList<IndexFileRecord> indexedRecords);
}
