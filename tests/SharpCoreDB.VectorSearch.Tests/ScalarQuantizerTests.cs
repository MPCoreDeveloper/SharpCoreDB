namespace SharpCoreDB.VectorSearch.Tests;

public class ScalarQuantizerTests
{
    private static ScalarQuantizer CreateCalibrated(int dims = 4)
    {
        var q = new ScalarQuantizer();
        var samples = new List<float[]>
        {
            Enumerable.Range(0, dims).Select(_ => -1.0f).ToArray(),
            Enumerable.Range(0, dims).Select(_ => 1.0f).ToArray(),
        };
        q.Calibrate(samples);
        return q;
    }

    [Fact]
    public void Calibrate_SetsProperties()
    {
        // Arrange & Act
        var q = CreateCalibrated(128);

        // Assert
        Assert.True(q.IsCalibrated);
        Assert.Equal(128, q.Dimensions);
        Assert.Equal(QuantizationType.Scalar8, q.Type);
        Assert.Equal(4.0f, q.CompressionRatio);
    }

    [Fact]
    public void Quantize_RoundTrip_ApproximatelyCorrect()
    {
        // Arrange
        var q = CreateCalibrated(4);
        float[] original = [0.5f, -0.5f, 0.0f, 0.75f];

        // Act
        byte[] quantized = q.Quantize(original);
        float[] reconstructed = q.Dequantize(quantized);

        // Assert — within quantization error (~1/128 for uint8 over [-1, 1])
        for (int i = 0; i < original.Length; i++)
        {
            Assert.Equal(original[i], reconstructed[i], 0.02f);
        }
    }

    [Fact]
    public void Quantize_MinMax_MapsToExtremes()
    {
        // Arrange
        var q = CreateCalibrated(2);

        // Act
        byte[] qMin = q.Quantize([-1.0f, -1.0f]);
        byte[] qMax = q.Quantize([1.0f, 1.0f]);

        // Assert
        Assert.Equal(0, qMin[0]);
        Assert.Equal(0, qMin[1]);
        Assert.Equal(255, qMax[0]);
        Assert.Equal(255, qMax[1]);
    }

    [Fact]
    public void DistanceQuantized_ApproximatesFullPrecision()
    {
        // Arrange
        var q = CreateCalibrated(8);
        var rng = new Random(42);
        float[] query = new float[8];
        float[] target = new float[8];
        for (int i = 0; i < 8; i++)
        {
            query[i] = (float)(rng.NextDouble() * 2 - 1);
            target[i] = (float)(rng.NextDouble() * 2 - 1);
        }

        byte[] quantizedTarget = q.Quantize(target);
        float exactDist = DistanceMetrics.EuclideanDistance(query, target);

        // Act
        float approxDist = q.DistanceQuantized(query, quantizedTarget, DistanceFunction.Euclidean);

        // Assert — should be within 10% for 8 dimensions
        Assert.True(Math.Abs(exactDist - approxDist) < exactDist * 0.15f + 0.01f,
            $"Exact: {exactDist}, Approx: {approxDist}");
    }

    [Fact]
    public void SerializeCalibration_RoundTrips()
    {
        // Arrange
        var original = CreateCalibrated(16);
        float[] testVec = new float[16];
        for (int i = 0; i < 16; i++) testVec[i] = (float)(i * 0.1 - 0.8);
        byte[] originalQuantized = original.Quantize(testVec);

        // Act
        byte[] calibData = original.SerializeCalibration();
        var restored = new ScalarQuantizer();
        restored.DeserializeCalibration(calibData);
        byte[] restoredQuantized = restored.Quantize(testVec);

        // Assert
        Assert.Equal(originalQuantized, restoredQuantized);
    }

    [Fact]
    public void Quantize_BeforeCalibration_Throws()
    {
        var q = new ScalarQuantizer();
        Assert.Throws<InvalidOperationException>(() => q.Quantize(new float[4]));
    }

    [Fact]
    public void Quantize_WrongDimensions_Throws()
    {
        var q = CreateCalibrated(4);
        Assert.Throws<ArgumentException>(() => q.Quantize(new float[8]));
    }

    [Fact]
    public void Calibrate_EmptySamples_Throws()
    {
        var q = new ScalarQuantizer();
        Assert.Throws<ArgumentException>(() => q.Calibrate(new List<float[]>()));
    }
}
