using EphemeralEnvs.Application.Abstractions;

using NetArchTest.Rules;

namespace EphemeralEnvs.ArchitectureTests;

/// <summary>CQRS level 2: commands write through AppDbContext, queries read through the read-only ReadDbContext.</summary>
public sealed class CqrsTests
{
    [Fact]
    public void QueryHandlers_DoNotUse_WriteDbContext()
    {
        ArchAssert.Passes(
            Types.InAssembly(Assemblies.Application).That().ImplementInterface(typeof(IQueryHandler<,>))
                .ShouldNot().HaveDependencyOnAny(Ns.AppDbContext),
            "The read side must not see the write side's DbContext.");
    }

    [Fact]
    public void QueryHandlers_UseIReadDb_NotReadDbContext()
    {
        ArchAssert.Passes(
            Types.InAssembly(Assemblies.Application).That().ImplementInterface(typeof(IQueryHandler<,>))
                .ShouldNot().HaveDependencyOnAny(Ns.ReadDbContext),
            "IReadDb exposes only queryable sets; the DbContext would also offer Add/Remove/Database.");
    }

    [Fact]
    public void CommandHandlers_DoNotUse_ReadDbContext()
    {
        ArchAssert.Passes(
            Types.InAssembly(Assemblies.Application).That().ImplementInterface(typeof(ICommandHandler<,>))
                .ShouldNot().HaveDependencyOnAny(Ns.ReadDbContext, Ns.IReadDb),
            "Commands change state through the domain and AppDbContext only.");
    }

    [Fact]
    public void HandlerResults_AreNotDomainTypes()
    {
        // Queries project straight into DTOs (Product exists only inside the expression tree, never materialized);
        // commands return ids. An entity in a result would leak the domain model out of Application.
        var violations = Assemblies.Application.GetTypes()
            .SelectMany(t => t.GetInterfaces().Select(i => (Handler: t, Interface: i)))
            .Where(x => x.Interface.IsGenericType
                && (x.Interface.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)
                    || x.Interface.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)))
            .SelectMany(x => TypeUnwrap.All(x.Interface.GetGenericArguments()[1]).Select(r => (x.Handler, Result: r)))
            .Where(x => x.Result.Assembly == Assemblies.Domain)
            .Select(x => $"{x.Handler.Name} returns {x.Result.Name}")
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void Handlers_AreInternalAndSealed()
    {
        ArchAssert.Passes(
            Types.InAssembly(Assemblies.Application).That().ImplementInterface(typeof(IQueryHandler<,>))
                .Or().ImplementInterface(typeof(ICommandHandler<,>))
                .Should().NotBePublic().And().BeSealed(),
            "Handlers are reached only through their interfaces (registered in AddApplication).");
    }

    [Fact]
    public void Handlers_AreFound_SoTheRulesAboveAreNotVacuous()
    {
        var queries = Types.InAssembly(Assemblies.Application).That().ImplementInterface(typeof(IQueryHandler<,>)).GetTypes();
        var commands = Types.InAssembly(Assemblies.Application).That().ImplementInterface(typeof(ICommandHandler<,>)).GetTypes();

        Assert.Equal(2, queries.Count());
        Assert.Single(commands);
    }
}
