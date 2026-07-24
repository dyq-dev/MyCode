namespace AI.Assistant.Core.Rag.Prompt;

public interface IRagPromptBuilder
{
    string Build(string contextText);
}
