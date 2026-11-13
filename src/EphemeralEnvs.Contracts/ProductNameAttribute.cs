using System.ComponentModel.DataAnnotations;

namespace EphemeralEnvs.Contracts;

/// <summary>
/// A product name: required, not whitespace-only, at most <see cref="MaxLength"/> characters.
/// Mirrors <c>Product.NameMaxLength</c> in the domain (checked by ContractTests).
/// Derives from <see cref="StringLengthAttribute"/> so OpenAPI still emits <c>maxLength</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.Field)]
public sealed class ProductNameAttribute : StringLengthAttribute
{
    public const int MaxLength = 200;

    public ProductNameAttribute()
        : base(MaxLength)
    {
    }

    public override bool IsValid(object? value) =>
        value is string name && !string.IsNullOrWhiteSpace(name) && base.IsValid(value);

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        ArgumentNullException.ThrowIfNull(validationContext);

        if (value is not string name || string.IsNullOrWhiteSpace(name))
        {
            return new ValidationResult(
                $"The {validationContext.DisplayName} field is required.",
                validationContext.MemberName is { } member ? [member] : null);
        }

        return base.IsValid(value, validationContext);
    }
}
