using Microsoft.Extensions.Time.Testing;
using OperationsCopilot.Domain.Chat;
using OperationsCopilot.Infrastructure.Conversations;
using Shouldly;
using Xunit;

namespace OperationsCopilot.UnitTests.Conversations;

public class InMemoryConversationStoreTests
{
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-03-01T09:00:00Z"));

    [Fact]
    public async Task GetHistoryAsync_ReturnsEmptyForAnUnknownConversation()
    {
        var store = new InMemoryConversationStore(_time);

        var history = await store.GetHistoryAsync("never-seen", TestContext.Current.CancellationToken);

        history.ShouldBeEmpty();
    }

    [Fact]
    public async Task AppendAsync_ReturnsTurnsInOrder()
    {
        var store = new InMemoryConversationStore(_time);

        await store.AppendAsync("c1", [Turn(ChatRole.User, "first")], TestContext.Current.CancellationToken);
        await store.AppendAsync("c1", [Turn(ChatRole.Assistant, "second")], TestContext.Current.CancellationToken);

        var history = await store.GetHistoryAsync("c1", TestContext.Current.CancellationToken);

        history.Select(t => t.Content).ShouldBe(["first", "second"]);
    }

    [Fact]
    public async Task AppendAsync_KeepsConversationsIsolated()
    {
        var store = new InMemoryConversationStore(_time);

        await store.AppendAsync("c1", [Turn(ChatRole.User, "mine")], TestContext.Current.CancellationToken);
        await store.AppendAsync("c2", [Turn(ChatRole.User, "yours")], TestContext.Current.CancellationToken);

        var history = await store.GetHistoryAsync("c1", TestContext.Current.CancellationToken);

        history.Single().Content.ShouldBe("mine");
    }

    [Fact]
    public async Task AppendAsync_DropsTheOldestTurnsOnceTheCapIsReached()
    {
        var store = new InMemoryConversationStore(_time);

        for (var i = 1; i <= 20; i++)
        {
            await store.AppendAsync("c1", [Turn(ChatRole.User, $"turn {i}")], TestContext.Current.CancellationToken);
        }

        var history = await store.GetHistoryAsync("c1", TestContext.Current.CancellationToken);

        // The cap is what stops a long conversation growing the prompt without bound.
        history.Count.ShouldBe(12);
        history.First().Content.ShouldBe("turn 9");
        history.Last().Content.ShouldBe("turn 20");
    }

    [Fact]
    public async Task GetHistoryAsync_ForgetsConversationsAfterTheirLifetime()
    {
        var store = new InMemoryConversationStore(_time);
        await store.AppendAsync("c1", [Turn(ChatRole.User, "hello")], TestContext.Current.CancellationToken);

        _time.Advance(TimeSpan.FromHours(1) + TimeSpan.FromMinutes(1));

        var history = await store.GetHistoryAsync("c1", TestContext.Current.CancellationToken);

        history.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetHistoryAsync_ExtendsTheLifetimeOnAccess()
    {
        var store = new InMemoryConversationStore(_time);
        await store.AppendAsync("c1", [Turn(ChatRole.User, "hello")], TestContext.Current.CancellationToken);

        // An active conversation should not expire mid-use.
        _time.Advance(TimeSpan.FromMinutes(50));
        await store.GetHistoryAsync("c1", TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromMinutes(50));

        var history = await store.GetHistoryAsync("c1", TestContext.Current.CancellationToken);

        history.ShouldNotBeEmpty();
    }

    private ChatTurn Turn(ChatRole role, string content) => new(role, content, _time.GetUtcNow());
}
