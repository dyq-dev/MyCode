using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using AI.Assistant.Core.Interfaces;
using AI.Assistant.Core.Rag.Models;

namespace AI.Assistant.Infrastructure.Services.Rag.Workspace;

public class JsonWorkspaceStore : IWorkspaceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _storePath;

    public JsonWorkspaceStore(string? storePath = null)
    {
        _storePath = storePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AI.Assistant",
            "workspace.json");
    }

    public async Task<List<KnowledgeSource>> LoadAsync()
    {
        if (!File.Exists(_storePath))
            return [];

        try
        {
            var json = await File.ReadAllTextAsync(_storePath);
            var loaded = JsonSerializer.Deserialize<List<KnowledgeSource>>(json, JsonOptions);
            if (loaded is null)
                return [];

            foreach (var s in loaded)
            {
                s.IndexStatus = "未索引";
                s.LastIndexedAt = null;
            }

            return loaded;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[JsonWorkspaceStore] Load failed: {ex.Message}");
            return [];
        }
    }

    public async Task SaveAsync(IEnumerable<KnowledgeSource> sources)
    {
        var dir = Path.GetDirectoryName(_storePath);
        if (dir is not null)
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(sources.ToList(), JsonOptions);
        await File.WriteAllTextAsync(_storePath, json);
    }
}
