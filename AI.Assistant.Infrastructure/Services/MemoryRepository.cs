using Dapper;
using Microsoft.Data.SqlClient;

namespace AI.Assistant.Infrastructure.Services;

public class MemoryRepository
{
    private readonly string _connectionString;

    public MemoryRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task EnsureTablesAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Memories')
            BEGIN
                CREATE TABLE Memories (
                    Id UNIQUEIDENTIFIER PRIMARY KEY,
                    Content NVARCHAR(MAX) NOT NULL,
                    Role NVARCHAR(20) NOT NULL,
                    SessionId NVARCHAR(100) NOT NULL,
                    VectorId NVARCHAR(100) NOT NULL,
                    CreatedAt DATETIME2 NOT NULL
                );
                CREATE INDEX IX_Memories_SessionId ON Memories(SessionId);
            END

            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'SessionSummaries')
            BEGIN
                CREATE TABLE SessionSummaries (
                    SessionId NVARCHAR(100) PRIMARY KEY,
                    Summary NVARCHAR(2000) NOT NULL,
                    Version INT NOT NULL DEFAULT 1,
                    CreatedAt DATETIME2 NOT NULL,
                    UpdatedAt DATETIME2 NOT NULL
                );
            END
            ELSE
            BEGIN
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('SessionSummaries') AND name = 'Version')
                    ALTER TABLE SessionSummaries ADD Version INT NOT NULL DEFAULT 1;
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('SessionSummaries') AND name = 'CreatedAt')
                    ALTER TABLE SessionSummaries ADD CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE();
            END
            """;

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await conn.ExecuteAsync(sql);
    }

    // ============ 原始消息 (Memories) ============

    public async Task SaveAsync(MemoryRecord record, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO Memories (Id, Content, Role, SessionId, VectorId, CreatedAt)
            VALUES (@Id, @Content, @Role, @SessionId, @VectorId, @CreatedAt)
            """;

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await conn.ExecuteAsync(sql, record);
    }

    public async Task<IList<MemoryRecord>> GetByVectorIdsAsync(IEnumerable<string> vectorIds, CancellationToken cancellationToken = default)
    {
        var ids = vectorIds.ToList();
        if (ids.Count == 0)
            return [];

        var sql = $"SELECT Id, Content, Role, SessionId, VectorId, CreatedAt FROM Memories WHERE VectorId IN @Ids";

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        var result = await conn.QueryAsync<MemoryRecord>(sql, new { Ids = ids }, commandTimeout: 5);
        return result.AsList();
    }

    // ============ 会话摘要 (SessionSummaries) ============

    public async Task<SessionSummaryRecord?> GetSummaryAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT SessionId, Summary, UpdatedAt FROM SessionSummaries WHERE SessionId = @SessionId";

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        return await conn.QueryFirstOrDefaultAsync<SessionSummaryRecord>(sql, new { SessionId = sessionId });
    }

    public async Task SaveSummaryAsync(SessionSummaryRecord record, CancellationToken cancellationToken = default)
    {
        const string sql = """
            MERGE SessionSummaries AS target
            USING (SELECT @SessionId AS SessionId) AS source
            ON target.SessionId = source.SessionId
            WHEN MATCHED THEN
                UPDATE SET Summary = @Summary, Version = target.Version + 1, UpdatedAt = @UpdatedAt
            WHEN NOT MATCHED THEN
                INSERT (SessionId, Summary, Version, CreatedAt, UpdatedAt)
                VALUES (@SessionId, @Summary, 1, @CreatedAt, @UpdatedAt);
            """;

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await conn.ExecuteAsync(sql, record);
    }
}

public class MemoryRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Content { get; set; } = string.Empty;
    public string Role { get; set; } = "user";
    public string SessionId { get; set; } = string.Empty;
    public string VectorId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class SessionSummaryRecord
{
    public string SessionId { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
