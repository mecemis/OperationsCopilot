using Testcontainers.PostgreSql;
using Xunit;

namespace OperationsCopilot.TestSupport;

/// <summary>
/// A throwaway PostgreSQL instance with pgvector, started once per test collection.
/// </summary>
/// <remarks>
/// The tests run against a real database rather than an in-memory provider on purpose: the
/// behaviour under test — the <c>&lt;=&gt;</c> cosine operator, the HNSW index, and the
/// <c>vector(1536)</c> column type — has no in-memory equivalent, so a fake provider would
/// verify nothing that matters.
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    /// <summary>Same image the application's compose file uses, so tests and runtime agree.</summary>
    private const string Image = "pgvector/pgvector:pg17";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(Image)
        .WithDatabase("operationscopilot_tests")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public ValueTask InitializeAsync() => new(_container.StartAsync());

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
