using EphemeralEnvs.Domain;

namespace EphemeralEnvs.DbTool.Seeding.Sets;

/// <summary>First demo products for QA. Applied sets are never edited: new demo data goes into a new set.</summary>
internal sealed class Seed0001Products : ISeedSet
{
    // Fixed ids: QA, tests and later sets (e.g. orders) can refer to them.
    public static readonly Guid NotebookA5 = Guid.Parse("0196a000-0000-7000-8000-000000000001");
    public static readonly Guid BallpointPen = Guid.Parse("0196a000-0000-7000-8000-000000000002");
    public static readonly Guid DeskLamp = Guid.Parse("0196a000-0000-7000-8000-000000000003");
    public static readonly Guid UsbCCable = Guid.Parse("0196a000-0000-7000-8000-000000000004");
    public static readonly Guid CoffeeMug = Guid.Parse("0196a000-0000-7000-8000-000000000005");

    private static readonly DateTimeOffset Created = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

    public string Name => "0001-products";

    public IEnumerable<object> Build() =>
    [
        Product.CreateSeeded(NotebookA5, "Notebook A5", 4.50m, Created),
        Product.CreateSeeded(BallpointPen, "Ballpoint pen", 1.20m, Created.AddMinutes(1)),
        Product.CreateSeeded(DeskLamp, "Desk lamp", 34.99m, Created.AddMinutes(2)),
        Product.CreateSeeded(UsbCCable, "USB-C cable 1 m", 9.90m, Created.AddMinutes(3)),
        Product.CreateSeeded(CoffeeMug, "Coffee mug", 12.00m, Created.AddMinutes(4)),
    ];
}
