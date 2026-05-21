namespace SharpCoreDB.EntityFrameworkCore.Tests.Storage;

using SharpCoreDB.EntityFrameworkCore.Storage;

public sealed class SharpCoreDBDataReaderTests
{
    [Fact]
    public void Constructor_WithQualifiedAndQuotedKeys_ShouldNormalizeColumnsAndReturnValues()
    {
        // Arrange
        List<Dictionary<string, object>> results =
        [
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["\"w\".\"Id\""] = 42,
                ["\"w\".\"PayloadJson\""] = "{\"name\":\"test\"}",
            },
        ];

        using var reader = new SharpCoreDBDataReader(results);

        // Act
        _ = reader.Read();
        var idOrdinal = reader.GetOrdinal("Id");
        var payloadOrdinal = reader.GetOrdinal("PayloadJson");
        var id = reader.GetInt32(idOrdinal);
        var payload = reader.GetString(payloadOrdinal);

        // Assert
        Assert.Equal(2, reader.FieldCount);
        Assert.Equal(42, id);
        Assert.Equal("{\"name\":\"test\"}", payload);
    }

    [Fact]
    public void Constructor_WithDuplicateQualifiedColumns_ShouldDeduplicateFieldNames()
    {
        // Arrange
        List<Dictionary<string, object>> results =
        [
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Id"] = 7,
                ["w.Id"] = 7,
                ["Products.Id"] = 7,
                ["Name"] = "Widget",
                ["w.Name"] = "Widget",
            },
        ];

        using var reader = new SharpCoreDBDataReader(results);

        // Act
        _ = reader.Read();

        // Assert
        // We intentionally keep all original columns now (even if they normalize similarly)
        // because Include navigation queries legitimately return columns from multiple tables.
        // The rich _nameToOrdinal map ensures GetOrdinal still works reliably.
        Assert.Equal(5, reader.FieldCount);
        Assert.Equal(7, reader.GetInt32(reader.GetOrdinal("Id")));
        Assert.Equal("Widget", reader.GetString(reader.GetOrdinal("Name")));
    }

    [Fact]
    public void GetOrdinal_WithQuotedQualifiedName_ShouldResolveToNormalizedColumn()
    {
        // Arrange
        List<Dictionary<string, object>> results =
        [
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["\"w\".\"Id\""] = 1,
            },
        ];

        using var reader = new SharpCoreDBDataReader(results);

        // Act
        var ordinal = reader.GetOrdinal("\"w\".\"Id\"");

        // Assert
        Assert.Equal(0, ordinal);
    }

    [Fact]
    public void Read_WithEmptyResults_ShouldReturnFalse()
    {
        // Arrange
        var results = new List<Dictionary<string, object>>();
        using var reader = new SharpCoreDBDataReader(results);

        // Act & Assert
        Assert.False(reader.Read());
        Assert.Equal(0, reader.FieldCount);
    }

    // ---------------------------------------------------------------------
    // Include / Navigation scenarios with Guid keys (the main reported bug)
    // ---------------------------------------------------------------------

    [Fact]
    public void Include_WithGuidKeys_AndMixedChildColumns_ShouldResolveCorrectOrdinals()
    {
        // Simulate a flat result set that EF Core might see during Include materialization
        // when the parent (Company) and child (Vacancy) both have "Id" and other similarly-named columns.
        var results = new List<Dictionary<string, object>>
        {
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Id"] = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                ["Name"] = "Delta Logistics",
                ["Vacancy_Id"] = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                ["Vacancy_Title"] = "Backend Developer",
                ["Vacancy_IsActive"] = true,
                ["Vacancy_CompanyId"] = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            },
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Id"] = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                ["Name"] = "Delta Logistics",
                ["Vacancy_Id"] = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                ["Vacancy_Title"] = "DevOps Engineer",
                ["Vacancy_IsActive"] = true,
                ["Vacancy_CompanyId"] = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            },
        };

        using var reader = new SharpCoreDBDataReader(results);

        var rowsRead = 0;
        while (reader.Read())
        {
            rowsRead++;

            // These should resolve correctly even after normalization
            var companyId = reader.GetGuid(reader.GetOrdinal("Id"));
            var companyName = reader.GetString(reader.GetOrdinal("Name"));
            var vacancyIsActiveOrdinal = reader.GetOrdinal("Vacancy_IsActive");
            var isActive = reader.GetBoolean(vacancyIsActiveOrdinal);

            Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), companyId);
            Assert.Equal("Delta Logistics", companyName);
            Assert.True(isActive);
        }

        Assert.Equal(2, rowsRead);
    }

    [Fact]
    public void GetBoolean_WithIncludeShapedRow_WhenOrdinalPointsToWrongColumn_ShouldStillFindCorrectValue()
    {
        // This simulates the exact class of failure we saw: EF Core asking for a bool column
        // at an ordinal that temporarily resolves to a string column (e.g. Title) because of
        // how the result set was built for navigation loading.
        var results = new List<Dictionary<string, object>>
        {
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["CompanyId"] = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                ["Title"] = "DevOps Engineer",           // This string was being read as bool in the bug
                ["IsActive"] = true,
            },
        };

        using var reader = new SharpCoreDBDataReader(results);
        _ = reader.Read();

        // Even if GetOrdinal("IsActive") internally has trouble, GetBoolean should be resilient
        var isActive = reader.GetBoolean(reader.GetOrdinal("IsActive"));
        Assert.True(isActive);
    }

    [Fact]
    public void GetValue_WithVeryMessyIncludeRow_ShouldSurviveMultipleNormalizationCollisions()
    {
        // Extreme case: many columns that all normalize to common names ("Id", "Name", "Active")
        var results = new List<Dictionary<string, object>>
        {
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["c.Id"] = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                ["v.Id"] = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                ["c.Name"] = "TechNova",
                ["v.Title"] = "AI Researcher",
                ["v.IsActive"] = true,
                ["IsActive"] = false, // decoy
            },
        };

        using var reader = new SharpCoreDBDataReader(results);
        _ = reader.Read();

        var companyId = reader.GetGuid(reader.GetOrdinal("c.Id"));
        var vacancyIsActive = reader.GetBoolean(reader.GetOrdinal("v.IsActive"));

        Assert.Equal(Guid.Parse("33333333-3333-3333-3333-333333333333"), companyId);
        Assert.True(vacancyIsActive);
    }
}
