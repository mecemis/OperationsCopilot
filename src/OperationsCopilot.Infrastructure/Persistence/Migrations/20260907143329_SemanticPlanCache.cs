using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace OperationsCopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SemanticPlanCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "semantic_plan_cache",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    question = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    embedding = table.Column<Vector>(type: "vector(1536)", nullable: false),
                    calls = table.Column<string>(type: "jsonb", nullable: false),
                    is_date_anchored = table.Column<bool>(type: "boolean", nullable: false),
                    captured_on = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    times_reused = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_semantic_plan_cache", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_semantic_plan_cache_created_at",
                table: "semantic_plan_cache",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_semantic_plan_cache_is_date_anchored_captured_on",
                table: "semantic_plan_cache",
                columns: new[] { "is_date_anchored", "captured_on" });

            // The second HNSW index in this schema, over the questions the cache is searched by.
            // Same reasoning as document_chunks: EF Core cannot express a pgvector operator
            // class, and vector_cosine_ops must match the `<=>` operator used by
            // PgVectorSemanticPlanCache or the planner ignores the index and the lookup
            // degrades to a full scan.
            //
            // The width here is only where a freshly migrated database starts. VectorSchema
            // reconciles it with the configured embedding model on startup, for this table and
            // for document_chunks together.
            migrationBuilder.Sql("""
                CREATE INDEX ix_semantic_plan_cache_embedding_hnsw
                ON semantic_plan_cache
                USING hnsw (embedding vector_cosine_ops)
                WITH (m = 16, ef_construction = 64);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_semantic_plan_cache_embedding_hnsw;");

            migrationBuilder.DropTable(
                name: "semantic_plan_cache");
        }
    }
}
