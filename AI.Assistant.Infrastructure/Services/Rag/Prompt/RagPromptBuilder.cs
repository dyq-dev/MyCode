using AI.Assistant.Core.Rag.Prompt;

namespace AI.Assistant.Infrastructure.Services.Rag.Prompt;

public sealed class RagPromptBuilder : IRagPromptBuilder
{
    private readonly RagPromptOptions _options;

    public RagPromptBuilder(RagPromptOptions options)
    {
        _options = options;
    }

    public string Build(string contextText)
    {
        return $@"{_options.RoleDefinition}

{_options.UsageRules}

{_options.AntiHallucination}

{_options.SourceDescription}

{contextText}";
    }
}
