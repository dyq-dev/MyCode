using Qdrant.Client.Grpc;

namespace AI.Assistant.Infrastructure.Services.Rag.Storage;

public interface IQdrantIndexStorage
{
    Task<bool> CollectionExistsAsync(string collectionName, CancellationToken cancellationToken = default);
    Task CreateCollectionAsync(string collectionName, VectorParams vectorParams, CancellationToken cancellationToken = default);
    Task DeleteByFilterAsync(string collectionName, Filter filter, CancellationToken cancellationToken = default);
    Task<IList<RetrievedPoint>> ScrollAllAsync(string collectionName, Filter filter, CancellationToken cancellationToken = default);
}
