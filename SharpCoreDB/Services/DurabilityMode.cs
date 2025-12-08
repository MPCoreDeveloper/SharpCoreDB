// <copyright file="DurabilityMode.cs" company="MPCoreDeveloper">
// Copyright (c) 2024-2025 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
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
