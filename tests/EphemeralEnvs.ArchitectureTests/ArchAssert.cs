using NetArchTest.Rules;

namespace EphemeralEnvs.ArchitectureTests;

internal static class ArchAssert
{
    /// <summary>Fails with the list of offending types, so the message says what to fix.</summary>
    public static void Passes(ConditionList rule, string because)
    {
        var result = rule.GetResult();
        var failing = result.FailingTypes?.Select(t => t.FullName) ?? [];

        Assert.True(result.IsSuccessful, $"{because}{Environment.NewLine}Violations: {string.Join(", ", failing)}");
    }
}
