#nullable enable

using FluentAssertions;
using SharpCoreDB.Provider.Sync.Metadata;
using System.Data;
using Xunit;

namespace SharpCoreDB.Provider.Sync.Tests;

/// <summary>
/// Tests for SharpCoreDB DataType to DbType mappings.
/// Phase 2 verification: Type mapping completeness.
/// </summary>
public sealed class TypeMappingTests
{
    [Theory]
    [InlineData(DataType.Integer, DbType.Int32)]
    [InlineData(DataType.Long, DbType.Int64)]
    [InlineData(DataType.String, DbType.String)]
    [InlineData(DataType.Real, DbType.Double)]
    [InlineData(DataType.Blob, DbType.Binary)]
    [InlineData(DataType.Boolean, DbType.Boolean)]
    [InlineData(DataType.DateTime, DbType.DateTime)]
    [InlineData(DataType.Decimal, DbType.Decimal)]
    [InlineData(DataType.Guid, DbType.Guid)]
    [InlineData(DataType.Ulid, DbType.String)]
    [InlineData(DataType.RowRef, DbType.Int64)]
    public void MapDataType_ShouldReturnCorrectDbType(DataType dataType, DbType expectedDbType)
    {
        // Act
        var result = SharpCoreDBDbMetadata.MapDataType(dataType);

        // Assert
        result.Should().Be(expectedDbType);
    }

    [Fact]
    public void MapDataType_Vector_ShouldThrowNotSupported()
    {
        // Act
        var act = () => SharpCoreDBDbMetadata.MapDataType(DataType.Vector);

        // Assert
        act.Should().Throw<NotSupportedException>()
            .WithMessage("*Vector*not supported*");
    }

    [Theory]
    [InlineData(DbType.Int32, DataType.Integer)]
    [InlineData(DbType.Int64, DataType.Long)]
    [InlineData(DbType.String, DataType.String)]
    [InlineData(DbType.Double, DataType.Real)]
    [InlineData(DbType.Binary, DataType.Blob)]
    [InlineData(DbType.Boolean, DataType.Boolean)]
    [InlineData(DbType.DateTime, DataType.DateTime)]
    [InlineData(DbType.Decimal, DataType.Decimal)]
    [InlineData(DbType.Guid, DataType.Guid)]
    public void MapDbType_ShouldReturnCorrectDataType(DbType dbType, DataType expectedDataType)
    {
        // Act
        var result = SharpCoreDBDbMetadata.MapDbType(dbType);

        // Assert
        result.Should().Be(expectedDataType);
    }

    [Theory]
    [InlineData(DbType.Int32, typeof(int))]
    [InlineData(DbType.Int64, typeof(long))]
    [InlineData(DbType.String, typeof(string))]
    [InlineData(DbType.Double, typeof(double))]
    [InlineData(DbType.Binary, typeof(byte[]))]
    [InlineData(DbType.Boolean, typeof(bool))]
    [InlineData(DbType.DateTime, typeof(DateTime))]
    [InlineData(DbType.Decimal, typeof(decimal))]
    [InlineData(DbType.Guid, typeof(Guid))]
    public void GetClrType_FromDbType_ShouldReturnCorrectType(DbType dbType, Type expectedType)
    {
        // Act
        var result = SharpCoreDBDbMetadata.GetClrType(dbType);

        // Assert
        result.Should().Be(expectedType);
    }

    [Theory]
    [InlineData(DataType.Integer, typeof(int))]
    [InlineData(DataType.Long, typeof(long))]
    [InlineData(DataType.String, typeof(string))]
    [InlineData(DataType.Real, typeof(double))]
    [InlineData(DataType.Blob, typeof(byte[]))]
    [InlineData(DataType.Boolean, typeof(bool))]
    [InlineData(DataType.DateTime, typeof(DateTime))]
    [InlineData(DataType.Decimal, typeof(decimal))]
    [InlineData(DataType.Guid, typeof(Guid))]
    [InlineData(DataType.Ulid, typeof(string))]
    [InlineData(DataType.RowRef, typeof(long))]
    public void GetClrType_FromDataType_ShouldReturnCorrectType(DataType dataType, Type expectedType)
    {
        // Act
        var result = SharpCoreDBDbMetadata.GetClrType(dataType);

        // Assert
        result.Should().Be(expectedType);
    }

    [Theory]
    [InlineData(DataType.Integer, "INTEGER")]
    [InlineData(DataType.Long, "BIGINT")]
    [InlineData(DataType.String, "TEXT")]
    [InlineData(DataType.Real, "REAL")]
    [InlineData(DataType.Blob, "BLOB")]
    [InlineData(DataType.Boolean, "INTEGER")]
    [InlineData(DataType.DateTime, "TEXT")]
    [InlineData(DataType.Decimal, "TEXT")]
    [InlineData(DataType.Guid, "TEXT")]
    [InlineData(DataType.Ulid, "TEXT")]
    [InlineData(DataType.RowRef, "BIGINT")]
    public void ToSqlTypeString_ShouldReturnCorrectSqlType(DataType dataType, string expectedSqlType)
    {
        // Act
        var result = SharpCoreDBDbMetadata.ToSqlTypeString(dataType);

        // Assert
        result.Should().Be(expectedSqlType);
    }

    [Theory]
    [InlineData(DataType.Integer, true)]
    [InlineData(DataType.Long, true)]
    [InlineData(DataType.String, true)]
    [InlineData(DataType.Guid, true)]
    [InlineData(DataType.Vector, false)]
    public void IsSyncSupported_ShouldReturnCorrectValue(DataType dataType, bool expectedSupported)
    {
        // Act
        var result = SharpCoreDBDbMetadata.IsSyncSupported(dataType);

        // Assert
        result.Should().Be(expectedSupported);
    }

    [Fact]
    public void RoundTrip_DataTypeToDbTypeToDataType_ShouldPreserveValue()
    {
        // Arrange
        var originalDataType = DataType.Long;

        // Act
        var dbType = SharpCoreDBDbMetadata.MapDataType(originalDataType);
        var roundTripDataType = SharpCoreDBDbMetadata.MapDbType(dbType);

        // Assert
        roundTripDataType.Should().Be(originalDataType);
    }
}
