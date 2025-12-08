// <copyright file="IndexManager.cs" company="MPCoreDeveloper">
// Copyright (c) 2024-2025 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.DataStructures;

using System.Threading.Channels;
using System.Threading.Tasks;

/// <summary>
/// Manages asynchronous index updates using a channel and background task.
/// </summary>
public class IndexManager : IDisposable
{
    /// <summary>
    /// Record for index update operations.
    /// </summary>
    public record IndexUpdate(Dictionary<string, object> Row, IEnumerable<HashIndex> Indexes, long Position);

    /// <summary>
    /// Channel for asynchronous index updates.
    /// </summary>
    private readonly Channel<IndexUpdate> _updateQueue = Channel.CreateUnbounded<IndexUpdate>();

    /// <summary>
    /// Background task for processing index updates.
    /// </summary>
    private readonly Task _updateTask;

    /// <summary>
    /// Initializes a new instance of the <see cref="IndexManager"/> class.
    /// </summary>
    public IndexManager()
    {
        this._updateTask = Task.Run(this.UpdateIndexesAsync);
    }

    /// <summary>
    /// Queues an index update operation asynchronously.
    /// </summary>
    /// <param name="update">The index update to queue.</param>
    public void QueueUpdate(IndexUpdate update)
    {
        this._updateQueue.Writer.TryWrite(update);
    }

    /// <summary>
    /// Processes index updates asynchronously in the background.
    /// </summary>
    private async Task UpdateIndexesAsync()
    {
        await foreach (var update in this._updateQueue.Reader.ReadAllAsync())
        {
            foreach (var index in update.Indexes)
            {
                index.Add(update.Row, update.Position);
            }
        }
    }

    /// <summary>
    /// Updates indexes asynchronously for a given update.
    /// </summary>
    /// <param name="update">The index update.</param>
    public async Task UpdateIndexesAsync(IndexUpdate update)
    {
        foreach (var index in update.Indexes)
        {
            index.Add(update.Row, update.Position);
        }
        await Task.CompletedTask; // Placeholder for async
    }

    /// <summary>
    /// Disposes the index manager and completes asynchronous operations.
    /// </summary>
    public void Dispose()
    {
        this._updateQueue.Writer.Complete();
        this._updateTask?.Wait();
        GC.SuppressFinalize(this);
    }
}
