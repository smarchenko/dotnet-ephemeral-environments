namespace EphemeralEnvs.Domain;

public sealed class Product
{
    /// <summary>Maximum length of <see cref="Name"/> (column <c>varchar(200)</c>).</summary>
    public const int NameMaxLength = 200;

    /// <summary>Total digits of <see cref="Price"/> (column <c>numeric(18,2)</c>).</summary>
    public const int PricePrecision = 18;

    /// <summary>Digits after the decimal point of <see cref="Price"/>.</summary>
    public const int PriceScale = 2;

    /// <summary>Smallest allowed <see cref="Price"/>.</summary>
    public const decimal MinPrice = 0.01m;

    /// <summary>Largest value that fits <c>numeric(18,2)</c>.</summary>
    public const decimal MaxPrice = 9_999_999_999_999_999.99m;

    public Product(string name, decimal price, DateTimeOffset createdAt)
    {
        EnsureValidName(name);
        EnsureValidPrice(price);

        Id = Guid.CreateVersion7();
        Name = name;
        Price = price;
        CreatedAt = createdAt;
    }

    // Used by EF Core when materializing entities.
    private Product()
    {
        Name = string.Empty;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public decimal Price { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    private static void EnsureValidName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainValidationException(nameof(Name), "Name is required.");
        }

        if (name.Length > NameMaxLength)
        {
            throw new DomainValidationException(nameof(Name), $"Name must be at most {NameMaxLength} characters.");
        }
    }

    private static void EnsureValidPrice(decimal price)
    {
        if (price is < MinPrice or > MaxPrice)
        {
            throw new DomainValidationException(nameof(Price), $"Price must be between {MinPrice} and {MaxPrice}.");
        }

        // numeric(18,2) would silently round extra decimals (1.005 → 1.01). Trailing zeros (1.500) are fine.
        if (decimal.Round(price, PriceScale) != price)
        {
            throw new DomainValidationException(nameof(Price), $"Price must have at most {PriceScale} decimal places.");
        }
    }
}
