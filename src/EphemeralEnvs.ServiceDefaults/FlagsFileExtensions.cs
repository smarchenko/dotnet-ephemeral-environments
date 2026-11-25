using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace EphemeralEnvs.ServiceDefaults;

public static class FlagsFileExtensions
{
    /// <summary>Path of the flags file; relative paths are resolved against the content root (/app in the image).</summary>
    public const string PathKey = "FlagsFile";

    public const string DefaultPath = "config/flags.json";

    /// <summary>
    /// Optional JSON config file that is re-read when it changes, for switching feature flags without a restart.
    /// deploy/local mounts a host folder there; without the file nothing changes. Added last, so it wins over
    /// appsettings*.json and environment variables. Only values read per request follow it live (flags do;
    /// CORS, connection strings and other startup settings need a restart).
    /// </summary>
    public static TBuilder AddFlagsFile<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        var path = builder.Configuration[PathKey] ?? DefaultPath;
        if (!Path.IsPathRooted(path))
        {
            path = Path.Combine(builder.Environment.ContentRootPath, path);
        }

        builder.Configuration.AddJsonFile(path, optional: true, reloadOnChange: true);
        return builder;
    }
}
