using SharpCoreDB.Migration;
using SharpCoreDB.Services;
using SharpCoreDB.Storage.Hybrid;
using SharpCoreDB.Storage.Overflow;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Xunit;

namespace SharpCoreDB.Tests;

/// <summary>
/// Covers <see cref="AotJsonSerializer"/>, the single options factory the core assembly uses so that a call site
/// can resolve <c>JsonTypeInfo&lt;T&gt;</c> instead of the reflection-based overloads the AOT/trim analyzers flag
/// (IL2026 / IL3050). Two properties matter and both are asserted here: the types the core serializes have
/// metadata on the source-generated contexts the AOT arm installs, and the JSON produced through the shared
/// options is byte-identical to the serializer the call sites used before AOT-readiness — no file or wire format
/// may move for an AOT-readiness change.
/// </summary>
public sealed class AotJsonSerializerTests
{
    private static Dictionary<string, object> SampleValueBag() => new()
    {
        ["id"] = 42L,
        ["name"] = "Ada",
        ["ratio"] = 1.5,
        ["flag"] = true,
        ["missing"] = null!,
        ["when"] = new DateTime(2026, 9, 26, 12, 34, 56, DateTimeKind.Utc),
        ["blob"] = new byte[] { 1, 2, 3 },
    };

    /// <summary>
    /// The shared options must serialize a metadata value bag to exactly the bytes the parameterless
    /// <c>JsonSerializer.Serialize</c> overload produced before this change.
    /// </summary>
    [Fact]
    public void Serialize_ValueBag_IsByteIdenticalToDefaultSerializer()
    {
        var row = SampleValueBag();

        Assert.Equal(JsonSerializer.Serialize(row), AotJsonSerializer.Serialize(row));
    }

    /// <summary>
    /// The UTF-8 variant is what the single-file and columnar writers use, so it has to match as well.
    /// </summary>
    [Fact]
    public void SerializeToUtf8Bytes_ValueBag_IsByteIdenticalToDefaultSerializer()
    {
        var row = SampleValueBag();

        Assert.Equal(JsonSerializer.SerializeToUtf8Bytes(row), AotJsonSerializer.SerializeToUtf8Bytes(row));
    }

    /// <summary>
    /// Deserializing and re-serializing through the shared options must return the same JSON, which is what a
    /// metadata load followed by a save relies on.
    /// </summary>
    [Fact]
    public void Deserialize_SerializedValueBag_RoundTripsToIdenticalJson()
    {
        var json = JsonSerializer.Serialize(SampleValueBag());

        var bag = AotJsonSerializer.Deserialize<Dictionary<string, object>>(json);

        Assert.NotNull(bag);
        Assert.Equal(json, AotJsonSerializer.Serialize(bag));
    }

    /// <summary>
    /// The same round trip over the UTF-8 entry points used by the storage layer.
    /// </summary>
    [Fact]
    public void Deserialize_Utf8Bytes_RoundTripsToIdenticalJson()
    {
        var utf8 = JsonSerializer.SerializeToUtf8Bytes(SampleValueBag());

        var bag = AotJsonSerializer.Deserialize<Dictionary<string, object>>(utf8.AsSpan());

        Assert.NotNull(bag);
        Assert.Equal(utf8, AotJsonSerializer.SerializeToUtf8Bytes(bag));
    }

    /// <summary>
    /// The nesting the metadata file uses — a list of value bags — has to stay identical as well.
    /// </summary>
    [Fact]
    public void Serialize_TableList_IsByteIdenticalToDefaultSerializer()
    {
        var tables = new List<Dictionary<string, object>>
        {
            SampleValueBag(),
            new Dictionary<string, object> { ["id"] = 7L },
        };

        Assert.Equal(JsonSerializer.Serialize(tables), AotJsonSerializer.Serialize(tables));
    }

    /// <summary>
    /// Call sites that wrote indented metadata keep that formatting; the options factory must reproduce it.
    /// </summary>
    [Fact]
    public void CreateOptions_WriteIndented_MatchesIndentedDefaultSerializer()
    {
        var row = SampleValueBag();
        var options = AotJsonSerializer.CreateOptions(writeIndented: true);

        var indented = JsonSerializer.Serialize(row, options.GetTypeInfo<Dictionary<string, object>>());

        Assert.Equal(JsonSerializer.Serialize(row, new JsonSerializerOptions { WriteIndented = true }), indented);
        Assert.Contains("\n", indented, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every type the core requires the AOT arm to serve from the internal context must really have generated
    /// metadata; a missing declaration would only surface as a runtime failure on a trimmed publish.
    /// </summary>
    /// <param name="type">The declared state type.</param>
    [Theory]
    [InlineData(typeof(float[]))]
    [InlineData(typeof(FilePointer))]
    [InlineData(typeof(MigrationCheckpoint))]
    [InlineData(typeof(UserCredentials))]
    [InlineData(typeof(Dictionary<string, UserCredentials>))]
    [InlineData(typeof(Dictionary<string, TableMetadataExtended>))]
    public void AotJsonContext_ResolvesMetadataForInternalStateTypes(Type type)
    {
        Assert.NotNull(SharpCoreDBAotJsonContext.Default.GetTypeInfo(type));
    }

    /// <summary>
    /// The payload types the metadata and row paths serialize from the public context.
    /// </summary>
    /// <param name="type">The declared payload type.</param>
    [Theory]
    [InlineData(typeof(Dictionary<string, object>))]
    [InlineData(typeof(List<Dictionary<string, object>>))]
    [InlineData(typeof(string))]
    [InlineData(typeof(DateTime))]
    [InlineData(typeof(JsonElement))]
    public void PublicJsonContext_ResolvesMetadataForMetadataPayloadTypes(Type type)
    {
        Assert.NotNull(SharpCoreDBJsonContext.Default.GetTypeInfo(type));
    }

    /// <summary>
    /// The combined resolver the AOT arm installs must serve both contexts, so a type from either one resolves
    /// through the same mechanism the call sites use.
    /// </summary>
    [Fact]
    public void CombinedResolver_ServesBothContexts()
    {
        var options = new JsonSerializerOptions();
        var combined = JsonTypeInfoResolver.Combine(
            SharpCoreDBJsonContext.Default,
            SharpCoreDBAotJsonContext.Default);

        Assert.NotNull(combined.GetTypeInfo(typeof(Dictionary<string, object>), options));
        Assert.NotNull(combined.GetTypeInfo(typeof(FilePointer), options));
    }
}
