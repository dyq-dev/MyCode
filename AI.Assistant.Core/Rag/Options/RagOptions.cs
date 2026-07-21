namespace AI.Assistant.Core.Rag.Options;

public class RagOptions
{
    public string QdrantCollectionName { get; set; } = "code_rag";
    public int MaxChunkSize { get; set; } = 512;
    public int ChunkOverlap { get; set; } = 50;
    public string[] SupportedExtensions { get; set; } =
        [".cs", ".xaml", ".json", ".md", ".xml"];

    public string[] IgnoreFolders { get; set; } =
        ["bin", "obj", ".git", ".vs", "node_modules", ".mimocode", ".cache"];

    public string[] IgnoreExtensions { get; set; } =
        [".exe", ".dll", ".pdb", ".cache", ".suo", ".user"];
}
