namespace EphemeralEnvs.Api.Infrastructure;

/// <summary>Section <c>App</c>: facts about the running environment (shown in the OpenAPI description).</summary>
public sealed class AppOptions
{
    public const string SectionName = "App";

    /// <summary>For a preview environment, the branch slug (e.g. <c>pr-42</c>); <c>local</c> on a dev machine.</summary>
    public string EnvironmentName { get; set; } = "unknown";
}
