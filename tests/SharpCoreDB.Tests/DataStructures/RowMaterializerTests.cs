namespace SharpCoreDB.Tests.DataStructures;

using System.Buffers.Binary;
using System.Text;
using SharpCoreDB.DataStructures;

public sealed class RowMaterializerTests
{
    [Fact]
    public void MaterializeRow_WithColumnPrefixAndMixedTypes_ShouldDecodeValues()
    {
        var columns = new[] { "id", "name", "active", "created", "amount", "payload", "meta" };
        var types = new[] { typeof(int), typeof(string), typeof(bool), typeof(DateTime), typeof(decimal), typeof(byte[]), typeof(Uri) };
        using var materializer = new RowMaterializer(columns, types);

        var created = DateTime.SpecifyKind(new DateTime(2026, 3, 12, 10, 30, 0), DateTimeKind.Utc);
        var amount = 123.45m;
        var payload = new byte[] { 9, 8, 7 };
        const string metaValue = "urn:test:meta";

        var data = new List<byte> { (byte)columns.Length };

        data.Add(1);
        WriteInt(data, 42);

        data.Add(1);
        WriteString(data, "Alice");

        data.Add(1);
        data.Add(1); // bool true

        data.Add(1);
        WriteInt64(data, created.ToBinary());

        data.Add(1);
        WriteDecimal(data, amount);

        data.Add(1);
        WriteByteArray(data, payload);

        data.Add(1);
        WriteString(data, metaValue); // fallback path for unknown type (Uri)

        var row = materializer.MaterializeRow(data.ToArray(), 0);

        Assert.Equal(42, row["id"]);
        Assert.Equal("Alice", row["name"]);
        Assert.Equal(true, row["active"]);
        Assert.Equal(created, row["created"]);
        Assert.Equal(amount, row["amount"]);
        Assert.True(payload.AsSpan().SequenceEqual((byte[])row["payload"]));
        Assert.Equal(metaValue, row["meta"]);
    }

    [Fact]
    public void MaterializeRow_WithoutColumnPrefixAndNullFlag_ShouldDecodeDBNull()
    {
        var columns = new[] { "id", "name" };
        var types = new[] { typeof(int), typeof(string) };
        using var materializer = new RowMaterializer(columns, types);

        var data = new List<byte>();
        data.Add(1); // id not null
        WriteInt(data, 5);
        data.Add(0); // name is null => DBNull

        var row = materializer.MaterializeRow(data.ToArray(), 0);

        Assert.Equal(5, row["id"]);
        Assert.Equal(DBNull.Value, row["name"]);
    }

    [Fact]
    public void MaterializeRow_WithTruncatedPayload_ShouldThrowInvalidOperationException()
    {
        var columns = new[] { "id" };
        var types = new[] { typeof(int) };
        using var materializer = new RowMaterializer(columns, types);

        var truncated = new byte[] { 1, 0x2A, 0x00 }; // nullFlag + partial int

        Assert.Throws<InvalidOperationException>(() => materializer.MaterializeRow(truncated, 0));
    }

    private static void WriteInt(List<byte> bytes, int value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        bytes.AddRange(buffer.ToArray());
    }

    private static void WriteInt64(List<byte> bytes, long value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(buffer, value);
        bytes.AddRange(buffer.ToArray());
    }

    private static void WriteString(List<byte> bytes, string value)
    {
        var utf8 = Encoding.UTF8.GetBytes(value);
        WriteInt(bytes, utf8.Length);
        bytes.AddRange(utf8);
    }

    private static void WriteByteArray(List<byte> bytes, byte[] value)
    {
        WriteInt(bytes, value.Length);
        bytes.AddRange(value);
    }

    private static void WriteDecimal(List<byte> bytes, decimal value)
    {
        var bits = decimal.GetBits(value);
        for (int i = 0; i < bits.Length; i++)
        {
            WriteInt(bytes, bits[i]);
        }
    }
}
