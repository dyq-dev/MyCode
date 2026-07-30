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
    private readonly ICodeRetriever _retriever;
    private readonly IRagContextBuilder _contextBuilder;
    private readonly IOptions<RagOptions> _options;
    private readonly ILogger<RagQueryService> _logger;
    private readonly IEmbeddingService? _embedding;
    private readonly KnowledgeQueryStore? _knowledgeQueryStore;

    public RagQueryService(
        ICodeRetriever retriever,
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
        var matchedKeyword = FindMatchedKeyword(userMessage);
        var triggered = matchedKeyword != null;

        if (opts.EnableDebugLog)
            _logger.LogDebug(
                "RAG query: query='{Query}', triggered={Triggered}, keyword={Keyword}",
                userMessage, triggered, matchedKeyword);

        if (!triggered)
            return BuildSkippedResult(userMessage, opts);

        var sw = Stopwatch.StartNew();

        // === Phase 1: Code retrieval (existing) ===
        IList<RetrievedCodeChunk> codeChunks;
        try
        {
            codeChunks = await _retriever.VectorSearchAsync(
                userMessage, cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            if (opts.EnableDebugLog)
                _logger.LogWarning(ex, "RAG code retrieval error for query '{Query}'", userMessage);
            return BuildErrorResult(userMessage, ex.Message, opts);
        }

        var codeRawCount = codeChunks.Count;

        var filteredCode = opts.MinimumScoreThreshold > 0 && codeChunks.Count > 0
            ? codeChunks.Where(c => c.Score >= opts.MinimumScoreThreshold).ToList()
            : codeChunks.ToList();

        // === Phase 2: Cross-source retrieval ===
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

        // === Phase 3: Merge & format ===
        var merged = MergeResults(filteredCode, mixedResults, opts);

        if (merged.Count == 0)
        {
            if (opts.EnableDebugLog)
                _logger.LogDebug(
                    "RAG no results after merge: codeRaw={CodeRaw}, codeAfterFilter={CodeAfter}, mixed={Mixed}",
                    codeRawCount, filteredCode.Count, mixedResults.Count);

            return BuildEmptyResult(userMessage, opts, matchedKeyword,
                sw.Elapsed, codeRawCount, filteredCode.Count, codeChunks);
        }

        var contextText = string.Join("\n---\n", merged.Select(x => x.Text));

        if (opts.EnableDebugLog)
            _logger.LogDebug(
                "RAG success: codeRaw={CodeRaw}, codeAfterFilter={CodeAfter}, mixed={Mixed}, used={Used}, tokens={Tokens}",
                codeRawCount, filteredCode.Count, mixedResults.Count,
                merged.Count, contextText.Length / 3);

        var debugInfo = opts.EnableDebugInfo
            ? BuildDebugInfo(userMessage, matchedKeyword, opts.MinimumScoreThreshold,
                sw.Elapsed, codeRawCount, filteredCode.Count, merged.Count, contextText, codeChunks)
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

    // ============ 合并逻辑 ============

    private static List<(float Score, string Text)> MergeResults(
        IList<RetrievedCodeChunk> codeResults,
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

    // ============ 结果构造 ============

    private static RagQueryResult BuildSkippedResult(
        string userMessage, RagOptions opts)
    {
        return new RagQueryResult
        {
            HasContext = false,
            DebugInfo = opts.EnableDebugInfo
                ? new RagDebugInfo { UserQuery = userMessage, Triggered = false }
                : null
        };
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
        string userMessage, RagOptions opts, string? matchedKeyword,
        TimeSpan retrievalElapsed, int rawCount, int afterFilter,
        IList<RetrievedCodeChunk> originalChunks)
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
                    MatchedKeyword = matchedKeyword,
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
        string? matchedKeyword,
        double threshold,
        TimeSpan retrievalElapsed,
        int rawCount,
        int afterFilter,
        int totalUsed,
        string contextText,
        IList<RetrievedCodeChunk> chunks)
    {
        return new RagDebugInfo
        {
            UserQuery = userMessage,
            Triggered = true,
            MatchedKeyword = matchedKeyword,
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
        IList<RetrievedCodeChunk> chunks)
    {
        return chunks.Select(c => new RagChunkDebugInfo
        {
            FilePath = c.Chunk.FilePath,
            StartLine = c.Chunk.StartLine,
            EndLine = c.Chunk.EndLine,
            Score = c.Score,
            Language = c.Chunk.Language,
            ChunkType = c.Chunk.ChunkType.ToString()
        }).ToList();
    }

    // ============ 关键词匹配 ============

    private string? FindMatchedKeyword(string message)
    {
        var keywords = _options.Value.RagKeywords;
        foreach (var keyword in keywords)
        {
            if (message.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return keyword;
        }
        return null;
    }
}
