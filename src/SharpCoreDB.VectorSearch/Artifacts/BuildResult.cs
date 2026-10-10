// <copyright file="BuildResult.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. C# 15 union-style result (Phase 6).
// </copyright>

using System.Runtime.CompilerServices;

namespace SharpCoreDB.VectorSearch.Artifacts;

/// <summary>
/// C# 15 result for index build and verification (munarium-inspired). net11.0-only v2.1 RC line.
/// </summary>
public abstract record BuildResult
{
    public sealed record Success(string ArtifactId, long BuildTimeMs) : BuildResult;
    public sealed record VerificationFailed(string Reason, string ArtifactId) : BuildResult;
    public sealed record LimitExceeded(string LimitType, long Actual, long Max) : BuildResult;
}
