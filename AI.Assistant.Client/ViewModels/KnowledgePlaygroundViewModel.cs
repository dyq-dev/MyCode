using System.Collections.ObjectModel;
using AI.Assistant.Client.Models;
using AI.Assistant.Core.Rag.Context;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AI.Assistant.Client.ViewModels;

public partial class KnowledgePlaygroundViewModel : ObservableObject
{
    private readonly IRagQueryService _ragQuery;

    [ObservableProperty]
    private string _inputText = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private RagDebugDisplayModel _result = new();

    public KnowledgePlaygroundViewModel(IRagQueryService ragQuery)
    {
        _ragQuery = ragQuery;
    }

    [RelayCommand]
    private async Task ExecuteQueryAsync()
    {
        var query = InputText?.Trim();
        if (string.IsNullOrEmpty(query))
            return;

        IsBusy = true;
        StatusText = "查询中...";
        Result.Clear();

        try
        {
            var ragResult = await _ragQuery.QueryAsync(query);

            if (ragResult.DebugInfo is not null)
                ApplyDebugInfo(ragResult.DebugInfo);

            if (ragResult.HasContext && ragResult.ContextText is not null)
                Result.ContextText = ragResult.ContextText;

            StatusText = ragResult.HasContext
                ? $"完成 — Context 已生成（{ragResult.EstimatedTokens} tokens）"
                : "未生成上下文（未触发 RAG 或无结果）";
        }
        catch (Exception ex)
        {
            StatusText = $"查询失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyDebugInfo(RagDebugInfo info)
    {
        Result.Triggered = info.Triggered;
        Result.Keyword = info.MatchedKeyword ?? "";
        Result.RetrievalElapsed = $"{info.RetrievalElapsed.TotalMilliseconds:F0}ms";
        Result.ContextBuildElapsed = $"{info.ContextBuildElapsed.TotalMilliseconds:F0}ms";
        Result.RawChunks = info.RawChunksReturned;
        Result.ChunksAfterFilter = info.ChunksAfterFilter;
        Result.ChunksUsed = info.ChunksUsedByBuilder;
        Result.EstimatedTokens = info.EstimatedTokens;

        var index = 1;
        foreach (var c in info.Chunks)
        {
            Result.Chunks.Add(new ChunkDisplayModel
            {
                Index = index++,
                FilePath = c.FilePath,
                StartLine = c.StartLine,
                EndLine = c.EndLine,
                Score = c.Score,
                ChunkType = c.ChunkType,
                Language = c.Language ?? ""
            });
        }
    }

    [RelayCommand]
    private void ClearResult()
    {
        Result.Clear();
        StatusText = "";
    }
}
