using Grpc.Core;
using SharpCoreDB.Client;
using SharpCoreDB.Server.Protocol;

namespace SharpCoreDB.Tests.Client;

public sealed class SharpCoreDBDataReaderTests
{
    [Fact]
    public async Task ReadAsync_WhenReaderIsClosed_ShouldReturnFalse()
    {
        // Arrange
        var stream = new FakeAsyncStreamReader();
        await using var reader = new SharpCoreDBDataReader(stream, CancellationToken.None);
        reader.Close();

        // Act
        var result = await reader.ReadAsync();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ReadAsync_WhenFirstResponseHasRows_ShouldReturnTrue()
    {
        // Arrange
        var response = new QueryResponse();
        response.Columns.Add(new ColumnMetadata { Name = "Id", Type = SharpCoreDB.Server.Protocol.DataType.Integer });
        var row = new RowData();
        row.Values.Add(new ParameterValue { IntValue = 123 });
        response.Rows.Add(row);

        var stream = new FakeAsyncStreamReader(response);
        await using var reader = new SharpCoreDBDataReader(stream, CancellationToken.None);

        // Act
        var result = await reader.ReadAsync();

        // Assert
        Assert.True(result);
        Assert.Equal(123, reader.GetInt32(0));
    }

    [Fact]
    public async Task ReadAsync_WhenNoResponses_ShouldReturnFalse()
    {
        // Arrange
        var stream = new FakeAsyncStreamReader();
        await using var reader = new SharpCoreDBDataReader(stream, CancellationToken.None);

        // Act
        var result = await reader.ReadAsync();

        // Assert
        Assert.False(result);
    }

    private sealed class FakeAsyncStreamReader : IAsyncStreamReader<QueryResponse>
    {
        private readonly Queue<QueryResponse> _responses;

        public FakeAsyncStreamReader(params QueryResponse[] responses)
        {
            _responses = new Queue<QueryResponse>(responses);
        }

        public QueryResponse Current { get; private set; } = new();

        public Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            if (_responses.Count == 0)
            {
                return Task.FromResult(false);
            }

            Current = _responses.Dequeue();
            return Task.FromResult(true);
        }
    }
}
