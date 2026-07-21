using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AI.Assistant.Core.Interfaces;
using AI.Assistant.Core.Models;

namespace AI.Assistant.Infrastructure.Services;

/// <summary>
/// Ollama 聊天服务实现
/// 通过 HTTP 调用本地 Ollama API 实现 AI 对话
/// API 文档：https://github.com/ollama/ollama/blob/main/docs/api.md
/// </summary>
public class OllamaChatService : IChatService
{
    private readonly HttpClient _httpClient;
    private readonly string _model;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true  // JSON 属性名不区分大小写
    };

    /// <param name="httpClient">由 DI 注入的 HttpClient，BaseAddress 已配置为 Ollama 地址</param>
    /// <param name="model">使用的模型名称，如 "gemma3:1b"、"llama3.2" 等</param>
    public OllamaChatService(HttpClient httpClient, string model = "gemma3:1b")
    {
        _httpClient = httpClient;
        _model = model;
    }

    /// <summary>
    /// 非流式发送消息 - 等待 AI 生成完整回复后一次性返回
    /// 优点：实现简单
    /// 缺点：用户需等待较长时间才能看到回复
    /// </summary>
    public async Task<string> SendAsync(string message, IEnumerable<ChatMessage> history, CancellationToken cancellationToken = default)
    {
        // 构建消息列表（历史记录 + 当前消息）
        var messages = BuildMessages(message, history);
        var request = new OllamaChatRequest
        {
            Model = _model,
            Messages = messages,
            Stream = false  // 非流式
        };

        // POST http://localhost:11434/api/chat
        var response = await _httpClient.PostAsJsonAsync("api/chat", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        // 解析响应，提取 AI 回复内容
        var result = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(cancellationToken: cancellationToken);
        return result?.Message?.Content ?? string.Empty;
    }

    /// <summary>
    /// 流式发送消息 - AI 逐字/逐句返回回复，用户体验更好
    /// Ollama 返回 JSONL 格式：每行一个 JSON 对象，包含一个 token
    /// 示例：{"model":"gemma3:1b","message":{"role":"assistant","content":"你"},"done":false}
    /// </summary>
    public async IAsyncEnumerable<string> StreamAsync(string message, IEnumerable<ChatMessage> history, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var messages = BuildMessages(message, history);
        var request = new OllamaChatRequest
        {
            Model = _model,
            Messages = messages,
            Stream = true  // 流式模式
        };

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "api/chat")
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };

        // 使用 ResponseHeadersRead 避免缓冲整个响应，实现真正的流式读取
        using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        // 逐行读取流式响应
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line)) continue;

            // 解析 JSON 行，提取文本片段
            var chunk = ParseChunk(line);
            if (chunk?.Message?.Content != null)
            {
                yield return chunk.Message.Content;  // 逐片段返回给 UI 显示
            }

            // done=true 表示 AI 已完成回复
            if (chunk?.Done == true) break;
        }
    }

    /// <summary>
    /// 解析单行 JSON 为流式响应块，解析失败返回 null（跳过错误行）
    /// </summary>
    private static OllamaStreamChunk? ParseChunk(string line)
    {
        try
        {
            return JsonSerializer.Deserialize<OllamaStreamChunk>(line, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 构建发送给 Ollama 的消息列表
    /// 将内部的 ChatMessage 模型转换为 Ollama API 格式
    /// </summary>
    private List<OllamaMessage> BuildMessages(string userMessage, IEnumerable<ChatMessage> history)
    {
        var messages = new List<OllamaMessage>();

        // 先添加历史消息，保持上下文连贯
        foreach (var msg in history)
        {
            messages.Add(new OllamaMessage
            {
                Role = msg.Role switch
                {
                    MessageRole.User => "user",
                    MessageRole.Assistant => "assistant",
                    MessageRole.System => "system",
                    _ => "user"
                },
                Content = msg.Content
            });
        }

        // 最后添加当前用户消息
        messages.Add(new OllamaMessage { Role = "user", Content = userMessage });
        return messages;
    }
}

#region Ollama API 数据传输对象（DTO）

/// <summary>
/// Ollama 聊天请求 - 对应 POST /api/chat 的请求体
/// </summary>
internal class OllamaChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("messages")]
    public List<OllamaMessage> Messages { get; set; } = [];

    [JsonPropertyName("stream")]
    public bool Stream { get; set; }
}

/// <summary>
/// Ollama 消息格式 - role: user/assistant/system
/// </summary>
internal class OllamaMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}

/// <summary>
/// Ollama 非流式响应 - 包含完整的 AI 回复
/// </summary>
internal class OllamaChatResponse
{
    [JsonPropertyName("message")]
    public OllamaMessage? Message { get; set; }
}

/// <summary>
/// Ollama 流式响应块 - 每个块包含一个 token 片段
/// </summary>
internal class OllamaStreamChunk
{
    [JsonPropertyName("message")]
    public OllamaMessage? Message { get; set; }

    /// <summary>是否为最后一个块</summary>
    [JsonPropertyName("done")]
    public bool Done { get; set; }
}

#endregion
