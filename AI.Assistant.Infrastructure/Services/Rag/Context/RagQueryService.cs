using System.Diagnostics;
using AI.Assistant.Core.Interfaces;
using AI.Assistant.Core.Rag.Context;
using AI.Assistant.Core.Rag.Interfaces;
using AI.Assistant.Core.Rag.Models;
using AI.Assistant.Core.Rag.Options;
using AI.Assistant.Infrastructure.Services.Rag.Retrieval;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AI.Assistant.Infrastructure.Services.Rag.Context;

public class RagQueryService : IRagQueryService
{
    private readonly IRetriever _retriever;
    private readonly IRagContextBuilder _contextBuilder;
    private readonly IOptions<RagOptions> _options;
    private readonly ILogger<RagQueryService> _logger;
    private readonly IEmbeddingService? _embedding;
    private readonly KnowledgeQueryStore? _knowledgeQueryStore;

    public RagQueryService(
        IRetriever retriever,
        IRagContextBuilder contextBuilder,
        IOptions<RagOptions> options,
        ILogger<RagQueryService> logger,
        IEmbeddingService? embedding = null,
        KnowledgeQueryStore? knowledgeQueryStore = null)
    {
        _retriever = retriever;
        _contextBuilder = contextBuilder;
        _options = options;
        _logger = logger;
        _embedding = embedding;
        _knowledgeQueryStore = knowledgeQueryStore;
    }

