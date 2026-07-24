using AI.Assistant.Core.Rag.Context;
using AI.Assistant.Core.Rag.Models;

namespace AI.Assistant.Infrastructure.Services.Rag.Context;

/// <summary>
/// RAG 上下文构建器——将检索到的代码分块拼接为纯文本上下文。
/// 支持：按文件分组、估算 token、裁剪超长、每文件上限。
/// </summary>
public class RagContextBuilder : IRagContextBuilder
{
    private readonly RagContextOptions _options;

    public RagContextBuilder(RagContextOptions options)
    {
        _options = options;
    }

    public Task<RagContext> BuildAsync(
        IList<RetrievedCodeChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var totalRetrieved = chunks.Count;

        if (totalRetrieved == 0)
        {
            return Task.FromResult(new RagContext
            {
                ContextText = "",
                Sources = [],
                EstimatedTokens = 0,
                TotalRetrieved = 0,
                TotalUsed = 0
            });
        }

        // 1) 按 Score 降序排列，高相关度优先
        var sorted = chunks.OrderByDescending(c => c.Score).ToList();

        // 2) 每文件只保留 MaxChunksPerFile 条，避免单个文件占满上下文
        var perFileLimited = sorted
            .GroupBy(c => c.Chunk.FilePath, StringComparer.OrdinalIgnoreCase)
            .SelectMany(g => g.Take(_options.MaxChunksPerFile))
            .ToList();

        // 3) 取全局 Top MaxChunks
        var topChunks = perFileLimited.Take(_options.MaxChunks).ToList();

        // 4) 分组拼接
        IReadOnlyList<RetrievedCodeChunk> finalSources;
        string contextText;

        if (_options.GroupByFile)
            finalSources = BuildGroupedText(topChunks, out contextText);
        else
            finalSources = BuildFlatText(topChunks, out contextText);

        return Task.FromResult(new RagContext
        {
            ContextText = contextText,
            Sources = finalSources,
            EstimatedTokens = contextText.Length / 3,
            TotalRetrieved = totalRetrieved,
            TotalUsed = finalSources.Count
        });
    }

    /// <summary>按文件分组拼接，组内按 StartLine 排序，组间按最高 Score 排序</summary>
    private IReadOnlyList<RetrievedCodeChunk> BuildGroupedText(
        List<RetrievedCodeChunk> chunks, out string contextText)
    {
        var grouped = chunks
            .GroupBy(c => c.Chunk.FilePath, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Max(c => c.Score))
            .ToList();

        var sb = new System.Text.StringBuilder();
        var used = new List<RetrievedCodeChunk>();
        var accumulatedTokens = 0;
        var hasContent = false;

        if (!string.IsNullOrEmpty(_options.Prefix))
        {
            sb.AppendLine(_options.Prefix);
            sb.AppendLine("---");
            hasContent = true;
        }

        foreach (var fileGroup in grouped)
        {
            var groupStopped = false;

            foreach (var chunk in fileGroup.OrderBy(c => c.Chunk.StartLine))
            {
                var segment = BuildChunkSegment(chunk);
                var segmentTokens = segment.Length / 3;

                if (used.Count > 0 && accumulatedTokens + segmentTokens > _options.MaxContextTokens)
                {
                    groupStopped = true;
                    break;
                }

                sb.Append(segment);
                accumulatedTokens += segmentTokens;
                used.Add(chunk);
                hasContent = true;
            }

            if (groupStopped)
                break;
        }

        contextText = hasContent ? sb.ToString().TrimEnd() : "";
        return used;
    }

    /// <summary>平铺拼接，保持传入顺序</summary>
    private IReadOnlyList<RetrievedCodeChunk> BuildFlatText(
        List<RetrievedCodeChunk> chunks, out string contextText)
    {
        var sb = new System.Text.StringBuilder();
        var used = new List<RetrievedCodeChunk>();
        var accumulatedTokens = 0;

        if (!string.IsNullOrEmpty(_options.Prefix))
        {
            sb.AppendLine(_options.Prefix);
            sb.AppendLine("---");
        }

        foreach (var chunk in chunks)
        {
            var segment = BuildChunkSegment(chunk);
            var segmentTokens = segment.Length / 3;

            if (used.Count > 0 && accumulatedTokens + segmentTokens > _options.MaxContextTokens)
                break;

            sb.Append(segment);
            accumulatedTokens += segmentTokens;
            used.Add(chunk);
        }

        contextText = sb.ToString().TrimEnd();
        return used;
    }

    /// <summary>生成单个分块的文本段落（含文件头 + 代码块）</summary>
    private string BuildChunkSegment(RetrievedCodeChunk chunk)
    {
        var c = chunk.Chunk;
        var sb = new System.Text.StringBuilder();

        if (_options.ShowLineNumbers)
            sb.AppendLine($"[文件: {c.FilePath} (第 {c.StartLine}-{c.EndLine} 行)]");
        else
            sb.AppendLine($"[文件: {c.FilePath}]");

        sb.AppendLine($"```{c.Language}");
        sb.AppendLine(c.Content);
        sb.AppendLine("```");
        sb.AppendLine();

        return sb.ToString();
    }
}
