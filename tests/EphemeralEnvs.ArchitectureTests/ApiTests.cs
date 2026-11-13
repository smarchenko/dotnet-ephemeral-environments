using Microsoft.AspNetCore.Mvc;

using NetArchTest.Rules;

namespace EphemeralEnvs.ArchitectureTests;

public sealed class ApiTests
{
    [Fact]
    public void Controllers_DoNotUse_DomainTypes()
    {
        ArchAssert.Passes(
            Types.InAssembly(Assemblies.Api).That().Inherit<ControllerBase>().ShouldNot().HaveDependencyOnAny(Ns.Domain),
            "Controllers work with DTOs (Contracts) and handlers; the Domain reference exists only for exception mapping.");
    }

    [Fact]
    public void Controllers_AreSealed_AndNamedController()
    {
        ArchAssert.Passes(
            Types.InAssembly(Assemblies.Api).That().Inherit<ControllerBase>()
                .Should().BeSealed().And().HaveNameEndingWith("Controller"),
            "Convention for API controllers.");
    }
}
