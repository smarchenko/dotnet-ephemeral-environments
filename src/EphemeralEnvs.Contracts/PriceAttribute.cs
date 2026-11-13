using System.ComponentModel.DataAnnotations;

namespace EphemeralEnvs.Contracts;

/// <summary>
/// A price: from <see cref="MinValue"/> to <see cref="MaxValue"/> (fits <c>numeric(18,2)</c>),
/// at most <see cref="MaxDecimalPlaces"/> decimal places. Mirrors the domain limits in <c>Product</c>
/// (checked by ContractTests). Derives from <see cref="RangeAttribute"/> so OpenAPI still emits min/max.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.Field)]
public sealed class PriceAttribute : RangeAttribute
{
    public const string MinValue = "0.01";
    public const string MaxValue = "9999999999999999.99";
    public const int MaxDecimalPlaces = 2;

    public PriceAttribute()
        : base(typeof(decimal), MinValue, MaxValue)
    {
        ParseLimitsInInvariantCulture = true;
        ConvertValueInInvariantCulture = true;
    }

    public override bool IsValid(object? value) => base.IsValid(value) && !HasTooManyDecimals(value);

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        ArgumentNullException.ThrowIfNull(validationContext);

        if (HasTooManyDecimals(value))
        {
            return new ValidationResult(
                $"The field {validationContext.DisplayName} must have at most {MaxDecimalPlaces} decimal places.",
                validationContext.MemberName is { } member ? [member] : null);
        }

        return base.IsValid(value, validationContext);
    }

    // Trailing zeros are fine: 1.500 == 1.50.
    private static bool HasTooManyDecimals(object? value) =>
        value is decimal price && decimal.Round(price, MaxDecimalPlaces) != price;
}
