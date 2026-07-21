using AI.Assistant.Core.Interfaces;
using AI.Assistant.Core.Models;
using AI.Assistant.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Qdrant.Client;

namespace AI.Assistant.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, Action<InfrastructureOptions>? configure = null)
    {
        var options = new InfrastructureOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);

        // ============ 聊天服务 ============
        bool chatCloud = options.ChatProvider == "Cloud";
        services.AddHttpClient("chat", client =>
        {
            client.BaseAddress = new Uri(chatCloud ? options.ChatCloudBaseUrl : options.OllamaBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(120);
        });
        services.AddSingleton<IChatService>(sp =>
        {
            var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient("chat");
            return chatCloud
                ? new OpenAICompatibleChatService(client, options.ChatCloudModel, options.ChatCloudApiKey)
                : new OllamaChatService(client, options.OllamaChatModel);
        });

        // ============ 向量化服务 ============
        bool embedCloud = options.EmbeddingProvider == "Cloud";
        services.AddHttpClient("embedding", client =>
        {
            client.BaseAddress = new Uri(embedCloud ? options.EmbeddingCloudBaseUrl : options.OllamaBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddSingleton<IEmbeddingService>(sp =>
        {
            var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient("embedding");
            return embedCloud
                ? new OpenAICompatibleEmbeddingService(client, options.EmbeddingCloudModel, options.EmbeddingCloudApiKey)
                : new OllamaEmbeddingService(client, options.OllamaEmbeddingModel);
        });

        // ============ Qdrant 向量数据库（gRPC 端口 6334） ============
        services.AddSingleton<QdrantClient>(_ => new QdrantClient("localhost", 6334));
        services.AddSingleton<IVectorStore>(sp =>
        {
            var qdrantClient = sp.GetRequiredService<QdrantClient>();
            return new QdrantVectorStore(qdrantClient);
        });

        // ============ mssql 长期记忆仓储 ============
        services.AddSingleton<MemoryRepository>(_ => new MemoryRepository(options.SqlConnectionString));

        // ============ 长期记忆过滤 ============
        services.AddSingleton<IMemoryFilter, MemoryFilter>();

        // ============ 知识库服务（预留） ============
        services.AddSingleton<IKnowledgeService>(_ => new StubKnowledgeService());

        // ============ 长期记忆协调服务 ============
        services.AddSingleton<MemoryService>(sp =>
        {
            var embedding = sp.GetRequiredService<IEmbeddingService>();
            var vectorStore = sp.GetRequiredService<IVectorStore>();
            var repository = sp.GetRequiredService<MemoryRepository>();
            var chatService = sp.GetRequiredService<IChatService>();
            var filter = sp.GetRequiredService<IMemoryFilter>();
            return new MemoryService(embedding, vectorStore, repository, chatService, filter, options.QdrantCollection);
        });

        return services;
    }
}

/// <summary>
/// 配置选项 - Chat 和 Embedding 完全独立（厂商、Key、地址、模型都可以不同）
/// </summary>
public class InfrastructureOptions
{
    // Provider 选择：Ollama 或 Cloud
    public string ChatProvider { get; set; } = "Ollama";
    public string EmbeddingProvider { get; set; } = "Ollama";

    // Ollama 本地配置
    public string OllamaBaseUrl { get; set; } = "http://localhost:11434";
    public string OllamaChatModel { get; set; } = "gemma3:1b";
    public string OllamaEmbeddingModel { get; set; } = "qllama/bge-small-zh-v1.5:latest";

    // Chat 云端配置（可独立于 Embedding）
    public string ChatCloudBaseUrl { get; set; } = string.Empty;
    public string ChatCloudApiKey { get; set; } = string.Empty;
    public string ChatCloudModel { get; set; } = string.Empty;

    // Embedding 云端配置（可独立于 Chat）
    public string EmbeddingCloudBaseUrl { get; set; } = string.Empty;
    public string EmbeddingCloudApiKey { get; set; } = string.Empty;
    public string EmbeddingCloudModel { get; set; } = string.Empty;

    // Qdrant
    public string QdrantBaseUrl { get; set; } = "http://localhost:6333";

    // 长期记忆在 mssql 中的集合（Qdrant 中的 collection 名）
    public string QdrantCollection { get; set; } = "memories";

    // mssql 连接串（Windows 认证示例，可在 appsettings 覆盖）
    public string SqlConnectionString { get; set; } =
        "Server=localhost;Database=AIAssistant;Trusted_Connection=True;TrustServerCertificate=True;";
}

// 知识库预留实现，始终返回空结果
internal class StubKnowledgeService : IKnowledgeService
{
    public Task<KnowledgeContext> RetrieveAsync(string query, CancellationToken cancellationToken = default)
        => Task.FromResult(new KnowledgeContext());
}
