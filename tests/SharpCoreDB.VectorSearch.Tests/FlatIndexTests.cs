namespace SharpCoreDB.VectorSearch.Tests;

public class FlatIndexTests
{
    [Fact]
    public void Add_AndSearch_ReturnsClosest()
    {
        // Arrange
        using var index = new FlatIndex(3, DistanceFunction.Euclidean);
        index.Add(1, [0.0f, 0.0f, 0.0f]);
        index.Add(2, [1.0f, 0.0f, 0.0f]);
        index.Add(3, [10.0f, 10.0f, 10.0f]);

        // Act — search near origin
        var results = index.Search([0.1f, 0.0f, 0.0f], k: 2);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Equal(1, results[0].Id); // closest to origin
        Assert.Equal(2, results[1].Id); // second closest
    }

    [Fact]
    public void Search_EmptyIndex_ReturnsEmpty()
    {
        // Arrange
        using var index = new FlatIndex(3);

        // Act
        var results = index.Search([1.0f, 2.0f, 3.0f], k: 5);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void Search_KGreaterThanCount_ReturnsAll()
    {
        // Arrange
        using var index = new FlatIndex(2);
        index.Add(1, [1.0f, 0.0f]);
        index.Add(2, [0.0f, 1.0f]);

        // Act
        var results = index.Search([0.5f, 0.5f], k: 100);

        // Assert
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void Remove_ExistingId_Succeeds()
    {
        // Arrange
        using var index = new FlatIndex(2);
        index.Add(1, [1.0f, 0.0f]);
        index.Add(2, [0.0f, 1.0f]);

        // Act
        bool removed = index.Remove(1);

        // Assert
        Assert.True(removed);
        Assert.Equal(1, index.Count);

        var results = index.Search([1.0f, 0.0f], k: 10);
        Assert.Single(results);
        Assert.Equal(2, results[0].Id);
    }

    [Fact]
    public void Remove_NonExistentId_ReturnsFalse()
    {
        // Arrange
        using var index = new FlatIndex(2);
        index.Add(1, [1.0f, 0.0f]);

        // Act & Assert
        Assert.False(index.Remove(999));
    }

    [Fact]
    public void Clear_RemovesAllVectors()
    {
        // Arrange
        using var index = new FlatIndex(2);
        index.Add(1, [1.0f, 0.0f]);
        index.Add(2, [0.0f, 1.0f]);

        // Act
        index.Clear();

        // Assert
        Assert.Equal(0, index.Count);
        Assert.Empty(index.Search([0.5f, 0.5f], k: 10));
    }

    [Fact]
    public void Add_WrongDimensions_Throws()
    {
        // Arrange
        using var index = new FlatIndex(3);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => index.Add(1, [1.0f, 2.0f]));
    }

    [Fact]
    public void Search_WrongQueryDimensions_Throws()
    {
        // Arrange
        using var index = new FlatIndex(3);
        index.Add(1, [1.0f, 2.0f, 3.0f]);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => index.Search([1.0f], k: 1));
    }

    [Fact]
    public void Properties_ReflectState()
    {
        // Arrange
        using var index = new FlatIndex(128, DistanceFunction.Cosine);
        index.Add(1, new float[128]);

        // Assert
        Assert.Equal(VectorIndexType.Flat, index.IndexType);
        Assert.Equal(128, index.Dimensions);
        Assert.Equal(DistanceFunction.Cosine, index.DistanceFunction);
        Assert.Equal(1, index.Count);
        Assert.True(index.EstimatedMemoryBytes > 0);
    }

    [Fact]
    public void Search_ResultsSortedByDistance()
    {
        // Arrange
        using var index = new FlatIndex(2, DistanceFunction.Euclidean);
        index.Add(1, [10.0f, 10.0f]);
        index.Add(2, [1.0f, 1.0f]);
        index.Add(3, [5.0f, 5.0f]);

        // Act — query at origin
        var results = index.Search([0.0f, 0.0f], k: 3);

        // Assert — should be sorted by ascending distance
        Assert.Equal(3, results.Count);
        for (int i = 1; i < results.Count; i++)
        {
            Assert.True(results[i].Distance >= results[i - 1].Distance,
                $"Result {i} distance {results[i].Distance} < result {i - 1} distance {results[i - 1].Distance}");
        }
    }
}
