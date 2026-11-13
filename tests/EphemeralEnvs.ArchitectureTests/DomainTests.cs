using System.Reflection;

namespace EphemeralEnvs.ArchitectureTests;

public sealed class DomainTests
{
    [Fact]
    public void Entities_HaveNoPublicSetters()
    {
        // State changes go through constructors/methods that enforce invariants, never through setters.
        var publicSetters = Assemblies.Domain.GetExportedTypes()
            .Where(t => t.IsClass && !typeof(Exception).IsAssignableFrom(t))
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Where(p => p.SetMethod?.IsPublic == true)
            .Select(p => $"{p.DeclaringType!.Name}.{p.Name}")
            .ToArray();

        Assert.Empty(publicSetters);
    }
}
