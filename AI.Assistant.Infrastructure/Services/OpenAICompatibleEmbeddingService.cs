using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AI.Assistant.Core.Interfaces;

namespace AI.Assistant.Infrastructure.Services;

/// <summary>
/// OpenAI 兼容的向量化服务实现
/// 支持所有兼容 OpenAI Embeddings API 的云端服务：
/// - OpenAI (text-embedding-3-small)
/// - 阿里通义千问 (text-embedding-v3)
/// - 智谱 AI (embedding-3)
/// POST {baseUrl}/embeddings
/// </summary>
public class OpenAICompatibleEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly string _apiKey;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public OpenAICompatibleEmbeddingService(HttpClient httpClient, string model, string apiKey)
    {
        _httpClient = httpClient;
        _model = model;
        _apiKey = apiKey;
    }

    /// <summary>
    /// 将单条文本转换为向量
    /// </summary>
    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        var result = await EmbedInternalAsync(text, cancellationToken);
        return result?.FirstOrDefault() ?? [];
    }

    /// <summary>
    /// 批量将文本转换为向量
    /// </summary>
    public async Task<IList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        var results = new List<float[]>();
        foreach (var text in texts)
        {
            var vector = await EmbedInternalAsync(text, cancellationToken);
            results.Add(vector?.FirstOrDefault() ?? []);
        }
        return results;
    }

    private async Task<IList<float[]>?> EmbedInternalAsync(string input, CancellationToken cancellationToken)
    {
        var request = new OpenAIEmbeddingRequest
        {
            Model = _model,
            Input = input
        };

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "embeddings")
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        httpRequest.Headers.Add("Authorization", $"Bearer {_apiKey}");

        var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OpenAIEmbeddingResponse>(cancellationToken: cancellationToken);
        return result?.Data?.Select(d => d.Embedding).ToList();
    }
}

#region OpenAI Embedding API DTO

internal class OpenAIEmbeddingRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("input")]
    public string Input { get; set; } = string.Empty;
}

internal class OpenAIEmbeddingResponse
{
    [JsonPropertyName("data")]
    public List<OpenAIEmbeddingData>? Data { get; set; }
}

internal class OpenAIEmbeddingData
{
    [JsonPropertyName("embedding")]
    public float[] Embedding { get; set; } = [];
}

#endregion
