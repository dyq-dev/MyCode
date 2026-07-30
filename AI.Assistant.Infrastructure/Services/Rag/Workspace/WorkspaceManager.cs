using System.Text.Json;
using System.Text.Json.Serialization;
using AI.Assistant.Core.Interfaces;
using AI.Assistant.Core.Rag.Interfaces;
using AI.Assistant.Core.Rag.Models;

namespace AI.Assistant.Infrastructure.Services.Rag.Workspace;

public class WorkspaceManager : IWorkspaceManager, IDisposable
{
    private readonly List<KnowledgeSource> _sources = [];
    private readonly object _lock = new();
    private readonly string _storePath;
    private readonly Timer? _saveTimer;
    private readonly IWorkspaceStore? _store;
    private bool _dirty;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public WorkspaceManager() : this(null)
    {
    }

    public WorkspaceManager(IWorkspaceStore? store)
    {
        _store = store;
        _storePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AI.Assistant",
            "workspace.json");

        Directory.CreateDirectory(Path.GetDirectoryName(_storePath)!);

        if (_store is null)
            TryLoadFromFile();

        _saveTimer = new Timer(_ => Flush(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    }

    public IReadOnlyList<KnowledgeSource> GetSources(string? workspaceId = null)
    {
        lock (_lock)
        {
            if (workspaceId is null)
                return _sources.ToList();

            return _sources.Where(s => s.WorkspaceId == workspaceId).ToList();
        }
    }

    public KnowledgeSource? GetSource(string id)
    {
        lock (_lock)
        {
            return _sources.FirstOrDefault(s => s.Id == id);
        }
    }

    public KnowledgeSource? GetSourceByUri(string uri)
    {
        lock (_lock)
        {
            return _sources.FirstOrDefault(s =>
                s.Uri.Equals(uri, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void AddSource(KnowledgeSource source)
    {
        lock (_lock)
        {
            _sources.Add(source);
            MarkDirty();
        }
    }

    public void RemoveSource(string id)
    {
        lock (_lock)
        {
            _sources.RemoveAll(s => s.Id == id);
            MarkDirty();
        }
    }

    public void UpdateSource(KnowledgeSource source)
    {
        lock (_lock)
        {
            var index = _sources.FindIndex(s => s.Id == source.Id);
            if (index >= 0)
                _sources[index] = source;
            MarkDirty();
        }
    }

    public bool HasSourceByType(SourceType type)
    {
        lock (_lock)
        {
            return _sources.Any(s => s.SourceType == type);
        }
    }

    public async Task LoadAsync()
    {
        if (_store is null)
            return;

        var loaded = await _store.LoadAsync();
        lock (_lock)
        {
            _sources.Clear();
            _sources.AddRange(loaded);
        }
    }

    private void MarkDirty() => _dirty = true;

    private void TryLoadFromFile()
    {
        if (!File.Exists(_storePath))
            return;

        try
        {
            var json = File.ReadAllText(_storePath);
            var loaded = JsonSerializer.Deserialize<List<KnowledgeSource>>(json, JsonOptions);
            if (loaded is not null)
                _sources.AddRange(loaded);
        }
        catch
        {
        }
    }

    private void Flush()
    {
        if (!_dirty) return;

        List<KnowledgeSource> snapshot;
        lock (_lock)
        {
            if (!_dirty) return;
            snapshot = [.. _sources];
            _dirty = false;
        }

        try
        {
            var json = JsonSerializer.Serialize(snapshot, JsonOptions);
            File.WriteAllText(_storePath, json);
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        _saveTimer?.Dispose();
        Flush();
    }
}
