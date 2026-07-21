using System.Security.Cryptography;
using System.Text;
using AI.Assistant.Core.Rag.Options;
using AI.Assistant.Infrastructure.Services.Rag.Scanner;

namespace AI.Assistant.Tests;

public class ProjectScannerTests : IDisposable
{
    private readonly string _tempDir;

    public ProjectScannerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "RagTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private RagOptions DefaultOptions() => new();

    private ProjectScanner CreateScanner(RagOptions? options = null) => new(options ?? DefaultOptions());

    private string CreateFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_tempDir, relativePath);
        var dir = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    // ============ 基本扫描 ============

    [Fact]
    public async Task ScanProjectAsync_EmptyDirectory_ReturnsEmptyList()
    {
        var scanner = CreateScanner();
        var result = await scanner.ScanProjectAsync(_tempDir);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ScanProjectAsync_WithSupportedFiles_ReturnsCodeFiles()
    {
        CreateFile("test.cs", "class Foo {}");
        CreateFile("readme.md", "# Hello");
        CreateFile("config.json", "{}");

        var scanner = CreateScanner();
        var result = await scanner.ScanProjectAsync(_tempDir);

        Assert.Equal(3, result.Count);
    }

    // ============ 扩展名过滤 ============

    [Fact]
    public async Task ScanProjectAsync_IgnoresUnsupportedExtensions()
    {
        CreateFile("test.cs", "code");
        CreateFile("test.png", "binary");

        var scanner = CreateScanner();
        var result = await scanner.ScanProjectAsync(_tempDir);

        Assert.Single(result);
        Assert.All(result, f => Assert.EndsWith(".cs", f.FilePath));
    }

    [Fact]
    public async Task ScanProjectAsync_IgnoresIgnoredExtensions()
    {
        var options = DefaultOptions();
        options.SupportedExtensions = [".cs", ".dll"];
        CreateFile("test.cs", "code");
        CreateFile("test.dll", "pe");

        var scanner = CreateScanner(options);
        var result = await scanner.ScanProjectAsync(_tempDir);

        Assert.Single(result);
        Assert.Contains(".cs", result[0].FilePath);
    }

    // ============ 目录过滤 ============

    [Fact]
    public async Task ScanProjectAsync_IgnoresBinFolder()
    {
        CreateFile("src/my.cs", "code");
        CreateFile("bin/output.exe", "pe");

        var scanner = CreateScanner();
        var result = await scanner.ScanProjectAsync(_tempDir);

        Assert.Single(result);
        Assert.DoesNotContain(result, f => f.FilePath.Contains("bin"));
    }

    [Fact]
    public async Task ScanProjectAsync_IgnoresNodeModules()
    {
        var options = DefaultOptions();
        options.SupportedExtensions = [".js"];

        CreateFile("src/app.js", "var x = 1;");
        CreateFile("node_modules/pkg/index.js", "module.exports = {};");

        var scanner = CreateScanner(options);
        var result = await scanner.ScanProjectAsync(_tempDir);

        Assert.Single(result);
        Assert.DoesNotContain(result, f => f.FilePath.Contains("node_modules"));
    }

    [Fact]
    public async Task ScanProjectAsync_IgnoresNestedIgnoredFolders()
    {
        CreateFile("src/lib/my.cs", "code");
        CreateFile("src/bin/tool.cs", "code");     // bin 在任何层级都应忽略

        var scanner = CreateScanner();
        var result = await scanner.ScanProjectAsync(_tempDir);

        Assert.Single(result);
        Assert.DoesNotContain(result, f => f.FilePath.Contains("bin"));
    }

    // ============ Hash 计算 ============

    [Fact]
    public async Task ScanProjectAsync_ComputesCorrectSha256()
    {
        var content = "class Foo { }";
        CreateFile("test.cs", content);

        var expectedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

        var scanner = CreateScanner();
        var result = await scanner.ScanProjectAsync(_tempDir);

        Assert.Single(result);
        Assert.Equal(expectedHash, result[0].FileHash);
    }

    [Fact]
    public async Task ScanProjectAsync_DifferentContent_DifferentHashes()
    {
        CreateFile("a.cs", "content a");
        CreateFile("b.cs", "content b");

        var scanner = CreateScanner();
        var result = await scanner.ScanProjectAsync(_tempDir);

        Assert.Equal(2, result.Count);
        Assert.NotEqual(result[0].FileHash, result[1].FileHash);
    }

    // ============ 编码检测 ============

    [Fact]
    public async Task ScanProjectAsync_ReadsUtf8Content()
    {
        var content = "public class 测试 { }";
        CreateFile("test.cs", content);

        var scanner = CreateScanner();
        var result = await scanner.ScanProjectAsync(_tempDir);

        Assert.Single(result);
        Assert.Equal(content, result[0].Content);
        Assert.Equal("utf-8", result[0].Encoding);
    }

    [Fact]
    public async Task ScanProjectAsync_ReadsUtf8BomContent()
    {
        var content = "class Foo { }";
        var filePath = Path.Combine(_tempDir, "test.cs");
        File.WriteAllBytes(filePath, [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(content)]);

        var scanner = CreateScanner();
        var result = await scanner.ScanProjectAsync(_tempDir);

        Assert.Single(result);
        Assert.Equal(content, result[0].Content);
        Assert.Equal("utf-8-bom", result[0].Encoding);
    }

    // ============ 语言检测 ============

    [Fact]
    public async Task ScanProjectAsync_DetectsLanguageByExtension()
    {
        CreateFile("test.cs", "class F {}");
        CreateFile("readme.md", "# Title");
        CreateFile("app.json", "{}");

        var scanner = CreateScanner();
        var result = await scanner.ScanProjectAsync(_tempDir);

        var byPath = result.ToDictionary(f => f.FilePath, f => f.Language);
        Assert.Equal("csharp", byPath["test.cs"]);
        Assert.Equal("markdown", byPath["readme.md"]);
        Assert.Equal("json", byPath["app.json"]);
    }

    [Fact]
    public async Task ScanProjectAsync_UnknownExtension_UsesTextLanguage()
    {
        var options = DefaultOptions();
        options.SupportedExtensions = [".myext"];
        CreateFile("file.myext", "some data");

        var scanner = CreateScanner(options);
        var result = await scanner.ScanProjectAsync(_tempDir);

        Assert.Single(result);
        Assert.Equal("text", result[0].Language);
    }

    // ============ 边界场景 ============

    [Fact]
    public async Task ScanProjectAsync_NonExistentPath_ReturnsEmptyList()
    {
        var badPath = Path.Combine(_tempDir, "does_not_exist");
        var scanner = CreateScanner();

        var result = await scanner.ScanProjectAsync(badPath);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ScanProjectAsync_EmptyFile_ReturnsEmptyContent()
    {
        CreateFile("empty.cs", "");

        var scanner = CreateScanner();
        var result = await scanner.ScanProjectAsync(_tempDir);

        Assert.Single(result);
        Assert.Empty(result[0].Content);
        Assert.NotEmpty(result[0].FileHash); // SHA256 of empty byte[]
    }

    [Fact]
    public async Task ScanProjectAsync_CancellationToken_StopsScanning()
    {
        CreateFile("a.cs", "a");
        CreateFile("b.cs", "b");
        CreateFile("c.cs", "c");

        var cts = new CancellationTokenSource();
        cts.Cancel(); // 立即取消

        var scanner = CreateScanner();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            scanner.ScanProjectAsync(_tempDir, cts.Token));
    }

    [Fact]
    public async Task ScanProjectAsync_RespectsCustomOptions()
    {
        var options = new RagOptions
        {
            SupportedExtensions = [".cs", ".ts"],
            IgnoreFolders = ["ignored"],
            IgnoreExtensions = [".txt"]
        };

        CreateFile("good.cs", "a");
        CreateFile("good.ts", "b");
        CreateFile("ignored/bad.cs", "c");  // 忽略目录
        CreateFile("notes.txt", "d");       // 忽略扩展名
        CreateFile("bad.exe", "e");         // 不支持扩展名

        var scanner = CreateScanner(options);
        var result = await scanner.ScanProjectAsync(_tempDir);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, f => f.FilePath == "good.cs");
        Assert.Contains(result, f => f.FilePath == "good.ts");
    }

    // ============ 相对路径 ============

    [Fact]
    public async Task ScanProjectAsync_ReturnsRelativePaths()
    {
        CreateFile("src/lib/utils.cs", "class Utils {}");
        CreateFile("src/services/api.cs", "class Api {}");

        var separator = Path.DirectorySeparatorChar;
        var scanner = CreateScanner();
        var result = await scanner.ScanProjectAsync(_tempDir);

        Assert.Contains(result, f => f.FilePath == $"src{separator}lib{separator}utils.cs");
        Assert.Contains(result, f => f.FilePath == $"src{separator}services{separator}api.cs");
    }
}
