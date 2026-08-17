using OperationsCopilot.TestSupport;
using Xunit;

namespace OperationsCopilot.EvaluationTests;

/// <summary>
/// Shares one migrated, seeded and indexed database across every test class in this assembly.
/// </summary>
/// <remarks>
/// xUnit requires a collection definition to live in the assembly that uses it, so this sits
/// here rather than alongside the fixture in OperationsCopilot.TestSupport. Starting the
/// container once and reusing it keeps the suite to a few seconds instead of a few minutes.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<SeededDatabaseFixture>
{
    public const string Name = "SeededDatabase";
}
