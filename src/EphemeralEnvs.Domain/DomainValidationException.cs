namespace EphemeralEnvs.Domain;

/// <summary>A domain invariant was violated. <see cref="Field"/> names the offending property.</summary>
public sealed class DomainValidationException(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}
