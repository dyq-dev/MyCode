using System.Runtime.CompilerServices;
using AI.Assistant.Core.Interfaces;
using AI.Assistant.Core.Models;
using AI.Assistant.Core.Rag.Context;
using AI.Assistant.Core.Rag.Prompt;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("AI.Assistant.Client")]

namespace AI.Assistant.Infrastructure.Services.Chat;

/// <summary>
/// IChatService 装饰器——在每次用户消息发送前执行 RAG 查询，
/// 将代码上下文注入到 System 消息之前（映射为 Context 角色）。
/// RAG 失败不阻断聊天，静默降级。
/// </summary>
public sealed class RagChatService : IChatService
{
    private readonly IChatService _inner;
    private readonly IRagQueryService _ragQuery;
    private readonly IRagPromptBuilder _promptBuilder;
    private readonly ILogger<RagChatService> _logger;

    internal RagQueryResult? LastRagResult { get; private set; }

    public RagChatService(IChatService inner, IRagQueryService ragQuery, IRagPromptBuilder promptBuilder, ILogger<RagChatService>? logger = null)
    {
        _inner = inner;
        _ragQuery = ragQuery;
        _promptBuilder = promptBuilder;
        _logger = logger ?? NullLogger<RagChatService>.Instance;
    }

    public IAsyncEnumerable<string> StreamAsync(
        string message,
        IEnumerable<ChatMessage> history,
        CancellationToken cancellationToken = default)
    {
        return StreamAsyncInternal(message, history, cancellationToken);
    }

    private async IAsyncEnumerable<string> StreamAsyncInternal(
        string message,
        IEnumerable<ChatMessage> history,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var result = await SafeQueryAsync(message, ct);
        LastRagResult = result;

        var modified = BuildHistory(history, result?.ContextText);

        await foreach (var chunk in _inner.StreamAsync(message, modified, ct))
        {
            yield return chunk;
        }
    }

    public Task<string> SendAsync(
        string message,
        IEnumerable<ChatMessage> history,
        CancellationToken cancellationToken = default)
    {
        return SendAsyncInternal(message, history, cancellationToken);
    }

    private async Task<string> SendAsyncInternal(
        string message,
        IEnumerable<ChatMessage> history,
        CancellationToken ct)
    {
        var result = await SafeQueryAsync(message, ct);
        LastRagResult = result;

        var modified = BuildHistory(history, result?.ContextText);

        return await _inner.SendAsync(message, modified, ct);
    }

    private async Task<RagQueryResult?> SafeQueryAsync(
        string message,
        CancellationToken ct)
    {
        try
        {
            var result = await _ragQuery.QueryAsync(message, ct);
            _logger.LogDebug(
                "SafeQuery: message='{Msg}', triggered={T}, hasContext={C}, chunksUsed={U}",
                message, result?.DebugInfo?.Triggered, result?.HasContext, result?.ChunksUsed);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RAG query failed, falling back: message='{Msg}'", message);
            return null;
        }
    }

    private List<ChatMessage> BuildHistory(
        IEnumerable<ChatMessage> history,
        string? contextText)
    {
        var list = history.ToList();

        if (contextText is null)
        {
            _logger.LogDebug("BuildHistory: no context, history unchanged (count={Count})", list.Count);
            return list;
        }

        var prompt = _promptBuilder.Build(contextText);
        var contextMsg = new ChatMessage
        {
            Role = MessageRole.Context,
            Content = prompt
        };

        var systemIndex = list.FindIndex(m => m.Role == MessageRole.System);

        if (systemIndex >= 0)
            list.Insert(systemIndex + 1, contextMsg);
        else
            list.Insert(0, contextMsg);

        var result = list
            .Select(m => m.Role == MessageRole.Context
                ? new ChatMessage
                {
                    Role = MessageRole.System,
                    Content = m.Content
                }
                : m)
            .ToList();

        _logger.LogDebug(
            "BuildHistory: injected prompt ({Len} chars) after system slot, " +
            "history count {Before}→{After}",
            prompt.Length, list.Count - 1, result.Count);

        return result;
    }
}
