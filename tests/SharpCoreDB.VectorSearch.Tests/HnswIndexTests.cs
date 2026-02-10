namespace SharpCoreDB.VectorSearch.Tests;

public class HnswIndexTests
{
    private static HnswConfig DefaultConfig(int dims = 8) => new()
    {
        Dimensions = dims,
        M = 8,
        EfConstruction = 50,
        EfSearch = 30,
        DistanceFunction = DistanceFunction.Euclidean,
    };

    [Fact]
    public void Add_SingleVector_Searchable()
    {
        // Arrange
        using var index = new HnswIndex(DefaultConfig(), seed: 42);
        index.Add(1, [1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 6.0f, 7.0f, 8.0f]);

        // Act
        var results = index.Search([1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 6.0f, 7.0f, 8.0f], k: 1);

        // Assert
        Assert.Single(results);
        Assert.Equal(1, results[0].Id);
        Assert.True(results[0].Distance < 1e-5f);
    }

    [Fact]
    public void Search_EmptyIndex_ReturnsEmpty()
    {
        // Arrange
        using var index = new HnswIndex(DefaultConfig(), seed: 42);

        // Act
        var results = index.Search(new float[8], k: 5);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void Add_MultipleVectors_FindsClosest()
    {
        // Arrange
        using var index = new HnswIndex(DefaultConfig(2), seed: 42);
        index.Add(1, [0.0f, 0.0f]);
        index.Add(2, [1.0f, 0.0f]);
        index.Add(3, [10.0f, 10.0f]);

        // Act — search near origin
        var results = index.Search([0.1f, 0.0f], k: 1);

        // Assert — closest should be id=1
        Assert.Single(results);
        Assert.Equal(1, results[0].Id);
    }

    [Fact]
    public void Remove_ExistingVector_Succeeds()
    {
        // Arrange
        using var index = new HnswIndex(DefaultConfig(2), seed: 42);
        index.Add(1, [1.0f, 0.0f]);
        index.Add(2, [0.0f, 1.0f]);
        index.Add(3, [1.0f, 1.0f]);

        // Act
        bool removed = index.Remove(1);

        // Assert
        Assert.True(removed);
        Assert.Equal(2, index.Count);

        var results = index.Search([1.0f, 0.0f], k: 10);
        Assert.DoesNotContain(results, r => r.Id == 1);
    }

    [Fact]
    public void Remove_NonExistent_ReturnsFalse()
    {
        // Arrange
        using var index = new HnswIndex(DefaultConfig(2), seed: 42);
        index.Add(1, [1.0f, 0.0f]);

        // Act & Assert
        Assert.False(index.Remove(999));
    }

    [Fact]
    public void Clear_ResetsEverything()
    {
        // Arrange
        using var index = new HnswIndex(DefaultConfig(2), seed: 42);
        index.Add(1, [1.0f, 0.0f]);
        index.Add(2, [0.0f, 1.0f]);

        // Act
        index.Clear();

        // Assert
        Assert.Equal(0, index.Count);
        Assert.Equal(0, index.MaxLevel);
        Assert.Empty(index.Search([0.5f, 0.5f], k: 10));
    }

    [Fact]
    public void Add_DuplicateId_Throws()
    {
        // Arrange
        using var index = new HnswIndex(DefaultConfig(2), seed: 42);
        index.Add(1, [1.0f, 0.0f]);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => index.Add(1, [0.0f, 1.0f]));
    }

    [Fact]
    public void Add_WrongDimensions_Throws()
    {
        // Arrange
        using var index = new HnswIndex(DefaultConfig(4), seed: 42);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => index.Add(1, [1.0f, 2.0f]));
    }

    [Fact]
    public void Properties_ReflectConfiguration()
    {
        // Arrange
        var config = DefaultConfig(128);
        using var index = new HnswIndex(config, seed: 42);

        // Assert
        Assert.Equal(VectorIndexType.Hnsw, index.IndexType);
        Assert.Equal(128, index.Dimensions);
        Assert.Equal(DistanceFunction.Euclidean, index.DistanceFunction);
        Assert.Same(config, index.Config);
    }

    [Fact]
    public void RecallAt10_Above90Percent_With100Vectors()
    {
        // Arrange — 100 random 16-dim vectors
        var rng = new Random(42);
        int dims = 16;
        int count = 100;
        int k = 10;

        var config = new HnswConfig
        {
            Dimensions = dims,
            M = 16,
            EfConstruction = 100,
            EfSearch = 50,
            DistanceFunction = DistanceFunction.Euclidean,
        };

        using var hnsw = new HnswIndex(config, seed: 42);
        using var flat = new FlatIndex(dims, DistanceFunction.Euclidean);

        var vectors = new float[count][];
        for (int i = 0; i < count; i++)
        {
            vectors[i] = new float[dims];
            for (int d = 0; d < dims; d++)
                vectors[i][d] = (float)(rng.NextDouble() * 2 - 1);

            hnsw.Add(i, vectors[i]);
            flat.Add(i, vectors[i]);
        }

        // Act — run 10 queries and measure recall
        int totalHits = 0;
        int totalExpected = 0;

        for (int q = 0; q < 10; q++)
        {
            float[] query = new float[dims];
            for (int d = 0; d < dims; d++)
                query[d] = (float)(rng.NextDouble() * 2 - 1);

            var exactResults = flat.Search(query, k);
            var hnswResults = hnsw.Search(query, k);

            var exactIds = new HashSet<long>(exactResults.Select(r => r.Id));
            totalHits += hnswResults.Count(r => exactIds.Contains(r.Id));
            totalExpected += exactIds.Count;
        }

        double recall = (double)totalHits / totalExpected;

        // Assert — recall should be > 90% (typically > 95%)
        Assert.True(recall >= 0.90, $"Recall@{k} was {recall:P1}, expected >= 90%");
    }

    [Fact]
    public void ConcurrentReads_DuringInsert_DoNotThrow()
    {
        // Arrange
        var config = new HnswConfig
        {
            Dimensions = 4,
            M = 8,
            EfConstruction = 30,
            EfSearch = 20,
            DistanceFunction = DistanceFunction.Cosine,
        };
        using var index = new HnswIndex(config, seed: 42);

        // Pre-populate with some vectors
        var rng = new Random(42);
        for (int i = 0; i < 20; i++)
        {
            float[] vec = [
                (float)rng.NextDouble(),
                (float)rng.NextDouble(),
                (float)rng.NextDouble(),
                (float)rng.NextDouble()
            ];
            index.Add(i, vec);
        }

        // Act — concurrent reads and writes
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        var readTask = Task.Run(() =>
        {
            int queryCount = 0;
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    float[] query = [0.5f, 0.5f, 0.5f, 0.5f];
                    _ = index.Search(query, k: 3);
                    queryCount++;
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }
        });

        var writeTask = Task.Run(() =>
        {
            var rng2 = new Random(123);
            for (int i = 100; i < 200 && !cts.Token.IsCancellationRequested; i++)
            {
                try
                {
                    float[] vec = [
                        (float)rng2.NextDouble(),
                        (float)rng2.NextDouble(),
                        (float)rng2.NextDouble(),
                        (float)rng2.NextDouble()
                    ];
                    index.Add(i, vec);
                    Thread.Sleep(5);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }
            cts.Cancel();
        });

        Task.WaitAll(readTask, writeTask);

        // Assert — no exceptions during concurrent access
        Assert.Empty(exceptions);
    }
}
