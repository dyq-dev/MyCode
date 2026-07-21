namespace AI.Assistant.Core.Models;

/// <summary>
/// 消息角色枚举 - 标识消息的发送者类型
/// </summary>
public enum MessageRole
{
    /// <summary>用户发送的消息</summary>
    User,

    /// <summary>AI 助手的回复</summary>
    Assistant,

    /// <summary>系统消息（如提示词、错误信息等）</summary>
    System
}
