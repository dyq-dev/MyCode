using AI.Assistant.Core.Interfaces;
using AI.Assistant.Core.Rag.Context;
using AI.Assistant.Core.Rag.Prompt;
using AI.Assistant.Infrastructure.Services.Chat;
using AI.Assistant.Infrastructure.Services.Rag.Context;
using Microsoft.Extensions.DependencyInjection;

namespace AI.Assistant.Infrastructure.Extensions;

public static class RagChatServiceCollectionExtensions
{
    public static IServiceCollection AddRagChatIntegration(this IServiceCollection services)
    {
        services.AddSingleton<IRagQueryService, RagQueryService>();
        services.AddSingleton<IChatService>(sp =>
        {
            var baseService = sp.GetRequiredService<BaseChatService>().Instance;
            var ragQuery = sp.GetRequiredService<IRagQueryService>();
            var promptBuilder = sp.GetRequiredService<IRagPromptBuilder>();
            return new RagChatService(baseService, ragQuery, promptBuilder);
        });
        return services;
    }
}
