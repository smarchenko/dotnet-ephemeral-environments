using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;

using EphemeralEnvs.Contracts;
using EphemeralEnvs.Domain;

using Microsoft.AspNetCore.Mvc;

namespace EphemeralEnvs.ArchitectureTests;

/// <summary>
/// Input contracts (<c>*Request</c>) validate at the API boundary with the same limits as the domain.
/// The domain is the source of truth; the contract attributes are a mirror checked here.
/// </summary>
public sealed class ContractTests
{
    [Fact]
    public void ProductNameAttribute_MatchesDomain()
    {
        Assert.Equal(Product.NameMaxLength, ProductNameAttribute.MaxLength);
        Assert.Equal(Product.NameMaxLength, new ProductNameAttribute().MaximumLength);
    }

    [Fact]
    public void PriceAttribute_MatchesDomain()
    {
        var price = new PriceAttribute();

        Assert.Equal(Product.MinPrice, Convert.ToDecimal(price.Minimum, CultureInfo.InvariantCulture));
        Assert.Equal(Product.MaxPrice, Convert.ToDecimal(price.Maximum, CultureInfo.InvariantCulture));
        Assert.Equal(Product.PriceScale, PriceAttribute.MaxDecimalPlaces);
    }

    [Fact]
    public void Requests_HaveNoUnboundedStringsOrDecimals()
    {
        // A new request with a bare `string NewName` fails here, whatever the property is called.
        var unbounded = RequestTypes()
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => (Type: t, Property: p)))
            .Where(x => !IsBounded(x.Type, x.Property))
            .Select(x => $"{x.Type.Name}.{x.Property.Name}")
            .ToArray();

        Assert.True(
            unbounded.Length == 0,
            "Every string needs [ProductName]/[StringLength]/[MaxLength], every decimal needs [Price]/[Range]. " +
            $"Unbounded: {string.Join(", ", unbounded)}");
    }

    [Fact]
    public void ControllerActions_TakeOnlyRequestContracts()
    {
        // Keeps the rule above honest: an input contract not named *Request would escape it.
        var violations = Assemblies.Api.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .SelectMany(m => m.GetParameters().Select(p => (Method: m, Parameter: p)))
            .Where(x => x.Parameter.ParameterType.Assembly == Assemblies.Contracts
                && !x.Parameter.ParameterType.Name.EndsWith("Request", StringComparison.Ordinal))
            .Select(x => $"{x.Method.DeclaringType!.Name}.{x.Method.Name}({x.Parameter.ParameterType.Name})")
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void ControllerActions_ReturnOnlyDtoContracts()
    {
        // Output side of the convention: Task<ActionResult<ProductDto[]>> → ProductDto must end with "Dto".
        var violations = ActionReturnContracts()
            .Where(x => !x.Contract.Name.EndsWith("Dto", StringComparison.Ordinal))
            .Select(x => $"{x.Method.DeclaringType!.Name}.{x.Method.Name} returns {x.Contract.Name}")
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void Contracts_AreFound_SoTheRulesAboveAreNotVacuous()
    {
        Assert.Contains(typeof(CreateProductRequest), RequestTypes());
        Assert.Contains(typeof(ProductDto), ActionReturnContracts().Select(x => x.Contract));
    }

    private static (MethodInfo Method, Type Contract)[] ActionReturnContracts() =>
        Assemblies.Api.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .SelectMany(m => TypeUnwrap.All(m.ReturnType).Select(c => (Method: m, Contract: c)))
            .Where(x => x.Contract.Assembly == Assemblies.Contracts)
            .ToArray();

    private static Type[] RequestTypes() =>
        Assemblies.Contracts.GetExportedTypes()
            .Where(t => t.IsClass && t.Name.EndsWith("Request", StringComparison.Ordinal))
            .ToArray();

    private static bool IsBounded(Type type, PropertyInfo property)
    {
        var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

        // Positional records keep the attributes on the constructor parameter, not on the property.
        var attributes = property.GetCustomAttributes<ValidationAttribute>()
            .Concat(type.GetConstructors()
                .SelectMany(c => c.GetParameters())
                .Where(p => p.Name == property.Name)
                .SelectMany(p => p.GetCustomAttributes<ValidationAttribute>()))
            .ToArray();

        if (propertyType == typeof(string))
        {
            return attributes.Any(a => a is StringLengthAttribute or MaxLengthAttribute);
        }

        if (propertyType == typeof(decimal))
        {
            return attributes.Any(a => a is RangeAttribute);
        }

        return true;
    }
}
