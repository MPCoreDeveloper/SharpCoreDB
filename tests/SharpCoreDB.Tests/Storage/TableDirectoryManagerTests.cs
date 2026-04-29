namespace SharpCoreDB.Tests.Storage;

using Moq;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Storage;
using SharpCoreDB.Storage.Scdb;

public sealed class TableDirectoryManagerTests
{
    [Fact]
    public void CreateTable_ShouldPersistResolvedColumnAndIndexOffsets()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"tabledir_{Guid.NewGuid():N}.scdb");

        try
        {
            var options = DatabaseOptions.CreateSingleFileDefault();
            options.EnableMemoryMapping = false;

            using (var provider = SingleFileStorageProvider.Open(dbPath, options))
            {
                var manager = provider.TableDirectoryManager;

                var table = new Mock<ITable>(MockBehavior.Strict);
                table.SetupGet(t => t.Name).Returns("orders");

                var columns = new List<ColumnDefinitionEntry>
                {
                    new ColumnDefinitionEntry { DataType = 1, Flags = 0 }
                };

                var indexes = new List<IndexDefinitionEntry>
                {
                    new IndexDefinitionEntry { IndexType = 0, ColumnCount = 1 }
                };

                manager.CreateTable(table.Object, dataBlockOffset: 123, columns, indexes);
                manager.Flush();

                var metadata = manager.GetTableMetadata("orders");
                Assert.True(metadata.HasValue);

                var columnBlock = provider.GetBlockMetadata("table:orders:columns");
                var indexBlock = provider.GetBlockMetadata("table:orders:indexes");

                Assert.NotNull(columnBlock);
                Assert.NotNull(indexBlock);
                Assert.Equal(checked((ulong)columnBlock!.Offset), metadata.Value.ColumnDefsOffset);
                Assert.Equal(checked((ulong)indexBlock!.Offset), metadata.Value.IndexDefsOffset);
                Assert.Equal(1u, metadata.Value.HashIndexCount);
                Assert.Equal(0u, metadata.Value.BTreeIndexCount);

                table.VerifyAll();
            }

            using var reopened = SingleFileStorageProvider.Open(dbPath, DatabaseOptions.CreateSingleFileDefault());
            var reopenedMetadata = reopened.TableDirectoryManager.GetTableMetadata("orders");

            Assert.True(reopenedMetadata.HasValue);
            Assert.NotEqual(0UL, reopenedMetadata.Value.ColumnDefsOffset);
            Assert.NotEqual(0UL, reopenedMetadata.Value.IndexDefsOffset);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }
}
