using System.Diagnostics;
using AI.Assistant.Core.Rag.Context;
using AI.Assistant.Core.Rag.Interfaces;
using AI.Assistant.Core.Rag.Models;
using AI.Assistant.Core.Rag.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AI.Assistant.Infrastructure.Services.Rag.Context;

public class RagQueryService : IRagQueryService
{
    private readonly ICodeRetriever _retriever;
    private readonly IRagContextBuilder _contextBuilder;
    private readonly IOptions<RagOptions> _options;
    private readonly ILogger<RagQueryService> _logger;

    public RagQueryService(
        ICodeRetriever retriever,
        IRagContextBuilder contextBuilder,
        IOptions<RagOptions> options,
        ILogger<RagQueryService> logger)
    {
        _retriever = retriever;
        _contextBuilder = contextBuilder;
        _options = options;
        _logger = logger;
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
        {
            return BuildSkippedResult(userMessage, opts);
        }

        var sw = Stopwatch.StartNew();
        IList<RetrievedCodeChunk> chunks;
        try
        {
            chunks = await _retriever.VectorSearchAsync(
                userMessage, cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (opts.EnableDebugLog)
                _logger.LogWarning(ex, "RAG retrieval error for query '{Query}'", userMessage);

            return BuildErrorResult(userMessage, ex.Message, opts);
        }
        sw.Stop();

        var rawCount = chunks.Count;

        IList<RetrievedCodeChunk> filtered = chunks;
        if (opts.MinimumScoreThreshold > 0 && chunks.Count > 0)
        {
            filtered = chunks
                .Where(c => c.Score >= opts.MinimumScoreThreshold)
                .ToList();
        }

        var afterFilter = filtered.Count;

        if (filtered.Count == 0)
        {
            if (opts.EnableDebugLog)
                _logger.LogDebug(
                    "RAG no results after threshold: raw={Raw}, afterFilter={After}, threshold={Threshold}",
                    rawCount, afterFilter, opts.MinimumScoreThreshold);

            return BuildEmptyResult(userMessage, opts, matchedKeyword,
                sw.Elapsed, rawCount, afterFilter, chunks);
        }

        var sw2 = Stopwatch.StartNew();
        var context = await _contextBuilder.BuildAsync(filtered, cancellationToken);
        sw2.Stop();

        if (opts.EnableDebugLog)
            _logger.LogDebug(
                "RAG success: raw={Raw}, afterFilter={After}, used={Used}, " +
                "tokens={Tokens}, retrievalElapsed={RetElapsed}ms, buildElapsed={BuildElapsed}ms",
                rawCount, afterFilter, context.TotalUsed, context.EstimatedTokens,
                sw.ElapsedMilliseconds, sw2.ElapsedMilliseconds);

        var debugInfo = opts.EnableDebugInfo
            ? BuildDebugInfo(userMessage, matchedKeyword, opts.MinimumScoreThreshold,
                sw.Elapsed, sw2.Elapsed, rawCount, afterFilter, context, chunks)
            : null;

        return new RagQueryResult
        {
            HasContext = true,
            ContextText = context.ContextText,
            ChunksUsed = context.TotalUsed,
            EstimatedTokens = context.EstimatedTokens,
            DebugInfo = debugInfo
        };
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
        TimeSpan buildElapsed,
        int rawCount,
        int afterFilter,
        RagContext context,
        IList<RetrievedCodeChunk> chunks)
    {
        return new RagDebugInfo
        {
            UserQuery = userMessage,
            Triggered = true,
            MatchedKeyword = matchedKeyword,
            MinimumScoreThreshold = threshold,
            RetrievalElapsed = retrievalElapsed,
            ContextBuildElapsed = buildElapsed,
            RawChunksReturned = rawCount,
            ChunksAfterFilter = afterFilter,
            ChunksUsedByBuilder = context.TotalUsed,
            EstimatedTokens = context.EstimatedTokens,
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
