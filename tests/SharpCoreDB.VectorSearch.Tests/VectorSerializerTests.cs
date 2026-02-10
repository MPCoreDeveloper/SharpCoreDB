namespace SharpCoreDB.VectorSearch.Tests;

public class VectorSerializerTests
{
    [Fact]
    public void Serialize_RoundTrip_PreservesData()
    {
        // Arrange
        float[] original = [1.0f, 2.0f, 3.0f, -4.5f, 0.0f];

        // Act
        byte[] serialized = VectorSerializer.Serialize(original);
        float[] deserialized = VectorSerializer.Deserialize(serialized);

        // Assert
        Assert.Equal(original, deserialized);
    }

    [Fact]
    public void Serialize_ContainsHeader()
    {
        // Arrange
        float[] vector = [1.0f, 2.0f, 3.0f];

        // Act
        byte[] serialized = VectorSerializer.Serialize(vector);

        // Assert
        Assert.True(serialized.Length >= 12); // VEC header = 12 bytes
        Assert.Equal((byte)'V', serialized[0]);
        Assert.Equal((byte)'E', serialized[1]);
        Assert.Equal((byte)'C', serialized[2]);
        Assert.Equal(0, serialized[3]);
    }

    [Fact]
    public void GetDimensions_ReturnsCorrectCount()
    {
        // Arrange
        float[] vector = [1.0f, 2.0f, 3.0f, 4.0f, 5.0f];
        byte[] serialized = VectorSerializer.Serialize(vector);

        // Act
        int dims = VectorSerializer.GetDimensions(serialized);

        // Assert
        Assert.Equal(5, dims);
    }

    [Fact]
    public void DeserializeSpan_ZeroCopy_MatchesValues()
    {
        // Arrange
        float[] original = [0.1f, 0.2f, 0.3f];
        byte[] serialized = VectorSerializer.Serialize(original);

        // Act
        ReadOnlySpan<float> span = VectorSerializer.DeserializeSpan(serialized);

        // Assert
        Assert.Equal(original.Length, span.Length);
        for (int i = 0; i < original.Length; i++)
        {
            Assert.Equal(original[i], span[i]);
        }
    }

    [Fact]
    public void FromJson_ParsesFloatArray()
    {
        // Arrange
        string json = "[0.1, 0.2, 0.3]";

        // Act
        float[] result = VectorSerializer.FromJson(json);

        // Assert
        Assert.Equal(3, result.Length);
        Assert.Equal(0.1f, result[0], 0.001f);
        Assert.Equal(0.2f, result[1], 0.001f);
        Assert.Equal(0.3f, result[2], 0.001f);
    }

    [Fact]
    public void ToJson_ProducesValidJsonArray()
    {
        // Arrange
        float[] vector = [1.0f, 2.5f, -3.0f];

        // Act
        string json = VectorSerializer.ToJson(vector);

        // Assert
        Assert.StartsWith("[", json);
        Assert.EndsWith("]", json);
        float[] parsed = VectorSerializer.FromJson(json);
        Assert.Equal(vector, parsed);
    }

    [Fact]
    public void Deserialize_RawBytes_FallsBackGracefully()
    {
        // Arrange — raw float bytes without header
        float[] original = [1.0f, 2.0f];
        byte[] rawBytes = new byte[original.Length * sizeof(float)];
        Buffer.BlockCopy(original, 0, rawBytes, 0, rawBytes.Length);

        // Act
        float[] result = VectorSerializer.Deserialize(rawBytes);

        // Assert
        Assert.Equal(original, result);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(128)]
    [InlineData(1536)]
    public void Serialize_VariousDimensions_RoundTrips(int dimensions)
    {
        // Arrange
        var rng = new Random(42);
        float[] vector = new float[dimensions];
        for (int i = 0; i < dimensions; i++)
            vector[i] = (float)(rng.NextDouble() * 2 - 1);

        // Act
        byte[] serialized = VectorSerializer.Serialize(vector);
        float[] deserialized = VectorSerializer.Deserialize(serialized);

        // Assert
        Assert.Equal(vector, deserialized);
        Assert.Equal(dimensions, VectorSerializer.GetDimensions(serialized));
    }

    [Fact]
    public void Serialize_EmptyVector_RoundTrips()
    {
        // Arrange
        float[] empty = [];

        // Act
        byte[] serialized = VectorSerializer.Serialize(empty);
        float[] deserialized = VectorSerializer.Deserialize(serialized);

        // Assert
        Assert.Empty(deserialized);
    }

    [Fact]
    public void FromJson_NullOrWhitespace_Throws()
    {
        Assert.Throws<ArgumentException>(() => VectorSerializer.FromJson(""));
        Assert.Throws<ArgumentException>(() => VectorSerializer.FromJson("  "));
    }
}
