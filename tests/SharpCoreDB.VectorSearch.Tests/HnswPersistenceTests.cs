namespace SharpCoreDB.VectorSearch.Tests;

public class HnswPersistenceTests
{
    [Fact]
    public void SerializeDeserialize_EmptyIndex_RoundTrips()
    {
        // Arrange
        var config = HnswConfig.Default(4);
        using var original = new HnswIndex(config, seed: 42);

        // Act
        byte[] data = HnswPersistence.Serialize(original);
        using var restored = HnswPersistence.Deserialize(data, seed: 42);

        // Assert
        Assert.Equal(0, restored.Count);
        Assert.Equal(4, restored.Dimensions);
    }

    [Fact]
    public void SerializeDeserialize_WithVectors_PreservesData()
    {
        // Arrange
        var config = new HnswConfig
        {
            Dimensions = 4,
            M = 8,
            EfConstruction = 30,
            EfSearch = 20,
            DistanceFunction = DistanceFunction.Euclidean,
        };
        using var original = new HnswIndex(config, seed: 42);
        original.Add(1, [1.0f, 0.0f, 0.0f, 0.0f]);
        original.Add(2, [0.0f, 1.0f, 0.0f, 0.0f]);
        original.Add(3, [0.0f, 0.0f, 1.0f, 0.0f]);

        // Act
        byte[] data = HnswPersistence.Serialize(original);
        using var restored = HnswPersistence.Deserialize(data, seed: 42);

        // Assert — same count, same search results
        Assert.Equal(original.Count, restored.Count);
        Assert.Equal(original.Dimensions, restored.Dimensions);

        var query = new float[] { 1.0f, 0.0f, 0.0f, 0.0f };
        var originalResults = original.Search(query, k: 3);
        var restoredResults = restored.Search(query, k: 3);

        Assert.Equal(originalResults.Count, restoredResults.Count);
        Assert.Equal(originalResults[0].Id, restoredResults[0].Id);
    }

    [Fact]
    public void SerializeDeserialize_PreservesConfig()
    {
        // Arrange
        var config = new HnswConfig
        {
            Dimensions = 16,
            M = 32,
            EfConstruction = 400,
            EfSearch = 200,
            DistanceFunction = DistanceFunction.Cosine,
        };
        using var original = new HnswIndex(config, seed: 42);
        original.Add(1, new float[16]);

        // Act
        byte[] data = HnswPersistence.Serialize(original);
        using var restored = HnswPersistence.Deserialize(data, seed: 42);

        // Assert
        Assert.Equal(config.Dimensions, restored.Config.Dimensions);
        Assert.Equal(config.M, restored.Config.M);
        Assert.Equal(config.EfConstruction, restored.Config.EfConstruction);
        Assert.Equal(config.EfSearch, restored.Config.EfSearch);
        Assert.Equal(config.DistanceFunction, restored.Config.DistanceFunction);
    }

    [Fact]
    public void Deserialize_InvalidMagic_Throws()
    {
        // Arrange
        byte[] data = new byte[100];
        data[0] = (byte)'B';
        data[1] = (byte)'A';
        data[2] = (byte)'D';
        data[3] = 0;

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => HnswPersistence.Deserialize(data));
    }

    [Fact]
    public void Deserialize_TooSmall_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            HnswPersistence.Deserialize(new byte[10]));
    }

    [Fact]
    public void GetSnapshot_ReturnsAllNodes()
    {
        // Arrange
        var config = HnswConfig.Default(2);
        using var index = new HnswIndex(config, seed: 42);
        index.Add(10, [1.0f, 0.0f]);
        index.Add(20, [0.0f, 1.0f]);
        index.Add(30, [1.0f, 1.0f]);

        // Act
        var snapshot = index.GetSnapshot();

        // Assert
        Assert.Equal(3, snapshot.Nodes.Count);
        var ids = snapshot.Nodes.Select(n => n.Id).OrderBy(id => id).ToArray();
        Assert.Equal([10L, 20L, 30L], ids);
    }
}
