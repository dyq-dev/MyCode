using CommunityToolkit.Mvvm.ComponentModel;

namespace AI.Assistant.Client.Models;

/// <summary>Chunk 调试信息的 UI 展示模型</summary>
public partial class ChunkDisplayModel : ObservableObject
{
    [ObservableProperty]
    private int _index;

    [ObservableProperty]
    private string _filePath = "";

    [ObservableProperty]
    private int _startLine;

    [ObservableProperty]
    private int _endLine;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScoreDisplay))]
    private float _score;

    [ObservableProperty]
    private string _chunkType = "";

    [ObservableProperty]
    private string _language = "";

    public string FileLabel => $"{FilePath}:{StartLine}-{EndLine}";
    public string ScoreDisplay => Score.ToString("F2");
}
