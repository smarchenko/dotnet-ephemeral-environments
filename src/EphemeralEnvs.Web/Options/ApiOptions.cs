namespace EphemeralEnvs.Web.Options;

/// <summary>Section <c>Api</c>: where the API is, as seen from the server and from the browser.</summary>
public sealed class ApiOptions
{
    public const string SectionName = "Api";

    /// <summary>Server-side calls (inside the Docker network, e.g. <c>http://api:8080</c>).</summary>
    public Uri? InternalBaseUrl { get; set; }

    /// <summary>
    /// Browser calls from <c>widget.js</c> (e.g. <c>http://localhost:5081</c>). Written into a meta tag at runtime,
    /// so the image has no build-time config.
    /// </summary>
    public Uri? PublicBaseUrl { get; set; }

    /// <summary>
    /// Timeout of a server-side call. Short on purpose: the page waits for it, and a hanging API must not pile up
    /// requests in the Web. Raise it for a slow environment via <c>Api__TimeoutSeconds</c>.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 5;

    public static bool IsValid(ApiOptions options) =>
        options.InternalBaseUrl is { IsAbsoluteUri: true }
        && options.PublicBaseUrl is { IsAbsoluteUri: true }
        && options.TimeoutSeconds > 0;
}
