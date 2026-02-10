namespace SharpCoreDB.VectorSearch.Tests;

public class DistanceMetricsTests
{
    [Fact]
    public void CosineDistance_IdenticalVectors_ReturnsZero()
    {
        // Arrange
        float[] a = [1.0f, 2.0f, 3.0f];

        // Act
        float distance = DistanceMetrics.CosineDistance(a, a);

        // Assert
        Assert.True(distance < 1e-5f, $"Expected ~0, got {distance}");
    }

    [Fact]
    public void CosineDistance_OrthogonalVectors_ReturnsOne()
    {
        // Arrange
        float[] a = [1.0f, 0.0f];
        float[] b = [0.0f, 1.0f];

        // Act
        float distance = DistanceMetrics.CosineDistance(a, b);

        // Assert
        Assert.True(Math.Abs(distance - 1.0f) < 1e-5f, $"Expected ~1, got {distance}");
    }

    [Fact]
    public void CosineDistance_OppositeVectors_ReturnsTwo()
    {
        // Arrange
        float[] a = [1.0f, 0.0f];
        float[] b = [-1.0f, 0.0f];

        // Act
        float distance = DistanceMetrics.CosineDistance(a, b);

        // Assert
        Assert.True(Math.Abs(distance - 2.0f) < 1e-5f, $"Expected ~2, got {distance}");
    }

    [Fact]
    public void CosineDistance_ZeroVector_ReturnsFallback()
    {
        // Arrange
        float[] a = [0.0f, 0.0f, 0.0f];
        float[] b = [1.0f, 2.0f, 3.0f];

        // Act
        float distance = DistanceMetrics.CosineDistance(a, b);

        // Assert — zero vector → orthogonal fallback (1.0)
        Assert.Equal(1.0f, distance);
    }

    [Fact]
    public void EuclideanDistance_IdenticalVectors_ReturnsZero()
    {
        // Arrange
        float[] a = [1.0f, 2.0f, 3.0f];

        // Act
        float distance = DistanceMetrics.EuclideanDistance(a, a);

        // Assert
        Assert.Equal(0.0f, distance, 1e-5f);
    }

    [Fact]
    public void EuclideanDistance_KnownValues_ReturnsCorrect()
    {
        // Arrange — distance between (0,0) and (3,4) = 5
        float[] a = [0.0f, 0.0f];
        float[] b = [3.0f, 4.0f];

        // Act
        float distance = DistanceMetrics.EuclideanDistance(a, b);

        // Assert
        Assert.Equal(5.0f, distance, 1e-5f);
    }

    [Fact]
    public void EuclideanDistanceSquared_KnownValues_ReturnsCorrect()
    {
        // Arrange
        float[] a = [0.0f, 0.0f];
        float[] b = [3.0f, 4.0f];

        // Act
        float distSq = DistanceMetrics.EuclideanDistanceSquared(a, b);

        // Assert
        Assert.Equal(25.0f, distSq, 1e-5f);
    }

    [Fact]
    public void DotProduct_KnownValues_ReturnsCorrect()
    {
        // Arrange — (1,2,3) · (4,5,6) = 4+10+18 = 32
        float[] a = [1.0f, 2.0f, 3.0f];
        float[] b = [4.0f, 5.0f, 6.0f];

        // Act
        float dot = DistanceMetrics.DotProduct(a, b);

        // Assert
        Assert.Equal(32.0f, dot, 1e-5f);
    }

    [Fact]
    public void NegativeDotProduct_IsNegated()
    {
        // Arrange
        float[] a = [1.0f, 2.0f];
        float[] b = [3.0f, 4.0f];

        // Act
        float dot = DistanceMetrics.DotProduct(a, b);
        float negDot = DistanceMetrics.NegativeDotProduct(a, b);

        // Assert
        Assert.Equal(-dot, negDot, 1e-5f);
    }

    [Fact]
    public void Normalize_ProducesUnitVector()
    {
        // Arrange
        float[] vector = [3.0f, 4.0f];

        // Act
        float[] normalized = DistanceMetrics.Normalize(vector);

        // Assert — magnitude should be 1.0
        float magnitude = MathF.Sqrt(normalized[0] * normalized[0] + normalized[1] * normalized[1]);
        Assert.Equal(1.0f, magnitude, 1e-5f);
        Assert.Equal(0.6f, normalized[0], 1e-5f); // 3/5
        Assert.Equal(0.8f, normalized[1], 1e-5f); // 4/5
    }

    [Fact]
    public void Normalize_ZeroVector_ReturnsCopy()
    {
        // Arrange
        float[] zero = [0.0f, 0.0f, 0.0f];

        // Act
        float[] result = DistanceMetrics.Normalize(zero);

        // Assert
        Assert.Equal(zero, result);
    }

    [Fact]
    public void Compute_DispatchesCorrectly()
    {
        // Arrange
        float[] a = [1.0f, 0.0f];
        float[] b = [0.0f, 1.0f];

        // Act & Assert
        float cosine = DistanceMetrics.Compute(a, b, DistanceFunction.Cosine);
        Assert.True(Math.Abs(cosine - 1.0f) < 1e-5f);

        float l2 = DistanceMetrics.Compute(a, b, DistanceFunction.Euclidean);
        Assert.Equal(MathF.Sqrt(2.0f), l2, 1e-5f);
    }

    [Fact]
    public void DimensionMismatch_ThrowsArgumentException()
    {
        // Arrange
        float[] a = [1.0f, 2.0f];
        float[] b = [1.0f, 2.0f, 3.0f];

        // Act & Assert
        Assert.Throws<ArgumentException>(() => DistanceMetrics.CosineDistance(a, b));
        Assert.Throws<ArgumentException>(() => DistanceMetrics.EuclideanDistance(a, b));
        Assert.Throws<ArgumentException>(() => DistanceMetrics.DotProduct(a, b));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(33)]
    [InlineData(128)]
    [InlineData(1536)]
    public void CosineDistance_SimdVsScalarTail_ConsistentResults(int dimensions)
    {
        // Arrange — use enough dimensions to trigger SIMD paths + scalar tail
        var rng = new Random(42);
        float[] a = new float[dimensions];
        float[] b = new float[dimensions];
        for (int i = 0; i < dimensions; i++)
        {
            a[i] = (float)(rng.NextDouble() * 2 - 1);
            b[i] = (float)(rng.NextDouble() * 2 - 1);
        }

        // Act
        float distance = DistanceMetrics.CosineDistance(a, b);

        // Assert — result should be in valid range [0, 2]
        Assert.InRange(distance, -0.01f, 2.01f);
    }
}
