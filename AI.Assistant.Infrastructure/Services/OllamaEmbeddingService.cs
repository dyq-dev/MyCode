using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AI.Assistant.Core.Interfaces;

namespace AI.Assistant.Infrastructure.Services;

/// <summary>
/// Ollama 向量化服务实现
/// 调用本地 Ollama 的 embedding 模型：POST /api/embeddings
/// 文档：https://github.com/ollama/ollama/blob/main/docs/api.md#generate-embeddings
/// 默认模型 bge-small-zh-v1.5，输出 512 维向量。
/// </summary>
public class OllamaEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly string _model;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public OllamaEmbeddingService(HttpClient httpClient, string model = "bge-small-zh-v1.5:latest")
    {
        _httpClient = httpClient;
        _model = model;
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        var request = new OllamaEmbedRequest
        {
            Model = _model,
            Prompt = text
        };

        var response = await _httpClient.PostAsJsonAsync("api/embeddings", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaEmbedResponse>(cancellationToken: cancellationToken);
        return result?.Embedding ?? [];
    }

    public async Task<IList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        var results = new List<float[]>();
        foreach (var text in texts)
        {
            results.Add(await EmbedAsync(text, cancellationToken));
        }
        return results;
    }

    #region Ollama Embedding API DTO

    internal class OllamaEmbedRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("prompt")]
        public string Prompt { get; set; } = string.Empty;
    }

    internal class OllamaEmbedResponse
    {
        [JsonPropertyName("embedding")]
        public float[] Embedding { get; set; } = [];
    }

    #endregion
}
