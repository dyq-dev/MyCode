using System.Text.Json;
using AI.Assistant.Core.Interfaces;
using AI.Assistant.Core.Models;

namespace AI.Assistant.Infrastructure.Services.Rag.Persistence;

public class ConversationStore : IConversationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _baseDir;
    private readonly string _indexPath;

    public ConversationStore()
    {
        _baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AI.Assistant",
            "conversations");
        _indexPath = Path.Combine(_baseDir, "index.json");
        Directory.CreateDirectory(_baseDir);
    }

    public Task<List<Conversation>> LoadIndexAsync()
    {
        if (!File.Exists(_indexPath))
            return Task.FromResult(new List<Conversation>());

        try
        {
            var json = File.ReadAllText(_indexPath);
            var result = JsonSerializer.Deserialize<List<Conversation>>(json, JsonOptions) ?? [];
            return Task.FromResult(result);
        }
        catch
        {
            return Task.FromResult(new List<Conversation>());
        }
    }

    public Task<Conversation?> LoadConversationAsync(string id)
    {
        var path = GetConversationPath(id);
        if (!File.Exists(path))
            return Task.FromResult<Conversation?>(null);

        try
        {
            var json = File.ReadAllText(path);
            var result = JsonSerializer.Deserialize<Conversation>(json, JsonOptions);
            return Task.FromResult(result);
        }
        catch
        {
            return Task.FromResult<Conversation?>(null);
        }
    }

    public Task SaveConversationAsync(Conversation conversation)
    {
        var path = GetConversationPath(conversation.Id.ToString());
        var json = JsonSerializer.Serialize(conversation, JsonOptions);
        File.WriteAllText(path, json);
        SaveIndex();
        return Task.CompletedTask;
    }

    public Task DeleteConversationAsync(string id)
    {
        var path = GetConversationPath(id);
        if (File.Exists(path))
            File.Delete(path);
        SaveIndex();
        return Task.CompletedTask;
    }

    private string GetConversationPath(string id) =>
        Path.Combine(_baseDir, $"{id}.json");

    private void SaveIndex()
    {
        var dir = Path.GetDirectoryName(_indexPath);
        if (dir is not null)
            Directory.CreateDirectory(dir);

        var entries = Directory.GetFiles(_baseDir, "*.json")
            .Where(f => !f.EndsWith("index.json", StringComparison.OrdinalIgnoreCase))
            .Select(f =>
            {
                try
                {
                    var json = File.ReadAllText(f);
                    var conv = JsonSerializer.Deserialize<Conversation>(json, JsonOptions);
                    if (conv is null) return null;
                    return new Conversation
                    {
                        Id = conv.Id,
                        Title = conv.Title,
                        CreatedAt = conv.CreatedAt,
                        UpdatedAt = conv.UpdatedAt
                    };
                }
                catch
                {
                    return null;
                }
            })
            .Where(c => c is not null)
            .Cast<Conversation>()
            .OrderByDescending(c => c.UpdatedAt)
            .ToList();

        var json = JsonSerializer.Serialize(entries, JsonOptions);
        File.WriteAllText(_indexPath, json);
    }
}
