namespace SharpCoreDB.Tests.Sql;

using SharpCoreDB.Services;

public sealed class ProjectionKeyNameNormalizerTests
{
    [Theory]
    [InlineData("name", "name")]
    [InlineData("  name  ", "name")]
    [InlineData("\"name\"", "name")]
    [InlineData("[name]", "name")]
    [InlineData("`name`", "name")]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void NormalizeIdentifier_ShouldTrimAndUnquote(string? input, string expected)
    {
        var normalized = ProjectionKeyNameNormalizer.NormalizeIdentifier(input);

        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void BuildDuplicateCounts_ShouldNormalizeAndCountCaseInsensitively()
    {
        var counts = ProjectionKeyNameNormalizer.BuildDuplicateCounts(
        [
            "id",
            "ID",
            " [id] ",
            "name",
            "\"name\"",
            "",
            "   "
        ]);

        Assert.Equal(2, counts.Count);
        Assert.Equal(3, counts["id"]);
        Assert.Equal(2, counts["name"]);
    }

    [Fact]
    public void ResolveOutputName_WithExplicitAlias_ShouldPreferAlias()
    {
        var counts = ProjectionKeyNameNormalizer.BuildDuplicateCounts(["id", "id"]);

        var output = ProjectionKeyNameNormalizer.ResolveOutputName(
            baseName: "id",
            qualifier: "o",
            explicitAlias: " [order_id] ",
            duplicateCounts: counts);

        Assert.Equal("order_id", output);
    }

    [Fact]
    public void ResolveOutputName_UnaliasedUnique_ShouldReturnBareName()
    {
        var counts = ProjectionKeyNameNormalizer.BuildDuplicateCounts(["id", "name"]);

        var output = ProjectionKeyNameNormalizer.ResolveOutputName(
            baseName: "name",
            qualifier: "c",
            explicitAlias: null,
            duplicateCounts: counts);

        Assert.Equal("name", output);
    }

    [Fact]
    public void ResolveOutputName_UnaliasedDuplicateWithQualifier_ShouldReturnQualifiedName()
    {
        var counts = ProjectionKeyNameNormalizer.BuildDuplicateCounts(["id", "id"]);

        var output = ProjectionKeyNameNormalizer.ResolveOutputName(
            baseName: "id",
            qualifier: "[o]",
            explicitAlias: null,
            duplicateCounts: counts);

        Assert.Equal("o.id", output);
    }

    [Fact]
    public void ResolveOutputName_UnaliasedDuplicateWithoutQualifier_ShouldReturnBareName()
    {
        var counts = ProjectionKeyNameNormalizer.BuildDuplicateCounts(["id", "id"]);

        var output = ProjectionKeyNameNormalizer.ResolveOutputName(
            baseName: "id",
            qualifier: null,
            explicitAlias: null,
            duplicateCounts: counts);

        Assert.Equal("id", output);
    }

    [Fact]
    public void ThrowIfExplicitAliasCollisions_ShouldThrow_WhenNormalizedAliasesCollide()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ProjectionKeyNameNormalizer.ThrowIfExplicitAliasCollisions([
                " [same_key] ",
                "\"same_key\"",
                "other"
            ]));

        Assert.Contains("Duplicate explicit column alias", ex.Message, StringComparison.Ordinal);
        Assert.Contains("same_key", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ThrowIfExplicitAliasCollisions_ShouldNotThrow_WhenAliasesAreUnique()
    {
        ProjectionKeyNameNormalizer.ThrowIfExplicitAliasCollisions([
            "order_id",
            "payment_id",
            null,
            "   "
        ]);
    }
}
