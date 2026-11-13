using NetArchTest.Rules;

namespace EphemeralEnvs.ArchitectureTests;

/// <summary>
/// Allowed dependencies: Api → Application, Contracts, Domain (exceptions only); Application → Domain, Data, Contracts;
/// Data → Domain; Web → Contracts. Domain, Contracts and ServiceDefaults depend on nothing of ours;
/// Api and Web both use ServiceDefaults.
/// </summary>
public sealed class LayerTests
{
    [Fact]
    public void Api_DoesNotDependOn_DataOrEfCore()
    {
        ArchAssert.Passes(
            Types.InAssembly(Assemblies.Api).ShouldNot().HaveDependencyOnAny(Ns.Data, Ns.EntityFrameworkCore, Ns.Npgsql),
            "The API must reach the database only through Application handlers, never through a DbContext.");
    }

    [Fact]
    public void Domain_DependsOnNothing()
    {
        ArchAssert.Passes(
            Types.InAssembly(Assemblies.Domain).ShouldNot().HaveDependencyOnAny(
                Ns.Api, Ns.Application, Ns.Contracts, Ns.Data, Ns.Web, Ns.AspNetCore, Ns.EntityFrameworkCore, Ns.Npgsql),
            "The domain is plain C#: no persistence, web or other layers.");
    }

    [Fact]
    public void Contracts_DependOnNothing()
    {
        ArchAssert.Passes(
            Types.InAssembly(Assemblies.Contracts).ShouldNot().HaveDependencyOnAny(
                Ns.Api, Ns.Application, Ns.Data, Ns.Domain, Ns.Web, Ns.EntityFrameworkCore),
            "DTOs are shared with the Web: they must not leak domain or persistence types.");
    }

    [Fact]
    public void Data_DoesNotDependOn_UpperLayers()
    {
        ArchAssert.Passes(
            Types.InAssembly(Assemblies.Data).ShouldNot().HaveDependencyOnAny(Ns.Api, Ns.Application, Ns.Contracts, Ns.Web),
            "Persistence knows only the domain.");
    }

    [Fact]
    public void Application_DoesNotDependOn_ApiWebOrAspNetCore()
    {
        ArchAssert.Passes(
            Types.InAssembly(Assemblies.Application).ShouldNot().HaveDependencyOnAny(Ns.Api, Ns.Web, Ns.AspNetCore),
            "Use cases must not know about HTTP: they can be called from anywhere (API, seed, background jobs).");
    }

    [Fact]
    public void Web_DependsOnlyOnContracts()
    {
        ArchAssert.Passes(
            Types.InAssembly(Assemblies.Web).ShouldNot().HaveDependencyOnAny(
                Ns.Api, Ns.Application, Ns.Data, Ns.Domain, Ns.EntityFrameworkCore, Ns.Npgsql),
            "The Web talks to the API over HTTP only.");
    }

    [Fact]
    public void ServiceDefaults_DependsOnNothingOfOurs()
    {
        ArchAssert.Passes(
            Types.InAssembly(Assemblies.ServiceDefaults).ShouldNot().HaveDependencyOnAny(
                Ns.Api, Ns.Application, Ns.Contracts, Ns.Data, Ns.Domain, Ns.Web, Ns.EntityFrameworkCore),
            "Shared host setup (logging, later OpenTelemetry) is infrastructure for every service, not app code.");
    }

    [Theory]
    [InlineData(nameof(Assemblies.Api))]
    [InlineData(nameof(Assemblies.Web))]
    public void Services_UseServiceDefaults(string service)
    {
        // One place for logging/telemetry setup: a service that configures it itself would drift from the others.
        var assembly = service == nameof(Assemblies.Api) ? Assemblies.Api : Assemblies.Web;

        Assert.Contains(
            assembly.GetReferencedAssemblies(),
            reference => reference.Name == Assemblies.ServiceDefaults.GetName().Name);
    }
}
