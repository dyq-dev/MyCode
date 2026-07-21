using AI.Assistant.Core.Rag.Interfaces;
using AI.Assistant.Core.Rag.Options;
using AI.Assistant.Infrastructure.Services.Rag.Chunking;
using AI.Assistant.Infrastructure.Services.Rag.Chunking.Strategies;
using AI.Assistant.Infrastructure.Services.Rag.Scanner;
using Microsoft.Extensions.DependencyInjection;

namespace AI.Assistant.Infrastructure.Extensions;

public static class RagServiceCollectionExtensions
{
    public static IServiceCollection AddRag(this IServiceCollection services, Action<RagOptions>? configure = null)
    {
        var options = new RagOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);

        return services
            .AddRagScanner()
            .AddRagChunking();
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
}
