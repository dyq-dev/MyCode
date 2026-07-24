using AI.Assistant.Core.Rag.Context;
using AI.Assistant.Core.Rag.Interfaces;
using AI.Assistant.Core.Rag.Options;
using AI.Assistant.Core.Rag.Prompt;
using AI.Assistant.Infrastructure.Services.Rag.Chunking;
using AI.Assistant.Infrastructure.Services.Rag.Chunking.Strategies;
using AI.Assistant.Infrastructure.Services.Rag.Context;
using AI.Assistant.Infrastructure.Services.Rag.Indexing;
using AI.Assistant.Infrastructure.Services.Rag.Prompt;
using AI.Assistant.Infrastructure.Services.Rag.Retrieval;
using AI.Assistant.Infrastructure.Services.Rag.Scanner;
using AI.Assistant.Infrastructure.Services.Rag.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AI.Assistant.Infrastructure.Extensions;

public static class RagServiceCollectionExtensions
{
    public static IServiceCollection AddRag(this IServiceCollection services, Action<RagOptions>? configure = null)
    {
        var options = new RagOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        services.AddSingleton<IOptions<RagOptions>>(Options.Create(options));

        return services
            .AddRagScanner()
            .AddRagChunking()
            .AddRagStorage()
            .AddRagIndexing()
            .AddRagRetrieval()
            .AddRagContext()
            .AddRagPrompt();
    }

    public static IServiceCollection AddRagScanner(this IServiceCollection services)
    {
        services.AddSingleton<IProjectScanner, ProjectScanner>();
        services.AddSingleton<IIndexComparer, IndexComparer>();
        return services;
    }

    public static IServiceCollection AddRagChunking(this IServiceCollection services)
    {
        services.AddSingleton<IChunkManager, ChunkManager>();
        services.AddSingleton<IChunkStrategy, WholeFileChunkStrategy>();
        return services;
    }

    public static IServiceCollection AddRagStorage(this IServiceCollection services)
    {
        services.AddSingleton<IQdrantIndexStorage, QdrantIndexStorage>();
        services.AddSingleton<ICodeIndexStore, CodeIndexStore>();
        return services;
    }

    public static IServiceCollection AddRagIndexing(this IServiceCollection services)
    {
        services.AddSingleton<ICodeIndexer, CodeIndexer>();
        return services;
    }

    public static IServiceCollection AddRagRetrieval(this IServiceCollection services)
    {
        services.AddSingleton<ICodeQueryStore, CodeQueryStore>();
        services.AddSingleton<ICodeRetriever, CodeRetriever>();
        return services;
    }

    public static IServiceCollection AddRagContext(this IServiceCollection services, Action<RagContextOptions>? configure = null)
    {
        var options = new RagContextOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        services.AddSingleton<IRagContextBuilder, RagContextBuilder>();
        return services;
    }

    public static IServiceCollection AddRagPrompt(this IServiceCollection services, Action<RagPromptOptions>? configure = null)
    {
        var options = new RagPromptOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        services.AddSingleton<IRagPromptBuilder, RagPromptBuilder>();
        return services;
    }
}
