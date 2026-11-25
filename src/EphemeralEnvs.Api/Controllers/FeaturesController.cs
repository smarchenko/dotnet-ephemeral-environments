using System.Text.Json;

using Microsoft.AspNetCore.Mvc;
using Microsoft.FeatureManagement;

namespace EphemeralEnvs.Api.Controllers;

/// <summary>Client-allowed flags for the browser widget: {"productStats":true}. Only <see cref="ClientFeatureFlags"/>.</summary>
[ApiController]
[Route("features")]
public sealed class FeaturesController(IFeatureManager features) : ControllerBase
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<Dictionary<string, bool>> Get()
    {
        var result = new Dictionary<string, bool>();
        foreach (var name in ClientFeatureFlags.Names)
        {
            // Dictionary keys are not camelCased by the serializer; do it here to match the rest of the JSON.
            result[JsonNamingPolicy.CamelCase.ConvertName(name)] = await features.IsEnabledAsync(name);
        }

        return result;
    }
}
