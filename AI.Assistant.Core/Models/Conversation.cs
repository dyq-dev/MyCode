namespace AI.Assistant.Core.Models;

/// <summary>
/// 会话模型 - 表示一次完整的对话，包含多条消息
/// </summary>
public class Conversation
{
    /// <summary>会话唯一标识</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>会话标题（默认"新对话"）</summary>
    public string Title { get; set; } = "新对话";

    /// <summary>创建时间</summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>最后更新时间</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    /// <summary>会话中的所有消息</summary>
    public List<ChatMessage> Messages { get; set; } = [];
}
