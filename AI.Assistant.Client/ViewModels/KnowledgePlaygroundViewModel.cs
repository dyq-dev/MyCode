using System.Collections.ObjectModel;
using AI.Assistant.Client.Models;
using AI.Assistant.Core.Interfaces;
using AI.Assistant.Core.Rag.Context;
using AI.Assistant.Core.Rag.Models;
using AI.Assistant.Infrastructure.Services.Rag.Retrieval;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AI.Assistant.Client.ViewModels;

public partial class KnowledgePlaygroundViewModel : ObservableObject
{
    private readonly IRagQueryService _ragQuery;
    private readonly KnowledgeQueryStore _knowledgeQueryStore;
    private readonly IEmbeddingService _embeddingService;

    public ObservableCollection<RetrievedKnowledgeChunk> MixedSearchResults { get; } = [];

    [ObservableProperty]
    private string _inputText = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private RagDebugDisplayModel _result = new();

    public KnowledgePlaygroundViewModel(
        IRagQueryService ragQuery,
        KnowledgeQueryStore knowledgeQueryStore,
        IEmbeddingService embeddingService)
    {
        _ragQuery = ragQuery;
        _knowledgeQueryStore = knowledgeQueryStore;
        _embeddingService = embeddingService;
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
        MixedSearchResults.Clear();

        try
        {
            var ragQueryTask = _ragQuery.QueryAsync(query);
            var vectorTask = _embeddingService.EmbedAsync(query);

            var ragResult = await ragQueryTask;
            var queryVector = await vectorTask;

            if (ragResult.DebugInfo is not null)
                ApplyDebugInfo(ragResult.DebugInfo);

            if (ragResult.HasContext && ragResult.ContextText is not null)
                Result.ContextText = ragResult.ContextText;

            StatusText = ragResult.HasContext
                ? $"完成 — Context 已生成（{ragResult.EstimatedTokens} tokens）"
                : "未生成上下文（未触发 RAG 或无结果）";

            var mixedResults = await _knowledgeQueryStore.SearchAsync(queryVector, cancellationToken: default);
            foreach (var r in mixedResults)
                MixedSearchResults.Add(r);
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
        MixedSearchResults.Clear();
        StatusText = "";
    }
}
