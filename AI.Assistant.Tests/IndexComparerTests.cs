using AI.Assistant.Core.Rag.Interfaces;
using AI.Assistant.Core.Rag.Models;
using AI.Assistant.Infrastructure.Services.Rag.Scanner;

namespace AI.Assistant.Tests;

public class IndexComparerTests
{
    private readonly IIndexComparer _comparer = new IndexComparer();

    private static CodeFile File(string path, string? hash = null)
    {
        return new CodeFile
        {
            FilePath = path,
            FileHash = hash ?? path,
            Content = "irrelevant",
            Language = "csharp",
            Encoding = "utf-8"
        };
    }

    private static IndexFileRecord Record(string path, string? hash = null)
    {
        return new IndexFileRecord
        {
            FilePath = path,
            FileHash = hash ?? path,
            IndexedAt = DateTime.UtcNow
        };
    }

    // ============ Empty / No changes ============

    [Fact]
    public void Compare_EmptyLists_ReturnsNoChanges()
    {
        var result = _comparer.Compare([], []);

        Assert.Empty(result.Added);
        Assert.Empty(result.Modified);
        Assert.Empty(result.Deleted);
        Assert.False(result.HasChanges);
    }

    [Fact]
    public void Compare_AllFilesUnchanged_ReturnsNoChanges()
    {
        var scanned = new List<CodeFile> { File("a.cs", "hash1"), File("b.cs", "hash2") };
        var indexed = new List<IndexFileRecord> { Record("a.cs", "hash1"), Record("b.cs", "hash2") };

        var result = _comparer.Compare(scanned, indexed);

        Assert.Empty(result.Added);
        Assert.Empty(result.Modified);
        Assert.Empty(result.Deleted);
        Assert.False(result.HasChanges);
    }

    // ============ Added ============

    [Fact]
    public void Compare_AllNewFiles_ReturnsAllAdded()
    {
        var scanned = new List<CodeFile> { File("a.cs"), File("b.cs") };

        var result = _comparer.Compare(scanned, []);

        Assert.Equal(2, result.Added.Count);
        Assert.Contains(result.Added, f => f.FilePath == "a.cs");
        Assert.Contains(result.Added, f => f.FilePath == "b.cs");
        Assert.Empty(result.Modified);
        Assert.Empty(result.Deleted);
        Assert.True(result.HasChanges);
    }

    [Fact]
    public void Compare_MixedAddedAndUnchanged_OnlyNewInAdded()
    {
        var scanned = new List<CodeFile> { File("existing.cs", "h1"), File("new.cs", "h2") };
        var indexed = new List<IndexFileRecord> { Record("existing.cs", "h1") };

        var result = _comparer.Compare(scanned, indexed);

        Assert.Single(result.Added);
        Assert.Equal("new.cs", result.Added[0].FilePath);
        Assert.Empty(result.Modified);
        Assert.Empty(result.Deleted);
    }

    // ============ Modified ============

    [Fact]
    public void Compare_ModifiedFile_DetectedInModified()
    {
        var scanned = new List<CodeFile> { File("a.cs", "newhash") };
        var indexed = new List<IndexFileRecord> { Record("a.cs", "oldhash") };

        var result = _comparer.Compare(scanned, indexed);

        Assert.Single(result.Modified);
        Assert.Equal("a.cs", result.Modified[0].FilePath);
        Assert.Equal("newhash", result.Modified[0].FileHash);
        Assert.Empty(result.Added);
        Assert.Empty(result.Deleted);
    }

    [Fact]
    public void Compare_MultipleModified_AllDetected()
    {
        var scanned = new List<CodeFile>
        {
            File("a.cs", "new_a"),
            File("b.cs", "same_b"),
            File("c.cs", "new_c")
        };
        var indexed = new List<IndexFileRecord>
        {
            Record("a.cs", "old_a"),
            Record("b.cs", "same_b"),
            Record("c.cs", "old_c")
        };

        var result = _comparer.Compare(scanned, indexed);

        Assert.Equal(2, result.Modified.Count);
        Assert.Contains(result.Modified, f => f.FilePath == "a.cs");
        Assert.Contains(result.Modified, f => f.FilePath == "c.cs");
        Assert.Empty(result.Added);
        Assert.Empty(result.Deleted);
    }

    // ============ Deleted ============

    [Fact]
    public void Compare_AllRemovedFiles_ReturnsAllDeleted()
    {
        var indexed = new List<IndexFileRecord> { Record("a.cs"), Record("b.cs") };

        var result = _comparer.Compare([], indexed);

        Assert.Equal(2, result.Deleted.Count);
        Assert.Contains(result.Deleted, r => r.FilePath == "a.cs");
        Assert.Contains(result.Deleted, r => r.FilePath == "b.cs");
        Assert.Empty(result.Added);
        Assert.Empty(result.Modified);
        Assert.True(result.HasChanges);
    }

    [Fact]
    public void Compare_PartialDelete_OnlyMissingInDeleted()
    {
        var scanned = new List<CodeFile> { File("keep.cs", "h1") };
        var indexed = new List<IndexFileRecord> { Record("keep.cs", "h1"), Record("gone.cs", "h2") };

        var result = _comparer.Compare(scanned, indexed);

        Assert.Single(result.Deleted);
        Assert.Equal("gone.cs", result.Deleted[0].FilePath);
        Assert.Empty(result.Added);
        Assert.Empty(result.Modified);
    }

