namespace AI.Assistant.Core.Models;

/// <summary>
/// 聊天消息模型 - 表示对话中的单条消息
/// </summary>
public class ChatMessage
{
    /// <summary>消息唯一标识</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>消息内容</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>消息角色（用户/AI/系统）</summary>
    public MessageRole Role { get; set; }

    /// <summary>消息时间戳</summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;
}