    public async Task<RagQueryResult> QueryAsync(
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        var opts = _options.Value;
        var sw = Stopwatch.StartNew();

        if (opts.EnableDebugLog)
            _logger.LogDebug("RAG query: query='{Query}'", userMessage);

        IList<RetrievedKnowledgeChunk> codeResults;
        try
        {
            codeResults = await _retriever.VectorSearchAsync(
                userMessage, cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            if (opts.EnableDebugLog)
                _logger.LogWarning(ex, "RAG retrieval error for query '{Query}'", userMessage);
            return BuildErrorResult(userMessage, ex.Message, opts);
        }

        var codeRawCount = codeResults.Count;

        var filteredCode = opts.MinimumScoreThreshold > 0 && codeResults.Count > 0
            ? codeResults.Where(c => c.Score >= opts.MinimumScoreThreshold).ToList()
            : [.. codeResults];

        IList<RetrievedKnowledgeChunk> mixedResults = [];
        if (_embedding is not null && _knowledgeQueryStore is not null)
        {
            try
            {
                var queryVector = await _embedding.EmbedAsync(userMessage, cancellationToken);
                mixedResults = await _knowledgeQueryStore.SearchAsync(
                    queryVector, opts.MaxTopK, cancellationToken);
            }
            catch (Exception ex)
            {
                if (opts.EnableDebugLog)
                    _logger.LogWarning(ex, "Cross-source retrieval failed, falling back to code-only");
            }
        }

        sw.Stop();

        var merged = MergeResults(filteredCode, mixedResults, opts);

        if (merged.Count == 0)
        {
            if (opts.EnableDebugLog)
                _logger.LogDebug(
                    "RAG no results after merge: codeRaw={CodeRaw}, codeAfterFilter={CodeAfter}, mixed={Mixed}",
                    codeRawCount, filteredCode.Count, mixedResults.Count);

            return BuildEmptyResult(userMessage, opts,
                sw.Elapsed, codeRawCount, filteredCode.Count, codeResults);
        }

        var contextText = string.Join("\n---\n", merged.Select(x => x.Text));

        if (opts.EnableDebugLog)
            _logger.LogDebug(
                "RAG success: codeRaw={CodeRaw}, codeAfterFilter={CodeAfter}, mixed={Mixed}, used={Used}, tokens={Tokens}",
                codeRawCount, filteredCode.Count, mixedResults.Count,
                merged.Count, contextText.Length / 3);

        var debugInfo = opts.EnableDebugInfo
            ? BuildDebugInfo(userMessage, opts.MinimumScoreThreshold,
                sw.Elapsed, codeRawCount, filteredCode.Count, merged.Count, contextText, codeResults)
            : null;

        return new RagQueryResult
        {
            HasContext = true,
            ContextText = contextText,
            ChunksUsed = merged.Count,
            EstimatedTokens = contextText.Length / 3,
            DebugInfo = debugInfo
        };
    }

    private static List<(float Score, string Text)> MergeResults(
        IList<RetrievedKnowledgeChunk> codeResults,
        IList<RetrievedKnowledgeChunk> mixedResults,
        RagOptions opts)
    {
        var threshold = opts.MinimumScoreThreshold;

        var items = new List<(float Score, string Text)>();

        foreach (var r in codeResults)
            items.Add((r.Score, $"[代码] {r.Chunk.Content}"));

        foreach (var r in mixedResults)
        {
            if (threshold <= 0 || r.Score >= threshold)
                items.Add((r.Score, $"[文档] {r.Chunk.Content}"));
        }

        return items
            .OrderByDescending(x => x.Score)
            .Take(opts.MaxTopK)
            .ToList();
    }

    private static RagQueryResult BuildErrorResult(
        string userMessage, string errorMessage, RagOptions opts)
    {
        return new RagQueryResult
        {
            HasContext = false,
            ErrorMessage = errorMessage,
            DebugInfo = opts.EnableDebugInfo
                ? new RagDebugInfo { UserQuery = userMessage, Triggered = true }
                : null
        };
    }

    private static RagQueryResult BuildEmptyResult(
        string userMessage, RagOptions opts,
        TimeSpan retrievalElapsed, int rawCount, int afterFilter,
        IList<RetrievedKnowledgeChunk> originalChunks)
    {
        return new RagQueryResult
        {
            HasContext = false,
            ChunksUsed = 0,
            DebugInfo = opts.EnableDebugInfo
                ? new RagDebugInfo
                {
                    UserQuery = userMessage,
                    Triggered = true,
                    MinimumScoreThreshold = opts.MinimumScoreThreshold,
                    RetrievalElapsed = retrievalElapsed,
                    RawChunksReturned = rawCount,
                    ChunksAfterFilter = afterFilter,
                    EstimatedTokens = 0,
                    Chunks = MapChunks(originalChunks)
                }
                : null
        };
    }

    private static RagDebugInfo BuildDebugInfo(
        string userMessage,
        double threshold,
        TimeSpan retrievalElapsed,
        int rawCount,
        int afterFilter,
        int totalUsed,
        string contextText,
        IList<RetrievedKnowledgeChunk> chunks)
    {
        return new RagDebugInfo
        {
            UserQuery = userMessage,
            Triggered = true,
            MinimumScoreThreshold = threshold,
            RetrievalElapsed = retrievalElapsed,
            ContextBuildElapsed = TimeSpan.Zero,
            RawChunksReturned = rawCount,
            ChunksAfterFilter = afterFilter,
            ChunksUsedByBuilder = totalUsed,
            EstimatedTokens = contextText.Length / 3,
            Chunks = MapChunks(chunks)
        };
    }

    private static IReadOnlyList<RagChunkDebugInfo> MapChunks(
        IList<RetrievedKnowledgeChunk> chunks)
    {
        return chunks.Select(c => new RagChunkDebugInfo
        {
            FilePath = c.Chunk is CodeChunk codeChunk ? codeChunk.FilePath : c.Chunk.SourceUri,
            StartLine = c.Chunk is CodeChunk cc ? cc.StartLine : 0,
            EndLine = c.Chunk is CodeChunk ec ? ec.EndLine : 0,
            Score = c.Score,
            Language = c.Chunk is CodeChunk lc ? lc.Language : "",
            ChunkType = c.Chunk is CodeChunk tc ? tc.ChunkType.ToString() : "Generic"
        }).ToList();
    }

}
