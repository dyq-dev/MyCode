using System.Text.RegularExpressions;
using AI.Assistant.Core.Interfaces;
using AI.Assistant.Core.Models;

namespace AI.Assistant.Infrastructure.Services;

public class MemoryRetrievalResult
{
    public string? Summary { get; set; }
    public IList<string> Facts { get; set; } = [];
    public IList<string> RawTexts { get; set; } = [];
}

public partial class MemoryService
{
    private readonly IEmbeddingService _embedding;
    private readonly IVectorStore _vectorStore;
    private readonly MemoryRepository _repository;
    private readonly IChatService _chatService;
    private readonly IMemoryFilter _filter;
    private readonly string _collection;

    public MemoryService(IEmbeddingService embedding, IVectorStore vectorStore, MemoryRepository repository, IChatService chatService, IMemoryFilter filter, string collection)
    {
        _embedding = embedding;
        _vectorStore = vectorStore;
        _repository = repository;
        _chatService = chatService;
        _filter = filter;
        _collection = collection;
    }

    /// <summary>
    /// 检索与会话 query 相关的记忆：会话摘要 + 事实 + 原文，三层由高到低。
    /// </summary>
    public async Task<MemoryRetrievalResult> RetrieveAsync(string query, string? sessionId = null, int topK = 3, CancellationToken cancellationToken = default)
    {
        var result = new MemoryRetrievalResult();

        try
        {
            // 1. 会话摘要
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                var summary = await _repository.GetSummaryAsync(sessionId, cancellationToken);
                result.Summary = summary?.Summary;
            }

            // 2. 事实 + 原文（语义检索）
            var vector = await _embedding.EmbedAsync(query, cancellationToken);
            if (vector.Length == 0)
                return result;

            Dictionary<string, string>? filter = null;
            if (!string.IsNullOrWhiteSpace(sessionId))
                filter = new Dictionary<string, string> { ["sessionId"] = sessionId };

            var hits = await _vectorStore.SearchAsync(_collection, vector, topK * 2, filter, cancellationToken);
            if (hits.Count == 0)
                return result;

            // 分离 facts 和 raw
            result.Facts = hits
                .Where(h => h.Metadata.TryGetValue("type", out var t) && t == "fact")
                .Select(h => h.Metadata.TryGetValue("content", out var c) ? c : "")
                .Where(c => !string.IsNullOrEmpty(c))
                .Take(topK)
                .ToList();

            var rawIds = hits
                .Where(h => !h.Metadata.TryGetValue("type", out var t) || t != "fact")
                .Select(h => h.Id).ToList();

            if (rawIds.Count > 0)
            {
                var raws = await _repository.GetByVectorIdsAsync(rawIds, cancellationToken);
                var byId = raws.ToDictionary(r => r.VectorId, r => r.Content);
                result.RawTexts = hits.Where(h => byId.ContainsKey(h.Id))
                    .Select(h => byId[h.Id])
                    .Take(topK)
                    .ToList();
            }
        }
        catch
        {
            // 检索失败不影响对话
        }

