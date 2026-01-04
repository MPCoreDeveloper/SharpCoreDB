// <copyright file="TemporaryBufferPool.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Pooling;

/// <summary>
/// Pool for temporary buffers used in key/value operations, string conversions, and intermediate data.
/// PERFORMANCE: Eliminates allocation churn for frequently used temporary buffers.
/// THREAD-SAFETY: Thread-local caching for zero-contention access.
/// MEMORY: Pools small-to-medium buffers (1KB-256KB) that would otherwise create GC pressure.
/// 
/// REFACTORED TO PARTIAL CLASSES FOR MAINTAINABILITY:
/// - TemporaryBufferPool.Core.cs: Pool management, fields, constructor, configuration
/// - TemporaryBufferPool.Operations.cs: Rent/Return operations, thread-local cache logic
/// - TemporaryBufferPool.Diagnostics.cs: Statistics, monitoring, nested types (TempBufferCache, wrappers)
/// - TemporaryBufferPool.cs (this file): Main documentation and class declaration
/// 
/// MODERN C# 14 FEATURES USED:
/// - ObjectDisposedException.ThrowIf: Modern disposal checking
/// - Target-typed new: new() for known types
/// - Collection expressions: Array initialization with []
/// - Enhanced pattern matching: is null, is not null
/// - Throw expressions: Inline throws in properties
/// - Null-forgiving operator: Safe use with validation
/// 
/// USAGE:
/// using var pool = new TemporaryBufferPool();
/// using var buffer = pool.RentSmallByteBuffer();
/// // Use buffer.ByteBuffer or buffer.AsSpan()
/// // Auto-returned on dispose
/// 
/// BUFFER SIZES:
/// - Small: 1KB (keys, small values)
/// - Medium: 8KB (typical records)
/// - Large: 64KB (large records)
/// - XLarge: 256KB (bulk operations)
/// 
/// THREAD-LOCAL CACHING:
/// - Zero contention for common buffer sizes
/// - Automatic cache management per thread
/// - Configurable cache capacity
/// </summary>
public partial class TemporaryBufferPool : IDisposable
{
    // This file intentionally left minimal.
    // All functionality is implemented in partial class files:
    // - TemporaryBufferPool.Core.cs
    // - TemporaryBufferPool.Operations.cs
    // - TemporaryBufferPool.Diagnostics.cs
}
