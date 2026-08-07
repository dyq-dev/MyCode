namespace AI.Assistant.Infrastructure.Rendering;

/// <summary>
/// MarkdownView 渲染调度策略：简单节流。
/// 流式期间每 500ms 渲染一次，确保 Markdown 格式逐步出现，不会突兀。
/// </summary>
public sealed class RenderTimingPolicy
{
    /// <summary>节流间隔</summary>
    public TimeSpan ThrottleInterval { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// 判断是否应该渲染。
    /// </summary>
    /// <param name="lastRender">上一次渲染的时间</param>
    /// <param name="now">当前时间</param>
    public bool ShouldRender(DateTime lastRender, DateTime now)
        => now - lastRender >= ThrottleInterval;
}
