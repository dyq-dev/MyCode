using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AI.Assistant.Core.Interfaces;
using AI.Assistant.Core.Models;

namespace AI.Assistant.Infrastructure.Services;

/// <summary>
/// OpenAI 兼容的聊天服务实现
/// 支持所有兼容 OpenAI Chat Completions API 的云端大模型：
/// - OpenAI (GPT-4o, GPT-4o-mini)
/// - 阿里通义千问 (Qwen)
/// - 智谱 AI (GLM-4)
/// - DeepSeek
/// - MiMo
/// 等等，只需修改 BaseUrl、ApiKey、Model 即可切换
/// </summary>
public class OpenAICompatibleChatService : IChatService
{
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly string _apiKey;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <param name="httpClient">HttpClient，BaseAddress 已配置为 API 地址</param>
    /// <param name="model">模型名称</param>
    /// <param name="apiKey">API Key</param>
    public OpenAICompatibleChatService(HttpClient httpClient, string model, string apiKey)
    {
        _httpClient = httpClient;
        _model = model;
        _apiKey = apiKey;
    }

    /// <summary>
    /// 非流式发送消息
    /// POST {baseUrl}/chat/completions
    /// </summary>
    public async Task<string> SendAsync(string message, IEnumerable<ChatMessage> history, CancellationToken cancellationToken = default)
    {
        var messages = BuildMessages(message, history);
        var request = new OpenAIChatRequest
        {
            Model = _model,
            Messages = messages,
            Stream = false
        };

        var httpRequest = CreateRequest(request);
        var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OpenAIChatResponse>(cancellationToken: cancellationToken);
        return result?.Choices?.FirstOrDefault()?.Message?.Content ?? string.Empty;
    }

    /// <summary>
    /// 流式发送消息 - 逐片段返回 AI 回复
    /// 响应格式：SSE (Server-Sent Events)
    /// data: {"choices":[{"delta":{"content":"你"}}]}
    /// data: [DONE]
    /// </summary>
    public async IAsyncEnumerable<string> StreamAsync(string message, IEnumerable<ChatMessage> history, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var messages = BuildMessages(message, history);
        var request = new OpenAIChatRequest
        {
            Model = _model,
            Messages = messages,
            Stream = true
        };

        var httpRequest = CreateRequest(request);
        using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line)) continue;

            // 跳过 SSE 前缀 "data: "
            if (line.StartsWith("data: "))
                line = line[6..];

            // 流结束标志
            if (line == "[DONE]") break;

            var chunk = ParseChunk(line);
            var content = chunk?.Choices?.FirstOrDefault()?.Delta?.Content;
            if (content != null)
            {
                yield return content;
            }
        }
    }

    /// <summary>创建带 Authorization 头的 HTTP 请求</summary>
    private HttpRequestMessage CreateRequest(object body)
    {
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        httpRequest.Headers.Add("Authorization", $"Bearer {_apiKey}");
        return httpRequest;
    }

    private static OpenAIStreamChunk? ParseChunk(string line)
    {
        try
        {
            return JsonSerializer.Deserialize<OpenAIStreamChunk>(line, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private List<OpenAIMessage> BuildMessages(string userMessage, IEnumerable<ChatMessage> history)
    {
        var messages = new List<OpenAIMessage>();

        foreach (var msg in history)
        {
            messages.Add(new OpenAIMessage
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

        messages.Add(new OpenAIMessage { Role = "user", Content = userMessage });
        return messages;
    }
}

#region OpenAI API 数据传输对象

internal class OpenAIChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("messages")]
    public List<OpenAIMessage> Messages { get; set; } = [];

    [JsonPropertyName("stream")]
    public bool Stream { get; set; }
}

internal class OpenAIMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}

internal class OpenAIChatResponse
{
    [JsonPropertyName("choices")]
    public List<OpenAIChoice>? Choices { get; set; }
}

internal class OpenAIChoice
{
    [JsonPropertyName("message")]
    public OpenAIMessage? Message { get; set; }
}

internal class OpenAIStreamChunk
{
    [JsonPropertyName("choices")]
    public List<OpenAIStreamChoice>? Choices { get; set; }
}

internal class OpenAIStreamChoice
{
    [JsonPropertyName("delta")]
    public OpenAIMessage? Delta { get; set; }
}

#endregion
