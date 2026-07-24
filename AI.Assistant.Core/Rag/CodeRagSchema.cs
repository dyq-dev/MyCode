using AI.Assistant.Core.Rag.Models;

namespace AI.Assistant.Core.Rag;

/// <summary>Qdrant payload 字段名与类型标识常量，集中管理避免硬编码散落</summary>
public static class CodeRagSchema
{
    // ========== 类型标识 ==========
    public const string TypeChunk = "chunk";
    public const string TypeIndexRecord = "index_record";

    // ========== Payload 字段名 ==========
    public const string FieldType = "_type";
    public const string FieldFilePath = "file_path";
    public const string FieldContent = "content";
    public const string FieldLanguage = "language";
    public const string FieldChunkType = "chunk_type";
    public const string FieldStartLine = "start_line";
    public const string FieldEndLine = "end_line";
    public const string FieldProjectPath = "project_path";
    public const string FieldNamespace = "namespace";
    public const string FieldClassName = "class_name";
    public const string FieldMethodName = "method_name";
    public const string FieldSymbolName = "symbol_name";
    public const string FieldIndexedAt = "indexed_at";
    public const string FieldFileHash = "file_hash";
    public const string FieldLastModifiedAt = "last_modified_at";
}

/// <summary>将 Qdrant payload 映射为 CodeChunk 领域对象</summary>
public static class CodeRagMapper
{
    public static CodeChunk ToCodeChunk(Dictionary<string, string> metadata, string chunkId)
    {
        return new CodeChunk
        {
            Id = chunkId,
            FilePath = metadata.GetValueOrDefault(CodeRagSchema.FieldFilePath, ""),
            Content = metadata.GetValueOrDefault(CodeRagSchema.FieldContent, ""),
            Language = metadata.GetValueOrDefault(CodeRagSchema.FieldLanguage, ""),
            ChunkType = ParseChunkType(metadata.GetValueOrDefault(CodeRagSchema.FieldChunkType, "")),
            StartLine = ParseInt(metadata.GetValueOrDefault(CodeRagSchema.FieldStartLine, "1")),
            EndLine = ParseInt(metadata.GetValueOrDefault(CodeRagSchema.FieldEndLine, "1")),
            ProjectPath = metadata.GetValueOrDefault(CodeRagSchema.FieldProjectPath, ""),
            Namespace = metadata.GetValueOrDefault(CodeRagSchema.FieldNamespace, ""),
            ClassName = metadata.GetValueOrDefault(CodeRagSchema.FieldClassName, ""),
            MethodName = metadata.GetValueOrDefault(CodeRagSchema.FieldMethodName, ""),
            SymbolName = metadata.GetValueOrDefault(CodeRagSchema.FieldSymbolName, ""),
            IndexedAt = ParseDateTime(metadata.GetValueOrDefault(CodeRagSchema.FieldIndexedAt, ""))
        };
    }

    private static CodeChunkType ParseChunkType(string value)
        => Enum.TryParse<CodeChunkType>(value, out var result) ? result : CodeChunkType.File;

    private static int ParseInt(string value)
        => int.TryParse(value, out var result) ? result : 1;

    private static DateTime ParseDateTime(string value)
        => DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var result)
            ? result : DateTime.MinValue;
}
