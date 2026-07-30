using System.Runtime.CompilerServices;
using AI.Assistant.Core.Rag.Interfaces;
using AI.Assistant.Core.Rag.Models;
using UglyToad.PdfPig;

namespace AI.Assistant.Infrastructure.Services.Rag.Parsers;

public class PdfParser : IDocumentParser
{
    public SourceType SourceType => SourceType.Pdf;
    public string[] SupportedExtensions => [".pdf"];

    public async IAsyncEnumerable<KnowledgeChunk> ParseAsync(
        string filePath,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var pageTexts = await Task.Run(() =>
        {
            try
            {
                using var pdf = PdfDocument.Open(filePath);
                return pdf.GetPages().Select((p, i) => (Index: i + 1, Text: p.Text)).ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PdfParser] 打开 PDF 失败: {ex.Message}");
                return new List<(int Index, string Text)>();
            }
        }, cancellationToken);

        foreach (var page in pageTexts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var text = SanitizeText(page.Text);
            if (string.IsNullOrWhiteSpace(text))
                continue;

            yield return new KnowledgeChunk
            {
                Id = Guid.NewGuid().ToString("N"),
                Content = text,
                SourceType = SourceType.Pdf,
                SourceUri = filePath,
                ProjectPath = filePath,
                IndexedAt = DateTime.UtcNow
            };
        }
    }

    private static string SanitizeText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var sb = new System.Text.StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c == '\0' || c == '\ufffd' || c == '\ufffe' || c == '\uffff')
                continue;
            if (c < 0x20 && c != '\t' && c != '\n' && c != '\r')
                continue;
            sb.Append(c);
        }
        return sb.ToString().Trim();
    }
}
