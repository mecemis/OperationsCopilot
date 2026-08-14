namespace OperationsCopilot.Infrastructure.Persistence;

/// <summary>
/// The vector width the EF Core migration creates the embedding column with.
/// </summary>
/// <remarks>
/// This is a starting point, not the truth. The width that matters is whatever the configured
/// embedding model produces, and that is only known at run time — see <see cref="VectorSchema"/>,
/// which reconciles the column with the active model on startup. This constant exists because a
/// migration is static SQL and has to name <em>some</em> width; the value matches
/// <c>text-embedding-3-small</c> for no better reason than that it was the first provider wired
/// up. Changing it would only alter the width a freshly migrated database starts at.
/// </remarks>
public static class EmbeddingDefaults
{
    /// <summary>Width baked into the initial migration.</summary>
    public const int MigrationDimensions = 1536;

    /// <summary>PostgreSQL column type used by the initial migration.</summary>
    public static readonly string ColumnType = $"vector({MigrationDimensions})";
}
