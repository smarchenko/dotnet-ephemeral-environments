namespace EphemeralEnvs.Api.Infrastructure;

/// <summary>CORS for the Web front-end: an explicit list of origins from <c>Cors:AllowedOrigins</c>, never <c>*</c>.</summary>
public static class CorsSetup
{
    public const string PolicyName = "AppOrigins";

    public const string ConfigKey = "Cors:AllowedOrigins";

    /// <summary>
    /// Comma-separated origins → trimmed, without a trailing <c>/</c> (browsers send <c>Origin</c> without it),
    /// empties dropped. <c>*</c> throws: every environment must list its origins explicitly.
    /// </summary>
    public static string[] ParseOrigins(string? raw)
    {
        var origins = (raw ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(origin => origin.TrimEnd('/'))
            .Where(origin => origin.Length > 0)
            .ToArray();

        if (origins.Contains("*"))
        {
            throw new InvalidOperationException($"{ConfigKey} must list explicit origins; '*' is not allowed.");
        }

        return origins;
    }
}
