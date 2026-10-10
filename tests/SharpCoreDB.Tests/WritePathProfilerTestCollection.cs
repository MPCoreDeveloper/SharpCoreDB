// <copyright file="WritePathProfilerTestCollection.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using Xunit;

/// <summary>
/// Collection definition for the test classes that drive <see cref="Diagnostics.WritePathProfiler"/>.
/// Its counters are process-wide, and every class that uses them brackets its own workload with
/// <c>Reset()</c>/<c>Enable()</c> and <c>Disable()</c> (the latter also from <c>Dispose</c>), so running two
/// such classes in parallel puts one class's <c>Disable()</c> inside the other's measured window — the
/// measured workload then records *nothing*. That is exactly what happened to
/// <c>WritePathProfilerTests.BatchUpdate_ThroughExecuteBatchSql_IsAttributed</c> when it ran beside
/// <c>WritePathEntryPointCoverageTests</c>: it read "expected &gt;= 100 in-place-patch calls, got 0" in a
/// mixed run and passed alone (session 80; red under pristine sources is kept in
/// <c>artifacts/s80-canaries-pristine.txt</c>). Serialising the profiler-driving classes removes the cause —
/// neither assertion is relaxed for it.
/// <para>
/// Deliberately **not** <c>DisableParallelization = true</c>: a shared collection already stops these two
/// classes from overlapping, and the exclusive flag costs the whole suite its parallelism — the core suite
/// went from 78,5 s to 1017,7 s with 1995 identical tests (<c>artifacts/s80-core-suite-final.txt</c>), and
/// other classes running beside these merely *add* profiler calls, which the floor assertions tolerate.
/// </para>
/// </summary>
[CollectionDefinition("WritePathProfilerTests")]
public class WritePathProfilerTestCollection;