        return result;
    }

    /// <summary>
    /// 存储用户消息并异步提取事实 + 更新会话摘要。
    /// 经 MemoryFilter 过滤后跳过无意义内容。
    /// </summary>
    public async Task StoreAsync(string content, string role, string sessionId, string? assistantResponse = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(content))
            return;

        // 记忆过滤：只有有价值的用户消息才进入长期记忆
        var filterResult = _filter.ShouldStore(content, role);
        if (!filterResult.ShouldStore)
            return;

        try
        {
            var vector = await _embedding.EmbedAsync(content, cancellationToken);
            if (vector.Length == 0)
                return;

            var vectorId = Guid.NewGuid().ToString();
            await _vectorStore.UpsertAsync(_collection, vectorId,
                vector, new Dictionary<string, string>
                {
                    ["content"] = content,
                    ["role"] = role,
                    ["sessionId"] = sessionId,
                    ["type"] = "raw"
                }, cancellationToken);

            await _repository.SaveAsync(new MemoryRecord
            {
                Content = content,
                Role = role,
                SessionId = sessionId,
                VectorId = vectorId
            }, cancellationToken);

            // 只有用户消息 + 有助手回复时才做提取+摘要
            if (role == "user" && !string.IsNullOrWhiteSpace(assistantResponse))
            {
                await ExtractFactsAndUpdateSummary(content, assistantResponse, vectorId, sessionId, cancellationToken);
            }
        }
        catch
        {
            // 存储失败不影响主对话
        }
    }

    private async Task ExtractFactsAndUpdateSummary(string userMessage, string assistantResponse, string sourceMessageId, string sessionId, CancellationToken cancellationToken)
    {
        var facts = await ExtractFactsAsync(userMessage, cancellationToken);
        if (facts.Count > 0)
        {
            foreach (var fact in facts)
            {
                var factVector = await _embedding.EmbedAsync(fact.Content, cancellationToken);
                if (factVector.Length == 0) continue;

                var factVectorId = Guid.NewGuid().ToString();
                await _vectorStore.UpsertAsync(_collection, factVectorId,
                    factVector, new Dictionary<string, string>
                    {
                        ["content"] = fact.Content,
                        ["type"] = "fact",
                        ["sessionId"] = sessionId,
                        ["category"] = fact.Category,
                        ["importance"] = fact.Importance.ToString("F2"),
                        ["sourceMessageId"] = fact.SourceMessageId,
                        ["createdAt"] = fact.CreatedAt.ToString("O")
                    }, cancellationToken);
            }
        }

        await UpdateSummaryAsync(userMessage, assistantResponse, sessionId, cancellationToken);
    }

    private static readonly Regex FactLineRegex = FactLineRegexPattern();

    [GeneratedRegex(@"^\[(\w+)\]\s*(.+)$")]
    private static partial Regex FactLineRegexPattern();

    private async Task<List<ExtractedFact>> ExtractFactsAsync(string userMessage, CancellationToken cancellationToken)
    {
        try
        {
            var system = new ChatMessage
            {
                Content = """
                    你是一个事实提取器。从用户消息中提取关于用户的事实陈述、偏好、需求等信息。

                    输出格式（每行一条）：
                    [类别] 事实内容

                    类别可以是：
                    user_profile - 用户身份/个人信息
                    preference - 偏好/习惯
                    project - 项目相关信息
                    technical - 技术相关信息
                    requirement - 需求/要求
                    experience - 经验/经历
                    other - 其他

                    示例：
                    用户说："我不喜欢看视频教程，我更喜欢看文档。"
                    输出：[preference] 用户喜欢看文档学习

                    用户说："我的项目是AI助手框架，用.NET 8开发的"
                    输出：[project] 用户正在开发AI助手框架
                    [technical] 用户使用.NET 8开发

                    如果没有事实可提取，只输出「无」
                    """,
                Role = MessageRole.System
            };

            var reply = await _chatService.SendAsync(userMessage, [system], cancellationToken);
            if (string.IsNullOrWhiteSpace(reply) || reply.Trim() == "无")
                return [];

            var now = DateTime.UtcNow;
            return reply
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(l => !string.IsNullOrWhiteSpace(l) && l != "无" && !l.StartsWith("```"))
                .Select(line =>
                {
                    var match = FactLineRegex.Match(line);
                    if (match.Success)
                    {
                        var category = match.Groups[1].Value;
                        var content = match.Groups[2].Value;
                        return new ExtractedFact
                        {
                            Content = content,
                            Category = category,
                            Importance = GetDefaultImportance(category),
                            CreatedAt = now
                        };
                    }

                    // 兼容无格式输出
                    return new ExtractedFact
                    {
                        Content = line,
                        Category = FactCategory.Other,
                        Importance = 0.5,
                        CreatedAt = now
                    };
                })
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static double GetDefaultImportance(string category) => category switch
    {
        "user_profile" => 0.9,
        "preference" => 0.7,
        "project" => 0.8,
        "technical" => 0.8,
        "requirement" => 0.7,
        "experience" => 0.6,
        _ => 0.5
    };

    private async Task UpdateSummaryAsync(string userMessage, string assistantResponse, string sessionId, CancellationToken cancellationToken)
    {
        try
        {
            var existing = await _repository.GetSummaryAsync(sessionId, cancellationToken);
            var oldSummary = existing?.Summary ?? "";
            var now = DateTime.UtcNow;

            var prompt = $"旧摘要：{oldSummary}\n\n最新对话：\n用户：{userMessage}\n助手：{assistantResponse}\n\n请输出更新后的摘要（一段通顺中文，200字以内）：";

            var system = new ChatMessage
            {
                Content = "你是一个会话摘要助手。根据新对话内容更新会话摘要，包含所有已知的用户信息。直接输出摘要，不要多余内容。",
                Role = MessageRole.System
            };

            var newSummary = await _chatService.SendAsync(prompt, [system], cancellationToken);
            if (!string.IsNullOrWhiteSpace(newSummary))
            {
                await _repository.SaveSummaryAsync(new SessionSummaryRecord
                {
                    SessionId = sessionId,
                    Summary = newSummary.Trim(),
                    CreatedAt = existing?.CreatedAt ?? now,
                    UpdatedAt = now
                }, cancellationToken);
            }
        }
        catch
        {
            // 摘要更新失败不影响后续
        }
    }

    /// <summary>
    /// 首次启动时确保底层存储就绪（mssql 表、Qdrant 集合）。
    /// </summary>
    public async Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        await _repository.EnsureTablesAsync(cancellationToken);
        await _vectorStore.SearchAsync(_collection, new float[512], 1, null, cancellationToken);
    }
}
