// <copyright file="PerformanceTestCollection.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using Xunit;

/// <summary>
/// Collection definition to force performance tests to run serially (not in parallel).
/// This prevents resource contention (CPU, disk I/O, memory) from causing timing-based test failures.
/// xUnit runs tests in parallel by default, which causes performance tests to interfere with each other.
/// </summary>
[CollectionDefinition("PerformanceTests", DisableParallelization = true)]
public class PerformanceTestCollection
{
    // This class is never instantiated - it's just a marker for xUnit
    // All test classes with [Collection("PerformanceTests")] will run serially
}
