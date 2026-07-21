using AI.Assistant.Core.Models;

namespace AI.Assistant.Core.Interfaces;

/// <summary>
/// 聊天服务接口 - 定义与 AI 模型交互的标准
/// 实现类：OllamaChatService（调用 Ollama API）
/// </summary>
public interface IChatService
{
    /// <summary>
    /// 发送消息并获取完整回复（非流式）
    /// </summary>
    /// <param name="message">用户输入的消息</param>
    /// <param name="history">历史对话记录，用于上下文理解</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>AI 的完整回复文本</returns>
    Task<string> SendAsync(string message, IEnumerable<ChatMessage> history, CancellationToken cancellationToken = default);

    /// <summary>
    /// 发送消息并流式获取回复（逐字输出，体验更好）
    /// </summary>
    /// <param name="message">用户输入的消息</param>
    /// <param name="history">历史对话记录</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>AI 回复的文本片段流</returns>
    IAsyncEnumerable<string> StreamAsync(string message, IEnumerable<ChatMessage> history, CancellationToken cancellationToken = default);
}
