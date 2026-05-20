using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SharpCoreDB.EntityFrameworkCore.Query;

namespace SharpCoreDB.EntityFrameworkCore.Infrastructure;

/// <summary>
/// Customizes the EF Core model for SharpCoreDB.
/// </summary>
public sealed class SharpCoreDBModelCustomizer(ModelCustomizerDependencies dependencies)
    : RelationalModelCustomizer(dependencies)
{
    /// <inheritdoc />
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        modelBuilder
            .HasDbFunction(typeof(SharpCoreDBDbFunctionsExtensions)
                .GetMethod(nameof(SharpCoreDBDbFunctionsExtensions.GraphTraverse))!)
            .HasName("GRAPH_TRAVERSE");

        // Automatically treat integer/long primary keys as server-generated.
        // This is the standard pattern used by the SQLite, MySQL, and PostgreSQL providers
        // so that "HasKey + int property" produces a generated value on insert.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var pk = entityType.FindPrimaryKey();
            if (pk is null)
                continue;

            foreach (var property in pk.Properties)
            {
                if ((property.ClrType == typeof(int) || property.ClrType == typeof(long)) &&
                    property.ValueGenerated == ValueGenerated.Never)
                {
                    modelBuilder.Entity(entityType.ClrType)
                        .Property(property.Name)
                        .ValueGeneratedOnAdd();
                }
            }
        }

        // Harden DateTime support: convert DateTime <-> ISO-8601 string for storage.
        // This ensures reliable comparisons and avoids coercion/NRE issues in the provider.
        var dateTimeConverter = new ValueConverter<DateTime, string>(
            v => v.ToUniversalTime().ToString("o"),
            v => DateTime.Parse(v, null, System.Globalization.DateTimeStyles.RoundtripKind));

        var dateTimeOffsetConverter = new ValueConverter<DateTimeOffset, string>(
            v => v.UtcDateTime.ToString("o"),
            v => DateTimeOffset.Parse(v, null, System.Globalization.DateTimeStyles.RoundtripKind));

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTime) && property.GetValueConverter() is null)
                {
                    modelBuilder.Entity(entityType.ClrType)
                        .Property(property.Name)
                        .HasConversion(dateTimeConverter)
                        .HasColumnType("TEXT"); // Reliable TEXT (ISO-8601) storage like SQLite
                }
                else if (property.ClrType == typeof(DateTimeOffset) && property.GetValueConverter() is null)
                {
                    modelBuilder.Entity(entityType.ClrType)
                        .Property(property.Name)
                        .HasConversion(dateTimeOffsetConverter)
                        .HasColumnType("TEXT");
                }
            }
        }
    }
}
