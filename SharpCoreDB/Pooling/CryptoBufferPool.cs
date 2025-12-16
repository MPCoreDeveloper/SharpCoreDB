// <copyright file="CryptoBufferPool.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Pooling;

/// <summary>
/// Specialized buffer pool for cryptographic operations with secure cleanup.
/// SECURITY: All buffers are cleared on return to prevent key/plaintext leakage.
/// PERFORMANCE: Thread-local caching for zero-contention in high-throughput scenarios.
/// MEMORY: Reuses buffers for encryption/decryption to minimize GC pressure.
/// 
/// REFACTORED TO PARTIAL CLASSES FOR MAINTAINABILITY:
/// - CryptoBufferPool.Core.cs: Pool management, fields, constructor, disposal
/// - CryptoBufferPool.Operations.cs: Rent/Return operations with secure clearing
/// - CryptoBufferPool.Diagnostics.cs: Statistics, CryptoCache, RentedCryptoBuffer, enums
/// - CryptoBufferPool.cs (this file): Main documentation and class declaration
/// 
/// MODERN C# 14 FEATURES USED:
/// - ObjectDisposedException.ThrowIf: Modern disposal checking
/// - Target-typed new: new() for known types
/// - Enhanced pattern matching: is null, is not null, is pattern
/// - Primary constructors: CryptoCache(int capacity)
/// - Throw expressions: ?? throw for properties
/// 
/// USAGE:
/// using var pool = new CryptoBufferPool();
/// using var keyBuffer = pool.RentKeyBuffer(32);
/// // Use keyBuffer.Buffer or keyBuffer.AsSpan()
/// // Auto-cleared and returned on dispose
/// 
/// SECURITY FEATURES:
/// - CryptographicOperations.ZeroMemory: Cannot be optimized away by compiler
/// - Automatic buffer clearing on return
/// - Extra clearing for key material buffers
/// - Audit trail via BytesCleared metric
/// - Thread-local cache also securely cleared
/// 
/// BUFFER TYPES:
/// - Encryption: For plaintext input (cleared on return)
/// - Decryption: For ciphertext input (cleared on return)
/// - KeyMaterial: For keys/nonces/tags (extra clearing, highest priority)
/// - Generic: General purpose crypto buffers
/// 
/// PERFORMANCE:
/// - Thread-local caching: Zero contention
/// - Configurable cache capacity
/// - Automatic size matching
/// - Maximum buffer size: 16MB default
/// </summary>
public partial class CryptoBufferPool : IDisposable
{
    // This file intentionally left minimal.
    // All functionality is implemented in partial class files:
    // - CryptoBufferPool.Core.cs
    // - CryptoBufferPool.Operations.cs
    // - CryptoBufferPool.Diagnostics.cs
}
