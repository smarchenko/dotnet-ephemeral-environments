using EphemeralEnvs.Data.History;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EphemeralEnvs.Data.Configurations;

internal sealed class DbToolHistoryEntryConfiguration : IEntityTypeConfiguration<DbToolHistoryEntry>
{
    public void Configure(EntityTypeBuilder<DbToolHistoryEntry> builder)
    {
        builder.ToTable("__db_tool_history");

        builder.HasKey(e => e.Name);
        builder.Property(e => e.Name).HasColumnName("name").HasMaxLength(DbToolHistoryEntry.NameMaxLength).ValueGeneratedNever();
        builder.Property(e => e.Kind).HasColumnName("kind").IsRequired().HasMaxLength(DbToolHistoryEntry.KindMaxLength);
        builder.Property(e => e.AppliedAt).HasColumnName("applied_at");
    }
}
