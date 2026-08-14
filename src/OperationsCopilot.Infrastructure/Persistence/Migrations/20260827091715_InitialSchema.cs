using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Pgvector;

#nullable disable

namespace OperationsCopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "document_chunks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_file = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    document_title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    heading = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    chunk_index = table.Column<int>(type: "integer", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    embedding = table.Column<Vector>(type: "vector(1536)", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    indexed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_chunks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    sku = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    supplier = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    is_discontinued = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_products", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "inventory_items",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    product_id = table.Column<int>(type: "integer", nullable: false),
                    warehouse_code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    quantity_on_hand = table.Column<int>(type: "integer", nullable: false),
                    reorder_threshold = table.Column<int>(type: "integer", nullable: false),
                    last_counted_on = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_inventory_items_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sales",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    product_id = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    region = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    sold_on = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales", x => x.id);
                    table.ForeignKey(
                        name: "fk_sales_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_document_chunks_source_file_chunk_index",
                table: "document_chunks",
                columns: new[] { "source_file", "chunk_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inventory_items_product_id_warehouse_code",
                table: "inventory_items",
                columns: new[] { "product_id", "warehouse_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inventory_items_warehouse_code_quantity_on_hand",
                table: "inventory_items",
                columns: new[] { "warehouse_code", "quantity_on_hand" });

            migrationBuilder.CreateIndex(
                name: "ix_products_category",
                table: "products",
                column: "category");

            migrationBuilder.CreateIndex(
                name: "ix_products_name",
                table: "products",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "ix_products_sku",
                table: "products",
                column: "sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_product_id_sold_on",
                table: "sales",
                columns: new[] { "product_id", "sold_on" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_sold_on",
                table: "sales",
                column: "sold_on");

            migrationBuilder.CreateIndex(
                name: "ix_sales_sold_on_region",
                table: "sales",
                columns: new[] { "sold_on", "region" });

            // HNSW index for approximate nearest-neighbour search. EF Core cannot express a
            // pgvector operator class, so it is created directly.
            //
            // vector_cosine_ops must match the `<=>` operator used by
            // PgVectorKnowledgeBaseSearch: an index built for a different distance function is
            // simply ignored by the planner, and the query silently degrades to a full scan.
            //
            // m = 16 and ef_construction = 64 are pgvector's defaults, and are a reasonable
            // balance of build time against recall for a knowledge base of this size.
            migrationBuilder.Sql("""
                CREATE INDEX ix_document_chunks_embedding_hnsw
                ON document_chunks
                USING hnsw (embedding vector_cosine_ops)
                WITH (m = 16, ef_construction = 64);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_document_chunks_embedding_hnsw;");

            migrationBuilder.DropTable(
                name: "document_chunks");

            migrationBuilder.DropTable(
                name: "inventory_items");

            migrationBuilder.DropTable(
                name: "sales");

            migrationBuilder.DropTable(
                name: "products");
        }
    }
}
