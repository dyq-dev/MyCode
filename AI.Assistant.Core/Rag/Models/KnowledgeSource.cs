using System.ComponentModel;
using System.Text.Json.Serialization;

namespace AI.Assistant.Core.Rag.Models;

public class KnowledgeSource : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string WorkspaceId { get; init; } = "default";
    public string Name { get; init; } = "";
    public SourceType SourceType { get; init; }
    public string Uri { get; init; } = "";
    public bool IsEnabled { get; set; } = true;
    public bool AutoSync { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    [JsonIgnore]
    public DateTime? LastIndexedAt { get; set; }

    private string? _indexStatus;
    [JsonIgnore]
    public string? IndexStatus
    {
        get => _indexStatus;
        set
        {
            if (_indexStatus != value)
            {
                _indexStatus = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IndexStatus)));
            }
        }
    }

    public string DisplayIcon => SourceType switch
    {
        SourceType.Code => "\U0001F4C4",
        SourceType.Markdown => "\U0001F4DD",
        SourceType.Document => "\U0001F4C4",
        SourceType.Text => "\U0001F4C4",
        SourceType.Pdf => "\U0001F4C4",
        _ => "\U00002753"
    };
}
