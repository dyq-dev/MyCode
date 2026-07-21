using System.Security.Cryptography;
using System.Text;
using AI.Assistant.Core.Rag.Interfaces;
using AI.Assistant.Core.Rag.Models;
using AI.Assistant.Core.Rag.Options;

namespace AI.Assistant.Infrastructure.Services.Rag.Scanner;

public class ProjectScanner : IProjectScanner
{
    private static readonly HashSet<string> IgnoreFolderSet;
    private static readonly HashSet<string> IgnoreExtensionSet;
    private static readonly HashSet<string> SupportedExtensionSet;
    private static readonly Dictionary<string, string> LanguageMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"] = "csharp",
        [".xaml"] = "xaml",
        [".json"] = "json",
        [".md"] = "markdown",
        [".xml"] = "xml",
        [".yaml"] = "yaml",
        [".yml"] = "yaml",
        [".js"] = "javascript",
        [".ts"] = "typescript",
        [".py"] = "python",
        [".java"] = "java",
        [".html"] = "html",
        [".css"] = "css",
        [".sql"] = "sql",
        [".ps1"] = "powershell",
        [".bat"] = "batch",
        [".sh"] = "shell",
        [".txt"] = "text"
    };

    private readonly RagOptions _options;

    static ProjectScanner()
    {
        IgnoreFolderSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IgnoreExtensionSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        SupportedExtensionSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public ProjectScanner(RagOptions options)
    {
        _options = options;
        RefreshLookups();
    }

    public async Task<IList<CodeFile>> ScanProjectAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(projectPath))
            return [];

        var result = new List<CodeFile>();
        var files = Directory.EnumerateFiles(projectPath, "*", SearchOption.AllDirectories);

        foreach (var filePath in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ShouldSkipFile(filePath))
                continue;

            var codeFile = await ReadFileAsync(filePath, projectPath, cancellationToken);
            if (codeFile is not null)
                result.Add(codeFile);
        }

        return result;
    }

    private bool ShouldSkipFile(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(extension))
            return true;

        if (IgnoreExtensionSet.Contains(extension))
            return true;

        if (!SupportedExtensionSet.Contains(extension))
            return true;

        var parts = filePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var part in parts.AsSpan()[..^1])
        {
            if (IgnoreFolderSet.Contains(part))
                return true;
        }

        return false;
    }

    private async Task<CodeFile?> ReadFileAsync(string filePath, string projectPath, CancellationToken cancellationToken)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
            var hash = ComputeHash(bytes);
            var (content, encoding) = DecodeText(bytes);

            var relativePath = Path.GetRelativePath(projectPath, filePath);
            var extension = Path.GetExtension(filePath);

            return new CodeFile
            {
                FilePath = relativePath,
                Content = content,
                Language = LanguageMap.TryGetValue(extension, out var lang) ? lang : "text",
                Encoding = encoding,
                FileHash = hash,
                LastModifiedTime = fileInfo.LastWriteTimeUtc
            };
        }
        catch
        {
            return null;
        }
    }

    private static string ComputeHash(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static (string content, string encoding) DecodeText(byte[] bytes)
    {
        if (bytes.Length == 0)
            return (string.Empty, "utf-8");

        // UTF-8 with BOM
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return (Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3), "utf-8-bom");
        }

        // Try UTF-8 without BOM
        try
        {
            if (IsValidUtf8(bytes))
            {
                return (Encoding.UTF8.GetString(bytes), "utf-8");
            }
        }
        catch
        {
            // fall through
        }

        // Fallback to system default
        var fallback = Encoding.Default;
        return (fallback.GetString(bytes), fallback.WebName);
    }

    private static bool IsValidUtf8(byte[] bytes)
    {
        try
        {
            var decoder = Encoding.UTF8.GetDecoder();
            var chars = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];
            decoder.Convert(bytes, 0, bytes.Length, chars, 0, chars.Length, true, out _, out _, out var completed);
            return completed;
        }
        catch
        {
            return false;
        }
    }

    private void RefreshLookups()
    {
        IgnoreFolderSet.Clear();
        foreach (var f in _options.IgnoreFolders)
            IgnoreFolderSet.Add(f);

        IgnoreExtensionSet.Clear();
        foreach (var e in _options.IgnoreExtensions)
            IgnoreExtensionSet.Add(e);

        SupportedExtensionSet.Clear();
        foreach (var e in _options.SupportedExtensions)
            SupportedExtensionSet.Add(e);
    }
}
