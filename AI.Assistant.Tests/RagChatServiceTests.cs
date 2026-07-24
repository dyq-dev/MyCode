using System.Runtime.CompilerServices;
using AI.Assistant.Core.Interfaces;
using AI.Assistant.Core.Models;
using AI.Assistant.Core.Rag.Context;
using AI.Assistant.Core.Rag.Prompt;
using AI.Assistant.Infrastructure.Services.Chat;

namespace AI.Assistant.Tests;

public class RagChatServiceTests
{
    private readonly FakeChatService _inner = new();
    private readonly FakeRagQueryService _ragQuery = new();
    private readonly FakeRagPromptBuilder _promptBuilder = new();
    private readonly RagChatService _service;

    public RagChatServiceTests()
    {
        _service = new RagChatService(_inner, _ragQuery, _promptBuilder);
    }

    // ============ SendAsync ============

    [Fact]
    public async Task SendAsync_NormalMessage_PassesHistoryUnchanged()
    {
        var history = MakeHistory(new[] { "hello", "hi!" });

        await _service.SendAsync("今天天气不错", history);

        Assert.Equal("今天天气不错", _inner.LastMessage);
        Assert.Equal(4, _inner.LastHistory!.Count);
        Assert.Equal(MessageRole.System, _inner.LastHistory[0].Role);
        Assert.Equal(MessageRole.User, _inner.LastHistory[1].Role);
        Assert.Equal("hello", _inner.LastHistory[1].Content);
    }

    [Fact]
    public async Task SendAsync_RagReturnsContext_ContextInjectedAfterSystem()
    {
        _ragQuery.Result = new RagQueryResult
        {
            HasContext = true,
            ContextText = "## 代码上下文\nclass Foo { }"
        };
        var history = MakeHistory(new[] { "你好", "请帮我查一下 UserService 类" });

        await _service.SendAsync("UserService 类在哪里", history);

        // history: [System, User("你好"), Assistant, User("请帮我...")] + Context → 5
        Assert.Equal(5, _inner.LastHistory!.Count);
        Assert.Equal(MessageRole.System, _inner.LastHistory[0].Role);
        Assert.Equal(MessageRole.System, _inner.LastHistory[1].Role);
        Assert.Contains("class Foo", _inner.LastHistory[1].Content);
        Assert.Equal(MessageRole.User, _inner.LastHistory[2].Role);
    }

    [Fact]
    public async Task SendAsync_RagReturnsNoContext_NoInjection()
    {
        _ragQuery.Result = new RagQueryResult { HasContext = false };
        var history = MakeHistory(new[] { "hello", "hi!" });

        await _service.SendAsync("这个类怎么用", history);

        Assert.Equal(4, _inner.LastHistory!.Count);
    }

    [Fact]
    public async Task SendAsync_RagThrows_ChatProceedsWithoutContext()
    {
        _ragQuery.Exception = new InvalidOperationException("fail");

        var history = MakeHistory(new[] { "hello" });

        await _service.SendAsync("接口定义", history);

        Assert.Equal(2, _inner.LastHistory!.Count);
        Assert.Equal("inner response", await _inner.GetLastResult());
    }

    [Fact]
    public async Task SendAsync_NoSystemMessage_ContextAtStart()
    {
        _ragQuery.Result = new RagQueryResult
        {
            HasContext = true,
            ContextText = "some context"
        };
        var history = new List<ChatMessage>
        {
            new() { Role = MessageRole.User, Content = "hello" },
            new() { Role = MessageRole.Assistant, Content = "hi" }
        };

        await _service.SendAsync("class 用法", history);

        Assert.Equal(3, _inner.LastHistory!.Count);
        Assert.Equal(MessageRole.System, _inner.LastHistory[0].Role);
        Assert.Equal("some context", _inner.LastHistory[0].Content);
    }