    // ============ Mixed ============

    [Fact]
    public void Compare_MixedAllFourStates_Correct()
    {
        var scanned = new List<CodeFile>
        {
            File("added.cs", "h_add"),
            File("modified.cs", "h_new"),
            File("unchanged.cs", "h_same")
        };
        var indexed = new List<IndexFileRecord>
        {
            Record("modified.cs", "h_old"),
            Record("unchanged.cs", "h_same"),
            Record("deleted.cs", "h_del")
        };

        var result = _comparer.Compare(scanned, indexed);

        Assert.Single(result.Added);
        Assert.Equal("added.cs", result.Added[0].FilePath);
        Assert.Single(result.Modified);
        Assert.Equal("modified.cs", result.Modified[0].FilePath);
        Assert.Single(result.Deleted);
        Assert.Equal("deleted.cs", result.Deleted[0].FilePath);
        Assert.True(result.HasChanges);
    }

    // ============ Order ============

    [Fact]
    public void Compare_DifferentOrder_ProducesSameResult()
    {
        var scanned = new List<CodeFile>
        {
            File("z.cs", "hz"),
            File("a.cs", "ha"),
            File("m.cs", "hm")
        };
        var indexed = new List<IndexFileRecord>
        {
            Record("m.cs", "hm"),
            Record("z.cs", "hz"),
            Record("a.cs", "ha")
        };

        var result = _comparer.Compare(scanned, indexed);

        Assert.Empty(result.Added);
        Assert.Empty(result.Modified);
        Assert.Empty(result.Deleted);
        Assert.False(result.HasChanges);
    }

    [Fact]
    public void Compare_ReversedOrder_StillDetectsChanges()
    {
        var scanned = new List<CodeFile>
        {
            File("c.cs", "new_c"),
            File("a.cs", "h_a"),
            File("b.cs", "new_b")
        };
        var indexed = new List<IndexFileRecord>
        {
            Record("a.cs", "h_a"),
            Record("b.cs", "old_b"),
            Record("c.cs", "old_c")
        };

        var result = _comparer.Compare(scanned, indexed);

        Assert.Equal(2, result.Modified.Count);
        Assert.Contains(result.Modified, f => f.FilePath == "b.cs");
        Assert.Contains(result.Modified, f => f.FilePath == "c.cs");
        Assert.Empty(result.Added);
        Assert.Empty(result.Deleted);
    }

    // ============ Case sensitivity ============

    [Fact]
    public void Compare_PathComparisonIsCaseInsensitive()
    {
        var scanned = new List<CodeFile> { File("SRC/File.cs", "h1") };
        var indexed = new List<IndexFileRecord> { Record("src/file.cs", "h1") };

        var result = _comparer.Compare(scanned, indexed);

        Assert.Empty(result.Added);
        Assert.Empty(result.Modified);
        Assert.Empty(result.Deleted);
        Assert.False(result.HasChanges);
    }

    [Fact]
    public void Compare_HashComparisonIsCaseInsensitive()
    {
        var scanned = new List<CodeFile> { File("a.cs", "ABCDEF") };
        var indexed = new List<IndexFileRecord> { Record("a.cs", "abcdef") };

        var result = _comparer.Compare(scanned, indexed);

        Assert.Empty(result.Modified);
        Assert.False(result.HasChanges);
    }

    // ============ Large volume test ============

    [Fact]
    public void Compare_HundredThousandFiles_CompletesQuickly()
    {
        const int count = 100_000;
        var scanned = new List<CodeFile>(count);
        var indexed = new List<IndexFileRecord>(count);

        for (int i = 0; i < count; i++)
        {
            scanned.Add(File($"file{i}.cs", $"hash{i}"));
            indexed.Add(Record($"file{i}.cs", $"hash{i}"));
        }

        // 修改中间一个
        scanned[count / 2].FileHash = "modified_hash";

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = _comparer.Compare(scanned, indexed);
        sw.Stop();

        Assert.Single(result.Modified);
        Assert.Equal($"file{count / 2}.cs", result.Modified[0].FilePath);
        Assert.True(sw.ElapsedMilliseconds < 5000, $"Took {sw.ElapsedMilliseconds}ms, expected < 5000ms");
    }

    // ============ Duplicate paths ============

    [Fact]
    public void Compare_DuplicateInScanned_LastWins()
    {
        var scanned = new List<CodeFile>
        {
            File("a.cs", "first_hash"),
            File("a.cs", "second_hash")
        };

        var result = _comparer.Compare(scanned, []);

        Assert.Single(result.Added);
        Assert.Equal("second_hash", result.Added[0].FileHash);
    }

    [Fact]
    public void Compare_DuplicateInIndexed_LastWins()
    {
        var scanned = new List<CodeFile> { File("a.cs", "current") };
        var indexed = new List<IndexFileRecord>
        {
            Record("a.cs", "old"),
            Record("a.cs", "same")
        };

        var result = _comparer.Compare(scanned, indexed);

        // last indexed record hash is "same" which differs from "current" → Modified
        Assert.Single(result.Modified);
        Assert.Equal("current", result.Modified[0].FileHash);
        Assert.Empty(result.Added);
        Assert.Empty(result.Deleted);
        Assert.True(result.HasChanges);
    }
}
