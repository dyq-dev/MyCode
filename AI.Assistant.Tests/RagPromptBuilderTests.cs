using AI.Assistant.Core.Rag.Prompt;
using AI.Assistant.Infrastructure.Services.Rag.Prompt;

namespace AI.Assistant.Tests;

public class RagPromptBuilderTests
{
    private readonly RagPromptOptions _defaultOptions = new();
    private readonly RagPromptBuilder _builder;

    public RagPromptBuilderTests()
    {
        _builder = new RagPromptBuilder(_defaultOptions);
    }

    [Fact]
    public void Build_IncludesAllSections()
    {
        var result = _builder.Build("some code context");

        Assert.Contains(_defaultOptions.RoleDefinition, result);
        Assert.Contains(_defaultOptions.UsageRules, result);
        Assert.Contains(_defaultOptions.AntiHallucination, result);
        Assert.Contains(_defaultOptions.SourceDescription, result);
    }

    [Fact]
    public void Build_IncludesContextText()
    {
        var result = _builder.Build("class Foo { }");

        Assert.Contains("class Foo { }", result);
    }

    [Fact]
    public void Build_EmptyContext_ProducesValidPrompt()
    {
        var result = _builder.Build("");

        Assert.Contains(_defaultOptions.RoleDefinition, result);
        Assert.Contains(_defaultOptions.SourceDescription, result);
    }

    [Fact]
    public void Build_CustomOptions_RespectsOverrides()
    {
        var options = new RagPromptOptions
        {
            RoleDefinition = "Custom role",
            UsageRules = "Custom rules",
            AntiHallucination = "Custom anti-hallucination",
            SourceDescription = "Custom source:"
        };
        var builder = new RagPromptBuilder(options);

        var result = builder.Build("custom context");

        Assert.Contains("Custom role", result);
        Assert.Contains("Custom rules", result);
        Assert.Contains("Custom anti-hallucination", result);
        Assert.Contains("Custom source:", result);
        Assert.Contains("custom context", result);
    }

    [Fact]
    public void Build_ContextTextAtEnd()
    {
        var result = _builder.Build("final context lines");

        Assert.EndsWith("final context lines", result.TrimEnd());
    }

    [Fact]
    public void Build_Order_RoleThenRulesThenAntiThenSourceThenContext()
    {
        var result = _builder.Build("ctx");

        var roleIdx = result.IndexOf(_defaultOptions.RoleDefinition, StringComparison.Ordinal);
        var rulesIdx = result.IndexOf(_defaultOptions.UsageRules, StringComparison.Ordinal);
        var antiIdx = result.IndexOf(_defaultOptions.AntiHallucination, StringComparison.Ordinal);
        var sourceIdx = result.IndexOf(_defaultOptions.SourceDescription, StringComparison.Ordinal);
        var ctxIdx = result.IndexOf("ctx", StringComparison.Ordinal);

        Assert.True(roleIdx < rulesIdx);
        Assert.True(rulesIdx < antiIdx);
        Assert.True(antiIdx < sourceIdx);
        Assert.True(sourceIdx < ctxIdx);
    }
}
