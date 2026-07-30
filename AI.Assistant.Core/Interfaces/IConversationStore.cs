using AI.Assistant.Core.Models;

namespace AI.Assistant.Core.Interfaces;

public interface IConversationStore
{
    Task<List<Conversation>> LoadIndexAsync();
    Task<Conversation?> LoadConversationAsync(string id);
    Task SaveConversationAsync(Conversation conversation);
    Task DeleteConversationAsync(string id);
}