    [Fact]
    public async Task SendAsync_OriginalMessagePreserved()
    {
        _ragQuery.Result = new RagQueryResult
        {
            HasContext = true,
            ContextText = "ctx"
        };

        await _service.SendAsync("原始消息不变", MakeHistory([]));

        Assert.Equal("原始消息不变", _inner.LastMessage);
    }

    // ============ StreamAsync ============

    [Fact]
    public async Task StreamAsync_RagReturnsContext_InjectsContext()
    {
        _ragQuery.Result = new RagQueryResult
        {
            HasContext = true,
            ContextText = "STREAM CTX"
        };
        var history = MakeHistory(new[] { "hello" });
        var chunks = new List<string>();

        await foreach (var chunk in _service.StreamAsync("这个接口在哪", history))
        {
            chunks.Add(chunk);
        }

        Assert.Equal(3, _inner.LastHistory!.Count);
        Assert.Equal(MessageRole.System, _inner.LastHistory[1].Role);
        Assert.Equal("STREAM CTX", _inner.LastHistory[1].Content);
        Assert.Equal("inner response", string.Concat(chunks));
    }

    [Fact]
    public async Task StreamAsync_NormalMessage_NoContextInjection()
    {
        var history = MakeHistory(new[] { "hello" });
        var chunks = new List<string>();

        await foreach (var chunk in _service.StreamAsync("天气不错", history))
        {
            chunks.Add(chunk);
        }

        Assert.Equal(2, _inner.LastHistory!.Count);
    }

    [Fact]
    public async Task StreamAsync_RagThrows_StreamProceeds()
    {
        _ragQuery.Exception = new InvalidOperationException("fail");
        var history = MakeHistory(new[] { "hello" });
        var chunks = new List<string>();

        await foreach (var chunk in _service.StreamAsync("代码怎么查", history))
        {
            chunks.Add(chunk);
        }

        Assert.NotEmpty(chunks);
        Assert.Equal(2, _inner.LastHistory!.Count);
    }

    // ============ 帮助方法 ============

    private static List<ChatMessage> MakeHistory(string[] userMessages)
    {
        var list = new List<ChatMessage>
        {
            new() { Role = MessageRole.System, Content = "" }
        };
        foreach (var msg in userMessages)
        {
            list.Add(new() { Role = MessageRole.User, Content = msg });
            list.Add(new() { Role = MessageRole.Assistant, Content = "ok" });
        }
        // 移除末尾多余的 Assistant（用户最后一条后面不应有回复）
        if (list.Count > 1 && list[^1].Role == MessageRole.Assistant)
            list.RemoveAt(list.Count - 1);
        return list;
    }

    // ============ Fakes ============

    private sealed class FakeChatService : IChatService
    {
        public string? LastMessage { get; private set; }
        public IList<ChatMessage>? LastHistory { get; private set; }
        public string Response { get; set; } = "inner response";

        public Task<string> SendAsync(string message, IEnumerable<ChatMessage> history, CancellationToken ct = default)
        {
            LastMessage = message;
            LastHistory = history.ToList();
            return Task.FromResult(Response);
        }

        public async IAsyncEnumerable<string> StreamAsync(
            string message,
            IEnumerable<ChatMessage> history,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            LastMessage = message;
            LastHistory = history.ToList();
            yield return Response;
            await Task.CompletedTask;
        }

        public Task<string> GetLastResult() => Task.FromResult(Response);
    }

    private sealed class FakeRagPromptBuilder : IRagPromptBuilder
    {
        public string Build(string contextText) => contextText;
    }

    private sealed class FakeRagQueryService : IRagQueryService
    {
        public RagQueryResult Result { get; set; } = new() { HasContext = false };
        public Exception? Exception { get; set; }
        public string? LastQuery { get; private set; }

        public Task<RagQueryResult> QueryAsync(string userMessage, CancellationToken ct = default)
        {
            LastQuery = userMessage;
            if (Exception is not null)
                throw Exception;
            return Task.FromResult(Result);
        }
    }
}
