using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace AI.Assistant.Infrastructure.Services.Rag.Storage;

public class QdrantIndexStorage : IQdrantIndexStorage
{
    private readonly QdrantClient _client;

    public QdrantIndexStorage(QdrantClient client)
    {
        _client = client;
    }

    public async Task<bool> CollectionExistsAsync(string collectionName, CancellationToken cancellationToken = default)
        => await _client.CollectionExistsAsync(collectionName, cancellationToken);

    public async Task CreateCollectionAsync(string collectionName, VectorParams vectorParams, CancellationToken cancellationToken = default)
        => await _client.CreateCollectionAsync(collectionName, vectorParams, cancellationToken: cancellationToken);

    public async Task DeleteByFilterAsync(string collectionName, Filter filter, CancellationToken cancellationToken = default)
        => await _client.DeleteAsync(collectionName, filter: filter, cancellationToken: cancellationToken);

    public async Task<IList<RetrievedPoint>> ScrollAllAsync(string collectionName, Filter filter, CancellationToken cancellationToken = default)
    {
        var results = new List<RetrievedPoint>();
        PointId? offset = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            var page = await _client.ScrollAsync(
                collectionName: collectionName,
                filter: filter,
                limit: 100,
                offset: offset,
                cancellationToken: cancellationToken);

            results.AddRange(page.Result);

            if (page.NextPageOffset is null)
                break;
            offset = page.NextPageOffset;
        }

        return results;
    }
}
