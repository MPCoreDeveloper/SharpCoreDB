// <copyright file="BTree.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.DataStructures;

using System;
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
        public TKey[] keysArray;
        public TValue[] valuesArray;
        public Node[] childrenArray;
        public int keysCount = 0;
        public int valuesCount = 0;
        public int childrenCount = 0;

        public bool IsLeaf;

        public Node(int capacity)
        {
            keysArray = new TKey[capacity];
            valuesArray = new TValue[capacity];
            childrenArray = new Node[capacity];
        }
    }

    private Node? root;
    private readonly int degree = 3;
    private readonly int nodeCapacity;

    /// <summary>
    /// Initializes a new instance of the <see cref="BTree{TKey, TValue}"/> class.
    /// </summary>
    public BTree()
    {
        nodeCapacity = 2 * degree;
    }

    /// <inheritdoc />
    public void Insert(TKey key, TValue value)
    {
        if (this.root == null)
        {
            this.root = new Node(nodeCapacity) { IsLeaf = true };
            this.root.keysArray[0] = key;
            this.root.valuesArray[0] = value;
            this.root.keysCount = 1;
            this.root.valuesCount = 1;
            return;
        }

        if (this.root.keysCount == (2 * this.degree) - 1)
        {
            var oldRoot = this.root;
            this.root = new Node(nodeCapacity) { IsLeaf = false };
            this.root.childrenArray[0] = oldRoot;
            this.root.childrenCount = 1;
            this.SplitChild(this.root, 0);
        }

        this.InsertNonFull(this.root, key, value);
    }

    private void InsertNonFull(Node node, TKey key, TValue value)
    {
        if (node.IsLeaf)
        {
            int insertPos = FindInsertIndex(node, key);
            InsertKeyValue(node, insertPos, key, value);
        }
        else
        {
            int childIndex = FindInsertIndex(node, key);
            if (node.childrenArray[childIndex].keysCount == (2 * this.degree) - 1)
            {
                this.SplitChild(node, childIndex);
                if (key.CompareTo(node.keysArray[childIndex]) > 0)
                {
                    childIndex++;
                }
            }

            this.InsertNonFull(node.childrenArray[childIndex], key, value);
        }
    }

    private static int FindInsertIndex(Node node, TKey key)
    {
        int low = 0;
        int high = node.keysCount;
        while (low < high)
        {
            int mid = (low + high) >> 1;
            if (key.CompareTo(node.keysArray[mid]) > 0)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        return low;
    }

    private static void InsertKeyValue(Node node, int pos, TKey key, TValue value)
    {
        if (node.keysCount == node.keysArray.Length) ResizeKeys(node);
        if (node.valuesCount == node.valuesArray.Length) ResizeValues(node);

        if (pos < node.keysCount)
        {
            Array.Copy(node.keysArray, pos, node.keysArray, pos + 1, node.keysCount - pos);
            Array.Copy(node.valuesArray, pos, node.valuesArray, pos + 1, node.valuesCount - pos);
        }

        node.keysArray[pos] = key;
        node.valuesArray[pos] = value;
        node.keysCount++;
        node.valuesCount++;
    }

    private void SplitChild(Node parent, int i)
    {
        var y = parent.childrenArray[i];
        var z = new Node(nodeCapacity) { IsLeaf = y.IsLeaf };
        InsertChild(parent, i + 1, z);
        InsertKey(parent, i, y.keysArray[this.degree - 1]);
        int t = this.degree;
        int copyCount = t - 1;

        Array.Copy(y.keysArray, t, z.keysArray, 0, copyCount);
        z.keysCount = copyCount;
        y.keysCount = copyCount;

        if (y.IsLeaf)
        {
            Array.Copy(y.valuesArray, t, z.valuesArray, 0, copyCount);
            z.valuesCount = copyCount;
            y.valuesCount = copyCount;
        }
        else
        {
            Array.Copy(y.childrenArray, t, z.childrenArray, 0, t);
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

    /// <inheritdoc />
    public void Clear()
    {
        this.root = null;
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
            // Key found in this node - remove it
            RemoveKeyAt(node, i);
            if (node.IsLeaf)
            {
                RemoveValueAt(node, i);
            }
            return true;
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
