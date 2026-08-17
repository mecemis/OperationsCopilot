using OperationsCopilot.Infrastructure.Persistence;

namespace OperationsCopilot.Api.Endpoints;

/// <summary>Startup-time database preparation.</summary>
public static class DatabaseStartupExtensions
{
    /// <summary>
    /// Migrates, seeds, and indexes on startup so that a fresh clone works from a single
    /// <c>docker compose up</c>. Set <c>Database:InitializeOnStartup</c> to <c>false</c> in any
    /// environment where schema changes should come from a release pipeline instead.
    /// </summary>
    public static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        if (!app.Configuration.GetValue("Database:InitializeOnStartup", defaultValue: true))
        {
            app.Logger.LogInformation("Database initialization on startup is disabled.");
            return;
        }

        using var scope = app.Services.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();

        await initializer.InitializeAsync(app.Lifetime.ApplicationStopping);
    }
}
