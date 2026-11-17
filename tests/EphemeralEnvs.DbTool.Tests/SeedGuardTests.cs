using EphemeralEnvs.DbTool.Seeding;

namespace EphemeralEnvs.DbTool.Tests;

public sealed class SeedGuardTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("production")]
    [InlineData("PROD")]
    [InlineData(" Production ")]
    public void IsAllowed_MissingOrProductionName_False(string? environmentName)
    {
        Assert.False(SeedGuard.IsAllowed(environmentName));
    }

    [Theory]
    [InlineData("local")]
    [InlineData("gh-feature-x")]
    [InlineData("production-like")]
    public void IsAllowed_OtherName_True(string environmentName)
    {
        Assert.True(SeedGuard.IsAllowed(environmentName));
    }
}
