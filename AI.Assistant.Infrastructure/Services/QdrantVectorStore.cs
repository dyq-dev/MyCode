using AI.Assistant.Core.Interfaces;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace AI.Assistant.Infrastructure.Services;

/// <summary>
/// Qdrant 向量数据库实现（使用官方 Qdrant.Client SDK，gRPC 协议）
/// 向量维度 512（bge-small-zh-v1.5），距离度量 Cosine。
/// </summary>
public class QdrantVectorStore : IVectorStore
{
    private readonly QdrantClient _client;
    private const int VectorSize = 512;

    public QdrantVectorStore(QdrantClient client)
    {
        _client = client;
    }

    public async Task UpsertAsync(string collection, string id, float[] vector, Dictionary<string, string>? metadata = null, CancellationToken cancellationToken = default)
    {
        await EnsureCollectionAsync(collection, cancellationToken);

        var point = new PointStruct
        {
            Id = new PointId { Uuid = id },
            Vectors = vector
        };
        if (metadata is not null)
        {
            foreach (var kv in metadata)
                point.Payload.Add(kv.Key, kv.Value);
        }

        await _client.UpsertAsync(collection, [point], cancellationToken: cancellationToken);
    }

    public async Task<IList<VectorSearchResult>> SearchAsync(string collection, float[] queryVector, int topK = 5, Dictionary<string, string>? filter = null, CancellationToken cancellationToken = default)
    {
        await EnsureCollectionAsync(collection, cancellationToken);

        Filter? qdrantFilter = null;
        if (filter is not null && filter.Count > 0)
        {
            var conditions = filter.Select(kv => new Condition
            {
                Field = new FieldCondition
                {
                    Key = kv.Key,
                    Match = new Match { Keywords = new RepeatedStrings { Strings = { kv.Value } } }
                }
            }).ToList();

            qdrantFilter = new Filter();
            qdrantFilter.Must.AddRange(conditions);
        }

        var results = await _client.SearchAsync(collection, queryVector, limit: (ulong)topK, filter: qdrantFilter, cancellationToken: cancellationToken);

        return results.Select(r => new VectorSearchResult
        {
            Id = r.Id.Uuid,
            Score = r.Score,
            Metadata = r.Payload.ToDictionary(kv => kv.Key, kv => kv.Value.StringValue ?? kv.Value.ToString())
        }).ToList();
    }

    public async Task DeleteAsync(string collection, string id, CancellationToken cancellationToken = default)
    {
        await _client.DeleteAsync(collection, [new PointId { Uuid = id }], cancellationToken: cancellationToken);
    }

    private async Task EnsureCollectionAsync(string collection, CancellationToken cancellationToken)
    {
        if (await _client.CollectionExistsAsync(collection, cancellationToken))
            return;

        await _client.CreateCollectionAsync(collection, new VectorParams
        {
            Size = VectorSize,
            Distance = Distance.Cosine
        }, cancellationToken: cancellationToken);
    }
}
