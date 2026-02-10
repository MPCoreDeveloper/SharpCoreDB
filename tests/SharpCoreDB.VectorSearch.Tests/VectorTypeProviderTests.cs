namespace SharpCoreDB.VectorSearch.Tests;

public class VectorTypeProviderTests
{
    private readonly VectorTypeProvider _provider = new(new VectorSearchOptions());

    [Theory]
    [InlineData("VECTOR", true)]
    [InlineData("VECTOR(1536)", true)]
    [InlineData("vector(128)", true)]
    [InlineData("INTEGER", false)]
    [InlineData("TEXT", false)]
    public void CanHandle_ReturnsExpected(string typeName, bool expected)
    {
        Assert.Equal(expected, _provider.CanHandle(typeName));
    }

    [Fact]
    public void GetStorageType_Vector1536_ReturnsVectorWithDimensions()
    {
        // Act
        var type = _provider.GetStorageType("VECTOR(1536)", out int dims);

        // Assert
        Assert.Equal(DataType.Vector, type);
        Assert.Equal(1536, dims);
    }

    [Fact]
    public void GetStorageType_VectorNoDims_ReturnsDynamicZero()
    {
        // Act
        var type = _provider.GetStorageType("VECTOR", out int dims);

        // Assert
        Assert.Equal(DataType.Vector, type);
        Assert.Equal(0, dims);
    }

    [Fact]
    public void GetStorageType_ExceedsMaxDimensions_Throws()
    {
        // Arrange — default max is 4096
        Assert.Throws<ArgumentException>(() =>
            _provider.GetStorageType("VECTOR(10000)", out _));
    }

    [Fact]
    public void Serialize_FloatArray_RoundTrips()
    {
        // Arrange
        float[] original = [0.1f, 0.2f, 0.3f];

        // Act
        byte[] bytes = _provider.Serialize(original, 3);
        var result = (float[])_provider.Deserialize(bytes, 3);

        // Assert
        Assert.Equal(original, result);
    }

    [Fact]
    public void Serialize_JsonString_RoundTrips()
    {
        // Arrange
        string json = "[1.0, 2.0, 3.0]";

        // Act
        byte[] bytes = _provider.Serialize(json, 3);
        var result = (float[])_provider.Deserialize(bytes, 3);

        // Assert
        Assert.Equal(3, result.Length);
        Assert.Equal(1.0f, result[0], 1e-5f);
    }

    [Fact]
    public void Serialize_DimensionMismatch_Throws()
    {
        // Arrange — 3-element vector for 5-dimension column
        float[] vec = [1.0f, 2.0f, 3.0f];

        // Act & Assert
        Assert.Throws<ArgumentException>(() => _provider.Serialize(vec, 5));
    }

    [Fact]
    public void Deserialize_DimensionMismatch_Throws()
    {
        // Arrange — serialize 3 dims, deserialize expecting 5
        float[] vec = [1.0f, 2.0f, 3.0f];
        byte[] bytes = _provider.Serialize(vec, 3);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => _provider.Deserialize(bytes, 5));
    }

    [Fact]
    public void GetStorageType_InvalidDimensions_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            _provider.GetStorageType("VECTOR(abc)", out _));
    }

    [Fact]
    public void GetStorageType_CustomMaxDimensions_Enforced()
    {
        // Arrange
        var options = new VectorSearchOptions { MaxDimensions = 128 };
        var provider = new VectorTypeProvider(options);

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            provider.GetStorageType("VECTOR(256)", out _));

        // This should succeed
        var type = provider.GetStorageType("VECTOR(128)", out int dims);
        Assert.Equal(128, dims);
    }
}
