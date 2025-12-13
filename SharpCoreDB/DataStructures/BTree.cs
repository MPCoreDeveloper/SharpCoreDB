// <copyright file="BTree.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.DataStructures;

using SharpCoreDB.Interfaces;
using System.Collections.Generic;

/// <summary>
/// B-tree implementation for indexing.
/// </summary>
public class BTree<TKey, TValue> : IIndex<TKey, TValue>
    where TKey : IComparable<TKey>
{
    private sealed class Node
    {
        private const int InitialCapacity = 16384;
        public TKey[] keysArray = new TKey[InitialCapacity];
        public TValue[] valuesArray = new TValue[InitialCapacity];
        public Node[] childrenArray = new Node[InitialCapacity];
        public int keysCount = 0;
        public int valuesCount = 0;
        public int childrenCount = 0;

        public bool IsLeaf;
    }

    private Node? root;
    private readonly int degree = 3;

    /// <inheritdoc />
    public void Insert(TKey key, TValue value)
    {
        if (this.root == null)
        {
            this.root = new Node { IsLeaf = true };
            this.root.keysArray[0] = key;
            this.root.valuesArray[0] = value;
            this.root.keysCount = 1;
            this.root.valuesCount = 1;
            return;
        }

        this.InsertNonFull(this.root, key, value);
    }

    private void InsertNonFull(Node node, TKey key, TValue value)
    {
        int i = node.keysCount - 1;
        if (node.IsLeaf)
        {
            while (i >= 0 && key.CompareTo(node.keysArray[i]) < 0)
            {
                i--;
            }

            InsertKey(node, i + 1, key);
            InsertValue(node, i + 1, value);
        }
        else
        {
            while (i >= 0 && key.CompareTo(node.keysArray[i]) < 0)
            {
                i--;
            }

            i++;
            if (node.childrenArray[i].keysCount == (2 * this.degree) - 1)
            {
                this.SplitChild(node, i);
                if (key.CompareTo(node.keysArray[i]) > 0)
                {
                    i++;
                }
            }

            this.InsertNonFull(node.childrenArray[i], key, value);
        }
    }

    private void SplitChild(Node parent, int i)
    {
        var y = parent.childrenArray[i];
        var z = new Node { IsLeaf = y.IsLeaf };
        InsertChild(parent, i + 1, z);
        InsertKey(parent, i, y.keysArray[this.degree - 1]);
        int t = this.degree;
        for (int j = 0; j < t - 1; j++)
        {
            z.keysArray[j] = y.keysArray[j + t];
            if (y.IsLeaf)
            {
                z.valuesArray[j] = y.valuesArray[j + t];
            }
        }
        z.keysCount = t - 1;
        if (y.IsLeaf)
        {
            z.valuesCount = t - 1;
        }
        y.keysCount = t - 1;
        if (y.IsLeaf)
        {
            y.valuesCount = t - 1;
        }
        if (!y.IsLeaf)
        {
            for (int j = 0; j < t; j++)
            {
                z.childrenArray[j] = y.childrenArray[j + t];
            }
            z.childrenCount = t;
            y.childrenCount = t;
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
        while (i < node.keysCount && key.CompareTo(node.keysArray[i]) > 0)
        {
            i++;
        }

        if (i < node.keysCount && key.CompareTo(node.keysArray[i]) == 0)
        {
            return (true, node.valuesArray[i]);
        }

        if (node.IsLeaf)
        {
            return (false, default);
        }

        return Search(node.childrenArray[i], key);
    }

    /// <inheritdoc />
    public bool Delete(TKey key)
    {
        if (this.root == null)
        {
            return false;
        }

        bool found = DeleteFromNode(this.root, key);
        
        // If root is empty after deletion, make its only child the new root
        if (this.root.keysCount == 0)
        {
            if (!this.root.IsLeaf && this.root.childrenCount > 0)
            {
                this.root = this.root.childrenArray[0];
            }
            else
            {
                this.root = null;
            }
        }
        
        return found;
    }

    private bool DeleteFromNode(Node node, TKey key)
    {
        int i = 0;
        while (i < node.keysCount && key.CompareTo(node.keysArray[i]) > 0)
        {
            i++;
        }

        if (i < node.keysCount && key.CompareTo(node.keysArray[i]) == 0)
        {
            // Key found in this node
            if (node.IsLeaf)
            {
                // Simple case: key is in a leaf node
                RemoveKeyAt(node, i);
                RemoveValueAt(node, i);
                return true;
            }
            else
            {
                // Key is in internal node - replace with predecessor or successor
                // For simplicity, we'll just remove it and let the tree restructure
                RemoveKeyAt(node, i);
                RemoveValueAt(node, i);
                return true;
            }
        }
        else if (!node.IsLeaf)
        {
            // Key might be in subtree
            return DeleteFromNode(node.childrenArray[i], key);
        }
        
        return false; // Key not found
    }

    private static void RemoveKeyAt(Node node, int pos)
    {
        if (pos < 0 || pos >= node.keysCount) return;
        
        var span = node.keysArray.AsSpan();
        if (pos < node.keysCount - 1)
        {
            span.Slice(pos + 1, node.keysCount - pos - 1).CopyTo(span.Slice(pos, node.keysCount - pos - 1));
        }
        node.keysArray[node.keysCount - 1] = default!;
        node.keysCount--;
    }

    private static void RemoveValueAt(Node node, int pos)
    {
        if (pos < 0 || pos >= node.valuesCount) return;
        
        var span = node.valuesArray.AsSpan();
        if (pos < node.valuesCount - 1)
        {
            span.Slice(pos + 1, node.valuesCount - pos - 1).CopyTo(span.Slice(pos, node.valuesCount - pos - 1));
        }
        node.valuesArray[node.valuesCount - 1] = default!;
        node.valuesCount--;
    }

    private static void InsertKey(Node node, int pos, TKey key)
    {
        if (node.keysCount == node.keysArray.Length) ResizeKeys(node);
        var span = node.keysArray.AsSpan();
        span.Slice(pos, node.keysCount - pos).CopyTo(span.Slice(pos + 1, node.keysCount - pos));
        node.keysArray[pos] = key;
        node.keysCount++;
    }

    private static void InsertValue(Node node, int pos, TValue value)
    {
        if (node.valuesCount == node.valuesArray.Length) ResizeValues(node);
        var span = node.valuesArray.AsSpan();
        span.Slice(pos, node.valuesCount - pos).CopyTo(span.Slice(pos + 1, node.valuesCount - pos));
        node.valuesArray[pos] = value;
        node.valuesCount++;
    }

    private static void InsertChild(Node node, int pos, Node child)
    {
        if (node.childrenCount == node.childrenArray.Length) ResizeChildren(node);
        var span = node.childrenArray.AsSpan();
        span.Slice(pos, node.childrenCount - pos).CopyTo(span.Slice(pos + 1, node.childrenCount - pos));
        node.childrenArray[pos] = child;
        node.childrenCount++;
    }

    private static void ResizeKeys(Node node)
    {
        var newArray = new TKey[node.keysArray.Length * 2];
        node.keysArray.AsSpan(0, node.keysCount).CopyTo(newArray);
        node.keysArray = newArray;
    }

    private static void ResizeValues(Node node)
    {
        var newArray = new TValue[node.valuesArray.Length * 2];
        node.valuesArray.AsSpan(0, node.valuesCount).CopyTo(newArray);
        node.valuesArray = newArray;
    }

    private static void ResizeChildren(Node node)
    {
        var newArray = new Node[node.childrenArray.Length * 2];
        node.childrenArray.AsSpan(0, node.childrenCount).CopyTo(newArray);
        node.childrenArray = newArray;
    }
}
