using AI.Assistant.Infrastructure.Rendering;

namespace AI.Assistant.Tests;

public class RenderTimingPolicyTests
{
    private readonly RenderTimingPolicy _policy = new();

    [Fact]
    public void ShouldRender_IntervalNotMet_ReturnsFalse()
    {
        var lastRender = DateTime.UtcNow;
        var now = lastRender.AddMilliseconds(300); // < 500ms

        Assert.False(_policy.ShouldRender(lastRender, now));
    }

    [Fact]
    public void ShouldRender_IntervalMet_ReturnsTrue()
    {
        var lastRender = DateTime.UtcNow;
        var now = lastRender.AddMilliseconds(550); // >= 500ms

        Assert.True(_policy.ShouldRender(lastRender, now));
    }

    [Fact]
    public void ShouldRender_ExactInterval_ReturnsTrue()
    {
        var lastRender = DateTime.UtcNow;
        var now = lastRender.AddMilliseconds(500);

        Assert.True(_policy.ShouldRender(lastRender, now));
    }

    [Fact]
    public void ShouldRender_FirstRender_ReturnsTrue()
    {
        var lastRender = DateTime.MinValue;
        var now = DateTime.UtcNow;

        Assert.True(_policy.ShouldRender(lastRender, now));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(100, false)]
    [InlineData(499, false)]
    [InlineData(500, true)]
    [InlineData(1000, true)]
    public void ShouldRender_VariousIntervals(int ms, bool expected)
    {
        var now = DateTime.UtcNow;
        var lastRender = now.AddMilliseconds(-ms);

        Assert.Equal(expected, _policy.ShouldRender(lastRender, now));
    }
}
