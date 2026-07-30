using AI.Assistant.Core.Rag.Models;
using AI.Assistant.Infrastructure.Services.Rag.Parsers;

namespace AI.Assistant.Tests;

public class DocumentParserTests
{
    // ============ TextParser ============

    [Fact]
    public async Task TextParser_ReadsTextFile()
    {
        var path = Path.GetTempFileName() + ".txt";
        try
        {
            await File.WriteAllTextAsync(path, "hello world");
            var parser = new TextParser();

            var chunks = await parser.ParseAsync(path).ToListAsync();

            Assert.Single(chunks);
            Assert.Equal("hello world", chunks[0].Content);
            Assert.Equal(SourceType.Text, chunks[0].SourceType);
            Assert.Equal(path, chunks[0].SourceUri);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task TextParser_EmptyFile_ReturnsNoChunks()
    {
        var path = Path.GetTempFileName() + ".txt";
        try
        {
            await File.WriteAllTextAsync(path, "");
            var parser = new TextParser();

            var chunks = await parser.ParseAsync(path).ToListAsync();

            Assert.Empty(chunks);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task TextParser_WhitespaceOnly_ReturnsNoChunks()
    {
        var path = Path.GetTempFileName() + ".txt";
        try
        {
            await File.WriteAllTextAsync(path, "   \n  \t  ");
            var parser = new TextParser();

            var chunks = await parser.ParseAsync(path).ToListAsync();

            Assert.Empty(chunks);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void TextParser_SupportedExtensions()
    {
        var parser = new TextParser();

        Assert.Equal([".txt"], parser.SupportedExtensions);
        Assert.Equal(SourceType.Text, parser.SourceType);
    }

    // ============ PdfParser ============

    [Fact]
    public async Task PdfParser_ReadsTextFromPdf()
    {
        var path = Path.GetTempFileName() + ".pdf";
        try
        {
            CreateMinimalPdf(path, "Hello from PDF");
            var parser = new PdfParser();

            var chunks = await parser.ParseAsync(path).ToListAsync();

            Assert.Single(chunks);
            Assert.Contains("Hello from PDF", chunks[0].Content);
            Assert.Equal(SourceType.Pdf, chunks[0].SourceType);
            Assert.Equal(path, chunks[0].SourceUri);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task PdfParser_EmptyPdf_ReturnsNoChunks()
    {
        var path = Path.GetTempFileName() + ".pdf";
        try
        {
            CreateMinimalPdf(path, "");
            var parser = new PdfParser();

            var chunks = await parser.ParseAsync(path).ToListAsync();

            Assert.Empty(chunks);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void PdfParser_SupportedExtensions()
    {
        var parser = new PdfParser();

        Assert.Equal([".pdf"], parser.SupportedExtensions);
        Assert.Equal(SourceType.Pdf, parser.SourceType);
    }

    private static void CreateMinimalPdf(string path, string text)
    {
        var escaped = text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        var content = $@"%PDF-1.4
1 0 obj
<< /Type /Catalog /Pages 2 0 R >>
endobj
2 0 obj
<< /Type /Pages /Kids [3 0 R] /Count 1 >>
endobj
3 0 obj
<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>
endobj
4 0 obj
<< /Length 44 >>
stream
BT /F1 12 Tf 100 700 Td ({escaped}) Tj ET
endstream
endobj
5 0 obj
<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>
endobj
xref
0 6
0000000000 65535 f 
0000000009 00000 n 
0000000058 00000 n 
0000000115 00000 n 
0000000266 00000 n 
0000000360 00000 n 
trailer
<< /Size 6 /Root 1 0 R >>
startxref
424
%%EOF";
        File.WriteAllText(path, content);
    }
}
