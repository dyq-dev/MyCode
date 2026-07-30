using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using AI.Assistant.Core.Rag.Interfaces;
using AI.Assistant.Core.Rag.Models;

namespace AI.Assistant.Infrastructure.Services.Rag.Parsers;

public partial class MarkdownParser : IDocumentParser
{
    public SourceType SourceType => SourceType.Markdown;
    public string[] SupportedExtensions => [".md", ".markdown"];

    public async IAsyncEnumerable<KnowledgeChunk> ParseAsync(
        string filePath,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var content = await File.ReadAllTextAsync(filePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
            yield break;

        var lines = content.Split('\n');
        var headingStack = new List<(int Level, string Text)>();
        var sectionLines = new List<string>();
        var currentHeadingText = "";
        var currentHeadingLevel = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = lines[i];

            var match = HeadingRegex().Match(line);
            if (match.Success)
            {
                foreach (var chunk in FlushSection())
                    yield return chunk;

                var level = match.Groups[1].Length;
                var text = match.Groups[2].Value.Trim();

                headingStack.RemoveAll(h => h.Level >= level);
                headingStack.Add((level, text));

                currentHeadingText = text;
                currentHeadingLevel = level;
            }

            sectionLines.Add(line.TrimEnd());
        }

        foreach (var chunk in FlushSection())
            yield return chunk;

        List<KnowledgeChunk> FlushSection()
        {
            var results = new List<KnowledgeChunk>();
            var sectionContent = string.Join('\n', sectionLines);
            sectionLines.Clear();

            if (string.IsNullOrWhiteSpace(sectionContent))
                return results;

            var headingPath = string.Join(" > ",
                headingStack.Select(h => h.Text));

            var chunk = new KnowledgeChunk
            {
                Id = Guid.NewGuid().ToString("N"),
                Content = sectionContent.Trim(),
                SourceType = SourceType.Markdown,
                SourceUri = filePath,
                IndexedAt = DateTime.UtcNow
            };

            chunk.Metadata["source_path"] = filePath;

            if (!string.IsNullOrEmpty(currentHeadingText))
            {
                chunk.Metadata["heading"] = currentHeadingText;
                chunk.Metadata["heading_level"] = currentHeadingLevel.ToString();
                chunk.Metadata["heading_path"] = headingPath;
            }

            results.Add(chunk);
            return results;
        }
    }

    [GeneratedRegex(@"^(#{1,6})\s+(.+)$", RegexOptions.Multiline)]
    private static partial Regex HeadingRegex();
}
