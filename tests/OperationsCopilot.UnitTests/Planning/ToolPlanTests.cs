using OperationsCopilot.Domain.Planning;
using Shouldly;
using Xunit;

namespace OperationsCopilot.UnitTests.Planning;

public class ToolPlanTests
{
    [Fact]
    public void IsDateAnchored_IsTrueWhenAnArgumentNamesAnExplicitDate()
    {
        // "Last month" is resolved by the model into fixed bounds using the date it was told.
        // Those bounds answer a different question next month.
        var plan = Plan(("startDate", "2026-08-01"), ("endDate", "2026-08-31"));

        plan.IsDateAnchored.ShouldBeTrue();
    }

    [Fact]
    public void IsDateAnchored_IsFalseForARelativeWindow()
    {
        // lastDays is resolved against today at execution time, so it means the same thing
        // whenever it runs.
        var plan = Plan(("lastDays", "30"));

        plan.IsDateAnchored.ShouldBeFalse();
    }

    [Fact]
    public void IsDateAnchored_IsFalseForASkuThatMerelyContainsDigits() =>
        Plan(("skuOrName", "PT-1001")).IsDateAnchored.ShouldBeFalse();

    [Fact]
    public void IsDateAnchored_IsFalseWhenNoArgumentIsSupplied() =>
        Plan().IsDateAnchored.ShouldBeFalse();

    [Fact]
    public void IsDateAnchored_IgnoresANullArgument() =>
        Plan(("warehouseCode", null)).IsDateAnchored.ShouldBeFalse();

    private static ToolPlan Plan(params (string Name, string? Value)[] arguments) =>
        new([
            new PlannedToolCall(
                "Operations",
                "GetSalesSummary",
                arguments.ToDictionary(a => a.Name, a => a.Value)),
        ]);
}
