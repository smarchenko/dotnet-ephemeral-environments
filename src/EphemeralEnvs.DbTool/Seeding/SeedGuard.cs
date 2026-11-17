namespace EphemeralEnvs.DbTool.Seeding;

/// <summary>
/// Safety net against demo data in production: seeding needs an explicit, non-production environment name.
/// The real protection is that production pipelines never run the seed step.
/// </summary>
internal static class SeedGuard
{
    private static readonly string[] ProductionNames = ["production", "prod"];

    public static bool IsAllowed(string? environmentName)
    {
        if (string.IsNullOrWhiteSpace(environmentName))
        {
            return false;
        }

        var name = environmentName.Trim();
        return !ProductionNames.Any(production => string.Equals(production, name, StringComparison.OrdinalIgnoreCase));
    }
}
