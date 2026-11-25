using System.Text.RegularExpressions;

using Microsoft.AspNetCore.OpenApi;
using Microsoft.FeatureManagement;
using Microsoft.FeatureManagement.Mvc;
using Microsoft.OpenApi;

namespace EphemeralEnvs.Api.Infrastructure;

/// <summary>
/// Drops from the OpenAPI document every operation whose [FeatureGate] is closed, so Swagger shows only what answers.
/// Flags come from configuration fixed at container start, so the doc and the gate agree.
/// </summary>
public sealed partial class FeatureGateDocumentTransformer(IFeatureManager features) : IOpenApiDocumentTransformer
{
    public async Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        if (document.Paths is null)
        {
            return;
        }

        foreach (var description in context.DescriptionGroups.SelectMany(group => group.Items))
        {
            var gates = description.ActionDescriptor.EndpointMetadata.OfType<FeatureGateAttribute>().ToList();
            if (gates.Count == 0 || await IsOpenAsync(gates) || description.HttpMethod is null)
            {
                continue;
            }

            var path = "/" + RouteConstraint().Replace(description.RelativePath ?? string.Empty, "{$1}");
            if (document.Paths.TryGetValue(path, out var item) && item.Operations is not null)
            {
                item.Operations.Remove(new HttpMethod(description.HttpMethod));
                if (item.Operations.Count == 0)
                {
                    document.Paths.Remove(path);
                }
            }
        }
    }

    // Same rules as the gate filter: every attribute must pass; each one is All/Any over its flags, optionally negated.
    private async Task<bool> IsOpenAsync(IEnumerable<FeatureGateAttribute> gates)
    {
        foreach (var gate in gates)
        {
            var states = new List<bool>();
            foreach (var feature in gate.Features)
            {
                states.Add(await features.IsEnabledAsync(feature));
            }

            var open = gate.RequirementType == RequirementType.All ? states.All(s => s) : states.Any(s => s);
            if (open == gate.Negate)
            {
                return false;
            }
        }

        return true;
    }

    // "products/{id:guid}" -> "products/{id}", as the OpenAPI generator writes path keys.
    [GeneratedRegex(@"\{([^}:=?]+)[^}]*\}")]
    private static partial Regex RouteConstraint();
}
