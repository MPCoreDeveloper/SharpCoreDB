using SharpCoreDB.DataStructures;
using System;
using System.Linq;

var index = new BTreeIndex<int>("test");

// Add values 1-100
for (int i = 1; i <= 100; i++)
{
    index.Add(i, i * 10L);
}

// Get full inorder traversal
var allResults = index.GetSortedEntries().ToList();
Console.WriteLine($"Total entries in tree: {allResults.Count}");

// Check what values are in the range 20-80
var rangeResults = index.FindRange(20, 80).ToList();
Console.WriteLine($"Range [20-80] results: {rangeResults.Count} items");
Console.WriteLine($"First 10: {string.Join(", ", rangeResults.Take(10))}");
Console.WriteLine($"Last 10: {string.Join(", ", rangeResults.Skip(Math.Max(0, rangeResults.Count - 10)))}");

// Check which IDs are missing
var expectedIds = Enumerable.Range(20, 61).Select(id => id * 10L).ToHashSet();
var missingIds = expectedIds.Where(id => !rangeResults.Contains(id)).ToList();
Console.WriteLine($"\nMissing {missingIds.Count} items: {string.Join(", ", missingIds.Take(20))}");

// Test simple case
var simpleIndex = new BTreeIndex<int>("simple");
simpleIndex.Add(1, 10);
simpleIndex.Add(2, 20);
simpleIndex.Add(3, 30);
simpleIndex.Add(4, 40);
simpleIndex.Add(5, 50);

var simpleRange = simpleIndex.FindRange(2, 4).ToList();
Console.WriteLine($"\nSimple test [2-4]: got {simpleRange.Count}, expected 3: {string.Join(", ", simpleRange)}");
