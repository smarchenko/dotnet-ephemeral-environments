namespace EphemeralEnvs.Domain;

public sealed class Product
{
    public Product(string name, decimal price, DateTimeOffset createdAt)
    {
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
}
