using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OperationsCopilot.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef</c> build a context without starting the API.
/// </summary>
/// <remarks>
/// Migrations are generated, not run, against this connection string, so it does not need to
/// point at a live database. Override it with <c>OPERATIONSDB_CONNECTION</c> when scaffolding
/// against a real one.
/// </remarks>
public sealed class OperationsDbContextFactory : IDesignTimeDbContextFactory<OperationsDbContext>
{
    private const string DefaultConnectionString =
        "Host=localhost;Port=55433;Database=operationscopilot;Username=postgres;Password=postgres";

    public OperationsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("OPERATIONSDB_CONNECTION")
            ?? DefaultConnectionString;

        var options = new DbContextOptionsBuilder<OperationsDbContext>()
            .UseNpgsql(connectionString, npgsql =>
            {
                npgsql.UseVector();
                npgsql.MigrationsAssembly(typeof(OperationsDbContext).Assembly.FullName);
            })
            .UseSnakeCaseNamingConvention()
            .Options;

        return new OperationsDbContext(options);
    }
}
