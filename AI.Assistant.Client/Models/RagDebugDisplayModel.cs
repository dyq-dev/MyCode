using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AI.Assistant.Client.Models;

/// <summary>RAG 调试信息的 UI 展示模型（不直接绑定 Core 层的 RagQueryResult）</summary>
public partial class RagDebugDisplayModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TriggeredDisplay))]
    private bool _triggered;

    [ObservableProperty]
    private string _keyword = "";

    [ObservableProperty]
    private string _retrievalElapsed = "";

    [ObservableProperty]
    private string _contextBuildElapsed = "";

    [ObservableProperty]
    private int _rawChunks;

    [ObservableProperty]
    private int _chunksAfterFilter;

    [ObservableProperty]
    private int _chunksUsed;

    [ObservableProperty]
    private int _estimatedTokens;

    [ObservableProperty]
    private string _contextText = "";

    public ObservableCollection<ChunkDisplayModel> Chunks { get; } = [];

    public string TriggeredDisplay => Triggered ? "是" : "否";

    public void Clear()
    {
        Triggered = false;
        Keyword = "";
        RetrievalElapsed = "";
        ContextBuildElapsed = "";
        RawChunks = 0;
        ChunksAfterFilter = 0;
        ChunksUsed = 0;
        EstimatedTokens = 0;
        ContextText = "";
        Chunks.Clear();
    }
}
