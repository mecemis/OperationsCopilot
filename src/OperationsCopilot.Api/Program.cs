using Microsoft.AspNetCore.Diagnostics;
using OperationsCopilot.Agent;
using OperationsCopilot.Api.Endpoints;
using OperationsCopilot.Infrastructure;
using OperationsCopilot.Infrastructure.Persistence;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOperationsCopilotInfrastructure(builder.Configuration);
builder.Services.AddOperationsCopilotAgent(builder.Configuration);

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<OperationsDbContext>("database");

var app = builder.Build();

// A failing tool or model call should read as a problem+json response, not a stack trace.
app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var feature = context.Features.Get<IExceptionHandlerFeature>();
    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("UnhandledException");

    logger.LogError(feature?.Error, "Unhandled exception while handling {Path}.", context.Request.Path);

    await Results.Problem(
            title: "The request could not be completed.",
            detail: app.Environment.IsDevelopment() ? feature?.Error.Message : null,
            statusCode: StatusCodes.Status500InternalServerError)
        .ExecuteAsync(context);
}));

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options.WithTitle("Operations Copilot API"));
}

// The test console at "/" — a single static page for driving the agent by hand and reading back
// the trace. It is served in every environment because this is a sample; a real deployment
// would put it behind the same authentication as the API, or drop it entirely.
// MapStaticAssets (rather than UseStaticFiles) so the build's pre-compressed Brotli and gzip
// variants are negotiated and assets carry ETag/caching headers.
//
// UseDefaultFiles rewrites "/" to "/index.html", but it only helps if it runs before routing.
// Minimal APIs insert UseRouting at the very start of the pipeline unless it is called
// explicitly, so calling it here is what puts the rewrite ahead of endpoint matching.
app.UseDefaultFiles();
app.UseRouting();

app.MapStaticAssets();

app.MapChatEndpoints();
app.MapHealthChecks("/health");

await app.InitializeDatabaseAsync();

await app.RunAsync();

/// <summary>Exposed so the integration tests can boot the real application with WebApplicationFactory.</summary>
public partial class Program;
