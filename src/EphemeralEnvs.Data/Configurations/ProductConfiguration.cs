using EphemeralEnvs.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EphemeralEnvs.Data.Configurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(p => p.Name).HasColumnName("name").IsRequired().HasMaxLength(200);
        builder.Property(p => p.Price).HasColumnName("price").HasColumnType("numeric(18,2)");
        builder.Property(p => p.CreatedAt).HasColumnName("created_at");
    }
}
