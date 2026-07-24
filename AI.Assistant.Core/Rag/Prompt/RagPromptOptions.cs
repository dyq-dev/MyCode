namespace AI.Assistant.Core.Rag.Prompt;

public class RagPromptOptions
{
    public string RoleDefinition { get; set; } =
        "你是一个资深软件工程师，精通 C#、.NET、WPF 和现代软件开发实践。";

    public string UsageRules { get; set; } =
        "回答问题时必须优先使用以下代码上下文中的信息。上下文中的代码库结构、接口定义、实现细节等是回答的主要依据。";

    public string AntiHallucination { get; set; } =
        "如果代码上下文中没有足够的信息来回答问题，必须明确告知用户你不知道，不得编造代码、接口、方法或类名。";

    public string SourceDescription { get; set; } =
        "以下是从当前项目代码仓库中检索到的相关代码上下文：";
}
