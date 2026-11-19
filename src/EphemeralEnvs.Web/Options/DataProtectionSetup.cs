using Microsoft.AspNetCore.DataProtection;

namespace EphemeralEnvs.Web.Options;

/// <summary>
/// Data Protection keys encrypt the antiforgery token and cookie (later also auth cookies, TempData).
/// Several Web replicas must share them, or a form rendered by one replica fails (400) when posted to another.
/// </summary>
public static class DataProtectionSetup
{
    public const string KeysPathKey = "DataProtection:KeysPath";

    /// <summary>Same name on every replica: keys are isolated per application name.</summary>
    public const string ApplicationName = "EphemeralEnvs.Web";

    /// <summary>
    /// <c>DataProtection__KeysPath</c> set → keys in that directory (in a container: a volume shared by the replicas);
    /// unset or empty → ASP.NET Core defaults (in a container: inside the container, different per replica).
    /// </summary>
    public static IHostApplicationBuilder AddWebDataProtection(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var keysPath = builder.Configuration[KeysPathKey];
        if (!string.IsNullOrWhiteSpace(keysPath))
        {
            builder.Services.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
                .SetApplicationName(ApplicationName);
        }

        return builder;
    }
}
