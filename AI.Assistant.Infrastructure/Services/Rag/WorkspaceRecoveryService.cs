using System.Globalization;
using AI.Assistant.Core.Rag.Interfaces;
using AI.Assistant.Core.Rag.Models;
using AI.Assistant.Core.Rag.Options;
using AI.Assistant.Infrastructure.Services.Rag.Storage;
using Google.Protobuf.Collections;
using Qdrant.Client.Grpc;

namespace AI.Assistant.Infrastructure.Services.Rag;

public class WorkspaceRecoveryService : IWorkspaceRecoveryService
{
    private readonly IQdrantIndexStorage _storage;
    private readonly RagOptions _options;

    public WorkspaceRecoveryService(IQdrantIndexStorage storage, RagOptions options)
    {
        _storage = storage;
        _options = options;
    }

    public async Task<IList<KnowledgeSource>> RecoverSourcesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var collection = _options.QdrantCollectionName;

            if (!await _storage.CollectionExistsAsync(collection, cancellationToken))
                return [];

            var points = await _storage.ScrollAllAsync(collection, filter: null!, cancellationToken);

            if (points.Count == 0)
                return [];

            return points
                .GroupBy(p => GetPayloadString(p.Payload, "source_id"))
                .Where(g => !string.IsNullOrEmpty(g.Key))
                .Select(g =>
                {
                    var first = g.First().Payload;
                    var uri = GetPayloadString(first, "source_uri");
                    var fileName = Path.GetFileName(uri);
                    var ext = Path.GetExtension(uri).ToLowerInvariant();

                    var sourceType = ext switch
                    {
                        ".pdf" => SourceType.Pdf,
                        ".md" or ".markdown" => SourceType.Markdown,
                        ".txt" => SourceType.Text,
                        ".cs" or ".sln" or ".csproj" or ".fsproj" => SourceType.Code,
                        _ => SourceType.Document
                    };

                    var indexedAtStr = GetPayloadString(first, "indexed_at");
                    DateTime createdAt = DateTime.TryParse(indexedAtStr, CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out var dt) ? dt : DateTime.UtcNow;

                    return new KnowledgeSource
                    {
                        Id = g.Key!,
                        WorkspaceId = "default",
                        Name = fileName,
                        SourceType = sourceType,
                        Uri = uri,
                        IsEnabled = true,
                        AutoSync = false,
                        CreatedAt = createdAt
                    };
                })
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static string GetPayloadString(MapField<string, Value> payload, string key)
    {
        if (payload.TryGetValue(key, out var value))
        {
            return value.KindCase switch
            {
                Value.KindOneofCase.StringValue => value.StringValue,
                Value.KindOneofCase.IntegerValue => value.IntegerValue.ToString(),
                Value.KindOneofCase.DoubleValue => value.DoubleValue.ToString(),
                _ => string.Empty
            };
        }
        return string.Empty;
    }
}
