using System.ComponentModel.DataAnnotations;
using OperationsCopilot.Domain.Abstractions;
using OperationsCopilot.Domain.Chat;

namespace OperationsCopilot.Api.Endpoints;

/// <summary>The single chat endpoint. Everything the copilot does is reached through here.</summary>
public static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api").WithTags("Chat");

        group.MapPost("/chat", HandleChatAsync)
            .WithName("Chat")
            .WithSummary("Ask the operations copilot a question.")
            .WithDescription(
                "The agent decides which tools to call — low stock, sales summary, product " +
                "details, knowledge base search — and may combine several in one answer. The " +
                "response reports which tools ran, which document passages were retrieved, and " +
                "how long the whole turn took.")
            .Produces<ChatResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return routes;
    }

    private static async Task<IResult> HandleChatAsync(
        ChatRequest request,
        ICopilotAgent agent,
        CancellationToken cancellationToken)
    {
        if (Validate(request) is { } errors)
        {
            return Results.ValidationProblem(errors);
        }

        var response = await agent.AskAsync(request, cancellationToken);

        return Results.Ok(response);
    }

    /// <summary>
    /// Validates against the data annotations on <see cref="ChatRequest"/> so the contract has a
    /// single definition, next to the type it describes.
    /// </summary>
    /// <returns>Field errors, or null when the request is valid.</returns>
    private static Dictionary<string, string[]>? Validate(ChatRequest request)
    {
        var results = new List<ValidationResult>();

        var isValid = Validator.TryValidateObject(
            request,
            new ValidationContext(request),
            results,
            validateAllProperties: true);

        if (isValid)
        {
            return null;
        }

        return results
            .SelectMany(
                result => result.MemberNames.DefaultIfEmpty(nameof(ChatRequest.Message)),
                (result, member) => (Member: member, result.ErrorMessage))
            .GroupBy(entry => entry.Member, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(entry => entry.ErrorMessage ?? "Invalid value.").ToArray(),
                StringComparer.Ordinal);
    }
}
