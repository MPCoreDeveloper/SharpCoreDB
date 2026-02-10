namespace SharpCoreDB.VectorSearch.Tests;

public class VectorFunctionProviderTests
{
    private readonly VectorFunctionProvider _provider = new();

    [Theory]
    [InlineData("VEC_DISTANCE_COSINE", true)]
    [InlineData("VEC_DISTANCE_L2", true)]
    [InlineData("VEC_DISTANCE_DOT", true)]
    [InlineData("VEC_FROM_FLOAT32", true)]
    [InlineData("VEC_TO_JSON", true)]
    [InlineData("VEC_NORMALIZE", true)]
    [InlineData("VEC_DIMENSIONS", true)]
    [InlineData("UNKNOWN_FUNC", false)]
    [InlineData("SUM", false)]
    public void CanHandle_ReturnsExpected(string functionName, bool expected)
    {
        Assert.Equal(expected, _provider.CanHandle(functionName));
    }

    [Fact]
    public void GetFunctionNames_ReturnsAllSeven()
    {
        var names = _provider.GetFunctionNames();
        Assert.Equal(7, names.Count);
    }

    [Fact]
    public void VecDistanceCosine_IdenticalVectors_ReturnsZero()
    {
        // Arrange
        float[] vec = [1.0f, 0.0f, 0.0f];
        var args = new List<object?> { vec, vec };

        // Act
        var result = _provider.Evaluate("VEC_DISTANCE_COSINE", args);

        // Assert
        Assert.IsType<float>(result);
        Assert.True((float)result! < 1e-5f);
    }

    [Fact]
    public void VecDistanceL2_KnownValues_ReturnsCorrect()
    {
        // Arrange — distance = 5
        float[] a = [0.0f, 0.0f];
        float[] b = [3.0f, 4.0f];
        var args = new List<object?> { a, b };

        // Act
        var result = _provider.Evaluate("VEC_DISTANCE_L2", args);

        // Assert
        Assert.Equal(5.0f, (float)result!, 1e-4f);
    }

    [Fact]
    public void VecFromFloat32_JsonString_ReturnsFloatArray()
    {
        // Arrange
        var args = new List<object?> { "[0.1, 0.2, 0.3]" };

        // Act
        var result = _provider.Evaluate("VEC_FROM_FLOAT32", args);

        // Assert
        var arr = Assert.IsType<float[]>(result);
        Assert.Equal(3, arr.Length);
    }

    [Fact]
    public void VecFromFloat32_FloatArray_ReturnsSameArray()
    {
        // Arrange
        float[] input = [1.0f, 2.0f];
        var args = new List<object?> { input };

        // Act
        var result = _provider.Evaluate("VEC_FROM_FLOAT32", args);

        // Assert
        Assert.Same(input, result);
    }

    [Fact]
    public void VecToJson_ProducesValidJson()
    {
        // Arrange
        float[] vec = [1.0f, 2.5f, -3.0f];
        var args = new List<object?> { vec };

        // Act
        var result = _provider.Evaluate("VEC_TO_JSON", args);

        // Assert
        var json = Assert.IsType<string>(result);
        Assert.Contains("1", json);
        Assert.Contains("-3", json);
    }

    [Fact]
    public void VecNormalize_ProducesUnitVector()
    {
        // Arrange
        float[] vec = [3.0f, 4.0f];
        var args = new List<object?> { vec };

        // Act
        var result = _provider.Evaluate("VEC_NORMALIZE", args);

        // Assert
        var normalized = Assert.IsType<float[]>(result);
        float magnitude = MathF.Sqrt(normalized[0] * normalized[0] + normalized[1] * normalized[1]);
        Assert.Equal(1.0f, magnitude, 1e-5f);
    }

    [Fact]
    public void VecDimensions_FloatArray_ReturnsLength()
    {
        // Arrange
        float[] vec = [1.0f, 2.0f, 3.0f, 4.0f, 5.0f];
        var args = new List<object?> { vec };

        // Act
        var result = _provider.Evaluate("VEC_DIMENSIONS", args);

        // Assert
        Assert.Equal(5, result);
    }

    [Fact]
    public void VecDimensions_SerializedBytes_ReturnsDimensions()
    {
        // Arrange
        float[] vec = [1.0f, 2.0f, 3.0f];
        byte[] serialized = VectorSerializer.Serialize(vec);
        var args = new List<object?> { serialized };

        // Act
        var result = _provider.Evaluate("VEC_DIMENSIONS", args);

        // Assert
        Assert.Equal(3, result);
    }

    [Fact]
    public void VecDistanceCosine_StringInput_CoercesFromJson()
    {
        // Arrange
        var args = new List<object?> { "[1.0, 0.0]", "[0.0, 1.0]" };

        // Act
        var result = _provider.Evaluate("VEC_DISTANCE_COSINE", args);

        // Assert — orthogonal → distance ≈ 1.0
        Assert.Equal(1.0f, (float)result!, 1e-4f);
    }

    [Fact]
    public void Evaluate_TooFewArguments_Throws()
    {
        var args = new List<object?> { new float[] { 1.0f } };
        Assert.Throws<ArgumentException>(() => _provider.Evaluate("VEC_DISTANCE_COSINE", args));
    }

    [Fact]
    public void Evaluate_NullArgument_Throws()
    {
        var args = new List<object?> { null };
        Assert.Throws<ArgumentException>(() => _provider.Evaluate("VEC_NORMALIZE", args));
    }

    [Fact]
    public void Evaluate_UnknownFunction_Throws()
    {
        Assert.Throws<NotSupportedException>(() =>
            _provider.Evaluate("VEC_UNKNOWN", new List<object?>()));
    }
}
