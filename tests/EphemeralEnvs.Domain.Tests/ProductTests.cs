namespace EphemeralEnvs.Domain.Tests;

public sealed class ProductTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_ValidValues_SetsProperties()
    {
        var product = new Product("Pen", 1.50m, Now);

        Assert.NotEqual(Guid.Empty, product.Id);
        Assert.Equal("Pen", product.Name);
        Assert.Equal(1.50m, product.Price);
        Assert.Equal(Now, product.CreatedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankName_Throws(string? name)
    {
        var ex = Assert.Throws<DomainValidationException>(() => new Product(name!, 1m, Now));

        Assert.Equal(nameof(Product.Name), ex.Field);
    }

    [Fact]
    public void Create_NameOfMaxLength_IsAllowed()
    {
        var product = new Product(new string('a', Product.NameMaxLength), 1m, Now);

        Assert.Equal(200, product.Name.Length);
    }

    [Fact]
    public void Create_NameLongerThanMax_Throws()
    {
        var ex = Assert.Throws<DomainValidationException>(() => new Product(new string('a', 201), 1m, Now));

        Assert.Equal(nameof(Product.Name), ex.Field);
    }

    [Theory]
    [InlineData("0.01")]
    [InlineData("9999999999999999.99")]
    public void Create_PriceAtBounds_IsAllowed(string price)
    {
        var value = decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture);

        var product = new Product("Pen", value, Now);

        Assert.Equal(value, product.Price);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.009")]
    [InlineData("10000000000000000")]
    public void Create_PriceOutOfRange_Throws(string price)
    {
        var value = decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture);

        var ex = Assert.Throws<DomainValidationException>(() => new Product("Pen", value, Now));

        Assert.Equal(nameof(Product.Price), ex.Field);
    }

    [Theory]
    [InlineData("1.005")]
    [InlineData("0.011")]
    [InlineData("12.3456")]
    public void Create_PriceWithMoreThanTwoDecimals_Throws(string price)
    {
        // numeric(18,2) would silently round it, so the stored price would differ from the requested one.
        var value = decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture);

        var ex = Assert.Throws<DomainValidationException>(() => new Product("Pen", value, Now));

        Assert.Equal(nameof(Product.Price), ex.Field);
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("1.50")]
    [InlineData("1.500")]
    public void Create_PriceWithTrailingZeros_IsAllowed(string price)
    {
        // 1.500 is the same value as 1.50: only significant decimal places count.
        var value = decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture);

        var product = new Product("Pen", value, Now);

        Assert.Equal(1.5m, product.Price);
    }
}
