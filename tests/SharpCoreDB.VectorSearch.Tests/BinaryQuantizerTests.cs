namespace SharpCoreDB.VectorSearch.Tests;

public class BinaryQuantizerTests
{
    private static BinaryQuantizer CreateCalibrated(int dims = 8)
    {
        var q = new BinaryQuantizer();
        q.Calibrate([new float[dims]]);
        return q;
    }

    [Fact]
    public void Calibrate_SetsProperties()
    {
        // Arrange & Act
        var q = CreateCalibrated(16);

        // Assert
        Assert.True(q.IsCalibrated);
        Assert.Equal(16, q.Dimensions);
        Assert.Equal(QuantizationType.Binary, q.Type);
        Assert.Equal(32.0f, q.CompressionRatio);
    }

    [Fact]
    public void Quantize_PositiveNegative_SetsBitsCorrectly()
    {
        // Arrange
        var q = CreateCalibrated(8);
        // Positive values get bit=1, negative/zero get bit=0
        float[] vector = [1.0f, -1.0f, 1.0f, -1.0f, 0.0f, 1.0f, -1.0f, 1.0f];

        // Act
        byte[] quantized = q.Quantize(vector);

        // Assert — single byte: bits 0,2,5,7 set = 0b10100101 = 0xA5
        Assert.Single(quantized);
        Assert.Equal(0b10100101, quantized[0]);
    }

    [Fact]
    public void Quantize_RoundTrip_PreservesSign()
    {
        // Arrange
        var q = CreateCalibrated(4);
        float[] original = [1.0f, -1.0f, 0.5f, -0.5f];

        // Act
        byte[] quantized = q.Quantize(original);
        float[] reconstructed = q.Dequantize(quantized);

        // Assert — binary quantization maps to +1/-1
        Assert.Equal(1.0f, reconstructed[0]);
        Assert.Equal(-1.0f, reconstructed[1]);
        Assert.Equal(1.0f, reconstructed[2]);
        Assert.Equal(-1.0f, reconstructed[3]);
    }

    [Fact]
    public void HammingDistance_IdenticalVectors_ReturnsZero()
    {
        // Arrange
        byte[] a = [0b10101010, 0b11001100];

        // Act
        float distance = BinaryQuantizer.HammingDistance(a, a);

        // Assert
        Assert.Equal(0f, distance);
    }

    [Fact]
    public void HammingDistance_OppositeVectors_ReturnsMaxBits()
    {
        // Arrange
        byte[] a = [0b00000000];
        byte[] b = [0b11111111];

        // Act
        float distance = BinaryQuantizer.HammingDistance(a, b);

        // Assert
        Assert.Equal(8f, distance);
    }

    [Fact]
    public void HammingDistance_KnownValues_ReturnsCorrect()
    {
        // Arrange — differ in 3 bits
        byte[] a = [0b10101010];
        byte[] b = [0b10100101];

        // Act
        float distance = BinaryQuantizer.HammingDistance(a, b);

        // Assert — XOR = 0b00001111 → popcount = 4
        Assert.Equal(4f, distance);
    }

    [Fact]
    public void SerializeCalibration_RoundTrips()
    {
        // Arrange
        var original = CreateCalibrated(32);
        float[] testVec = new float[32];
        for (int i = 0; i < 32; i++) testVec[i] = (i % 2 == 0) ? 1.0f : -1.0f;
        byte[] originalQ = original.Quantize(testVec);

        // Act
        byte[] data = original.SerializeCalibration();
        var restored = new BinaryQuantizer();
        restored.DeserializeCalibration(data);
        byte[] restoredQ = restored.Quantize(testVec);

        // Assert
        Assert.Equal(originalQ, restoredQ);
    }

    [Fact]
    public void Quantize_BeforeCalibration_Throws()
    {
        var q = new BinaryQuantizer();
        Assert.Throws<InvalidOperationException>(() => q.Quantize(new float[8]));
    }

    [Fact]
    public void Quantize_WrongDimensions_Throws()
    {
        var q = CreateCalibrated(8);
        Assert.Throws<ArgumentException>(() => q.Quantize(new float[16]));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(64)]
    [InlineData(100)]
    public void Quantize_VariousDimensions_ProducesCorrectByteCount(int dims)
    {
        // Arrange
        var q = CreateCalibrated(dims);
        float[] vec = new float[dims];
        for (int i = 0; i < dims; i++) vec[i] = 1.0f;

        // Act
        byte[] quantized = q.Quantize(vec);

        // Assert — ceil(dims / 8) bytes
        int expectedBytes = (dims + 7) / 8;
        Assert.Equal(expectedBytes, quantized.Length);
    }
}
