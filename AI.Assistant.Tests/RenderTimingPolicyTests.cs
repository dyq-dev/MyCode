using AI.Assistant.Infrastructure.Rendering;

namespace AI.Assistant.Tests;

public class RenderTimingPolicyTests
{
    private readonly RenderTimingPolicy _policy = new();

    [Fact]
    public void ShouldRender_DebounceNotMet_ReturnsFalse()
    {
        var lastChange = DateTime.UtcNow;
        var lastRender = DateTime.UtcNow;
        var now = lastChange.AddMilliseconds(200);

        Assert.False(_policy.ShouldRender(lastChange, lastRender, now));
    }

    [Fact]
    public void ShouldRender_DebounceMet_ReturnsTrue()
    {
        var lastChange = DateTime.UtcNow;
        var lastRender = DateTime.UtcNow;
        var now = lastChange.AddMilliseconds(450);

        Assert.True(_policy.ShouldRender(lastChange, lastRender, now));
    }

    [Fact]
    public void ShouldRender_WatchdogMet_ReturnsTrue()
    {
        var lastChange = DateTime.UtcNow;
        var lastRender = DateTime.UtcNow;
        var now = lastRender.AddSeconds(2.1);

        Assert.True(_policy.ShouldRender(lastChange, lastRender, now));
    }

    [Fact]
    public void ShouldRender_WatchdogNotMet_ReturnsFalse()
    {
        var now = DateTime.UtcNow;
        var lastChange = now;                    // 刚刚变化，防抖未满足（< 400ms）
        var lastRender = now.AddSeconds(-1.5);   // 1.5s 前渲染，看门狗未满足（< 2s）

        Assert.False(_policy.ShouldRender(lastChange, lastRender, now));
    }

    [Fact]
    public void ShouldRender_BothMet_ReturnsTrue()
    {
        var lastChange = DateTime.UtcNow;
        var lastRender = DateTime.UtcNow;
        var now = lastChange.AddMilliseconds(500);

        Assert.True(_policy.ShouldRender(lastChange, lastRender, now));
    }

    [Fact]
    public void ShouldRender_FirstRender_ReturnsTrue()
    {
        var lastChange = DateTime.UtcNow;
        var lastRender = DateTime.MinValue;
        var now = lastChange.AddMilliseconds(100);

        Assert.True(_policy.ShouldRender(lastChange, lastRender, now));
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(399, 0, false)]
    [InlineData(400, 0, true)]
    [InlineData(1000, 0, true)]
    [InlineData(0, 1999, false)]
    [InlineData(0, 2000, true)]
    [InlineData(0, 5000, true)]
    [InlineData(500, 500, true)]
    public void ShouldRender_VariousCombinations(int debounceMs, int watchdogMs, bool expected)
    {
        var now = DateTime.UtcNow;
        var lastChange = now.AddMilliseconds(-debounceMs);
        var lastRender = now.AddMilliseconds(-watchdogMs);

        Assert.Equal(expected, _policy.ShouldRender(lastChange, lastRender, now));
    }
}
