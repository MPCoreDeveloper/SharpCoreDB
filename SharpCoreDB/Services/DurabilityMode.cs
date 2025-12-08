// <copyright file="DurabilityMode.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace SharpCoreDB.Services;

/// <summary>
/// Defines the durability modes for WAL commits.
/// </summary>
public enum DurabilityMode
{
    /// <summary>
    /// Full synchronous durability - uses FileStream.Flush(true) to force data to physical disk.
    /// Guarantees data survives system crashes and power failures.
    /// Slower but most durable.
    /// </summary>
    FullSync,

    /// <summary>
    /// Asynchronous durability - relies on OS buffering and periodic flushing.
    /// Faster but may lose recent commits on crash.
    /// </summary>
    Async,
}
