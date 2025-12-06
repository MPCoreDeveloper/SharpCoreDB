using System.Collections.Generic;
using SharpCoreDB.Interfaces;

namespace SharpCoreDB.DataStructures;

/// <summary>
/// B-tree implementation for indexing.
/// </summary>
public class BTree<TKey, TValue> : IIndex<TKey, TValue> where TKey : IComparable<TKey>
{
    private sealed class Node
    {
        public bool IsLeaf;
        public List<TKey> Keys = [];
        public List<TValue> Values = [];
        public List<Node> Children = [];
    }

    private Node? _root;
    private readonly int _degree = 3;

    /// <inheritdoc />
    public void Insert(TKey key, TValue value)
    {
        if (_root == null)
        {
            _root = new Node { IsLeaf = true };
            _root.Keys.Add(key);
            _root.Values.Add(value);
            return;
        }
        InsertNonFull(_root, key, value);
    }

    private void InsertNonFull(Node node, TKey key, TValue value)
    {
        int i = node.Keys.Count - 1;
        if (node.IsLeaf)
        {
            while (i >= 0 && key.CompareTo(node.Keys[i]) < 0) i--;
            node.Keys.Insert(i + 1, key);
            node.Values.Insert(i + 1, value);
        }
        else
        {
            while (i >= 0 && key.CompareTo(node.Keys[i]) < 0) i--;
            i++;
            if (node.Children[i].Keys.Count == 2 * _degree - 1)
            {
                SplitChild(node, i);
                if (key.CompareTo(node.Keys[i]) > 0) i++;
            }
            InsertNonFull(node.Children[i], key, value);
        }
    }

    private void SplitChild(Node parent, int i)
    {
        var y = parent.Children[i];
        var z = new Node { IsLeaf = y.IsLeaf };
        parent.Children.Insert(i + 1, z);
        parent.Keys.Insert(i, y.Keys[_degree - 1]);
        z.Keys.AddRange(y.Keys.GetRange(_degree, _degree - 1));
        y.Keys.RemoveRange(_degree - 1, _degree);
        if (!y.IsLeaf)
        {
            z.Children.AddRange(y.Children.GetRange(_degree, _degree));
            y.Children.RemoveRange(_degree, _degree);
        }
        else
        {
            z.Values.AddRange(y.Values.GetRange(_degree, _degree - 1));
            y.Values.RemoveRange(_degree - 1, _degree);
        }
    }

    /// <inheritdoc />
    public (bool Found, TValue? Value) Search(TKey key)
    {
        return Search(_root, key);
    }

    private static (bool Found, TValue? Value) Search(Node? node, TKey key)
    {
        if (node == null) return (false, default);
        int i = 0;
        while (i < node.Keys.Count && key.CompareTo(node.Keys[i]) > 0) i++;
        if (i < node.Keys.Count && key.CompareTo(node.Keys[i]) == 0) return (true, node.Values[i]);
        if (node.IsLeaf) return (false, default);
        return Search(node.Children[i], key);
    }
}
