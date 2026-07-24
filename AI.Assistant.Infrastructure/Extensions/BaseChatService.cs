using AI.Assistant.Core.Interfaces;

namespace AI.Assistant.Infrastructure.Extensions;

/// <summary>
/// 持有基础 ChatService 实例的显式容器。
/// AddInfrastructure 注册 BaseChatService（持有 Ollama/OpenAI 实例），
/// AddRagChatIntegration 通过它构造 RagChatService 装饰器。
/// 避免直接操作 ServiceDescriptor 的循环依赖问题。
/// </summary>
internal sealed class BaseChatService
{
    public IChatService Instance { get; }

    public BaseChatService(IChatService instance)
    {
        Instance = instance;
    }
}
