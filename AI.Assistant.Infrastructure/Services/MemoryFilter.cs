using AI.Assistant.Core.Interfaces;
using AI.Assistant.Core.Models;

namespace AI.Assistant.Infrastructure.Services;

public class MemoryFilter : IMemoryFilter
{
    private static readonly HashSet<string> SkipPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "你好", "您好", "hi", "hello", "嗨", "hey",
        "谢谢", "感谢", "thanks", "thank you",
        "再见", "拜拜", "bye", "goodbye",
        "嗯", "哦", "好的", "知道了", "明白", "可以"
    };

    public MemoryFilterResult ShouldStore(string content, string role)
    {
        if (role != "user")
            return new MemoryFilterResult(false, "只存储用户消息");

        if (string.IsNullOrWhiteSpace(content) || content.Length < 5)
            return new MemoryFilterResult(false, "消息太短");

        var trimmed = content.Trim();
        foreach (var prefix in SkipPrefixes)
        {
            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return new MemoryFilterResult(false, "打招呼/感谢/无意义内容");
        }

        return new MemoryFilterResult(true, null);
    }
}
