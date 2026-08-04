namespace AI.Assistant.Infrastructure.Rendering;

/// <summary>
/// MarkdownView 渲染调度策略：防抖 + 看门狗。
/// - 防抖：内容稳定 400ms 后才渲染（流暂停时触发）
/// - 看门狗：连续流式超过 2s 强制渲染一次（保持实时感）
/// </summary>
public sealed class RenderTimingPolicy
{
    /// <summary>防抖间隔：内容稳定多久后渲染</summary>
    public TimeSpan DebounceInterval { get; init; } = TimeSpan.FromMilliseconds(400);

    /// <summary>看门狗间隔：连续流式最多等多久强制渲染</summary>
    public TimeSpan WatchdogInterval { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// 判断是否应该渲染。
    /// </summary>
    /// <param name="lastContentChange">内容最后一次变化的时间</param>
    /// <param name="lastRender">上一次渲染的时间</param>
    /// <param name="now">当前时间</param>
    public bool ShouldRender(DateTime lastContentChange, DateTime lastRender, DateTime now)
        => now - lastContentChange >= DebounceInterval
        || now - lastRender >= WatchdogInterval;
}
