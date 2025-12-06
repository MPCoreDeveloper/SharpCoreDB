// <copyright file="BTree.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace SharpCoreDB.DataStructures;

using System.Collections.Generic;
using SharpCoreDB.Interfaces;

/// <summary>
/// B-tree implementation for indexing.
/// </summary>
public class BTree<TKey, TValue> : IIndex<TKey, TValue>
    where TKey : IComparable<TKey>
{
    private sealed class Node
    {
        public bool IsLeaf;
        public List<TKey> Keys = [];
        public List<TValue> Values = [];
        public List<Node> Children = [];
    }

    private Node? root;
    private readonly int degree = 3;

    /// <inheritdoc />
    public void Insert(TKey key, TValue value)
    {
        if (this.root == null)
        {
            this.root = new Node { IsLeaf = true };
            this.root.Keys.Add(key);
            this.root.Values.Add(value);
            return;
        }

        this.InsertNonFull(this.root, key, value);
    }

    private void InsertNonFull(Node node, TKey key, TValue value)
    {
        int i = node.Keys.Count - 1;
        if (node.IsLeaf)
        {
            while (i >= 0 && key.CompareTo(node.Keys[i]) < 0)
            {
                i--;
            }

            node.Keys.Insert(i + 1, key);
            node.Values.Insert(i + 1, value);
        }
        else
        {
            while (i >= 0 && key.CompareTo(node.Keys[i]) < 0)
            {
                i--;
            }

            i++;
            if (node.Children[i].Keys.Count == (2 * this.degree) - 1)
            {
                this.SplitChild(node, i);
                if (key.CompareTo(node.Keys[i]) > 0)
                {
                    i++;
                }
            }

            this.InsertNonFull(node.Children[i], key, value);
        }
    }

    private void SplitChild(Node parent, int i)
    {
        var y = parent.Children[i];
        var z = new Node { IsLeaf = y.IsLeaf };
        parent.Children.Insert(i + 1, z);
        parent.Keys.Insert(i, y.Keys[this.degree - 1]);
        z.Keys.AddRange(y.Keys.GetRange(this.degree, this.degree - 1));
        y.Keys.RemoveRange(this.degree - 1, this.degree);
        if (!y.IsLeaf)
        {
            z.Children.AddRange(y.Children.GetRange(this.degree, this.degree));
            y.Children.RemoveRange(this.degree, this.degree);
        }
        else
        {
            z.Values.AddRange(y.Values.GetRange(this.degree, this.degree - 1));
            y.Values.RemoveRange(this.degree - 1, this.degree);
        }
    }

    /// <inheritdoc />
    public (bool Found, TValue? Value) Search(TKey key)
    {
        return Search(this.root, key);
    }

    private static (bool Found, TValue? Value) Search(Node? node, TKey key)
    {
        if (node == null)
        {
            return (false, default);
        }

        int i = 0;
        while (i < node.Keys.Count && key.CompareTo(node.Keys[i]) > 0)
        {
            i++;
        }

        if (i < node.Keys.Count && key.CompareTo(node.Keys[i]) == 0)
        {
            return (true, node.Values[i]);
        }

        if (node.IsLeaf)
        {
            return (false, default);
        }

        return Search(node.Children[i], key);
    }
}
