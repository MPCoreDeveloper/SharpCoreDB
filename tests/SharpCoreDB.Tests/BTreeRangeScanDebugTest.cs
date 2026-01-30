using SharpCoreDB.DataStructures;
using System.Linq;
using Xunit;

namespace SharpCoreDB.Tests;

public class BTreeRangeScanDebugTest
{
    [Fact]
    public void Debug_Minimal_BTree_Insert()
    {
        var btree = new BTree<int, string>();
        
        // Insert just 6 values to trigger first split (degree=3, max keys=5)
        for (int i = 1; i <= 6; i++)
        {
            btree.Insert(i, $"Value{i}");
            
            // Verify immediately after each insert
            for (int j = 1; j <= i; j++)
            {
                var (found, value) = btree.Search(j);
                Assert.True(found, $"After inserting {i}, key {j} not found");
                Assert.Equal($"Value{j}", value);
            }
        }
    }
    
    [Fact]
    public void Debug_BTree_Structure_And_RangeScan()
    {
        var btree = new BTree<int, string>();
        
        // Insert 1-100 to match the failing test
        for (int i = 1; i <= 100; i++)
        {
            btree.Insert(i, $"Value{i}");
        }
        
        // Verify each key can be found
        for (int i = 1; i <= 100; i++)
        {
            var (found, value) = btree.Search(i);
            Assert.True(found, $"Key {i} not found");
            Assert.Equal($"Value{i}", value);
        }
        
        // Get all values via InOrderTraversal
        var allValues = btree.InOrderTraversal().ToList();
        Assert.Equal(100, allValues.Count);
        
        // Get values in range [20, 80] via RangeScan
        var rangeValues = btree.RangeScan(20, 80).ToList();
        
        // Expected: values for keys 20-80 inclusive = 61 values
        Assert.Equal(61, rangeValues.Count);
        
        // Verify first and last
        Assert.Equal("Value20", rangeValues.First());
        Assert.Equal("Value80", rangeValues.Last());
    }
}
