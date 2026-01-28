// <copyright file="AssemblyInfo.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using System.Runtime.CompilerServices;

// ✅ Phase 1: Allow test assemblies to access internal members for performance testing
[assembly: InternalsVisibleTo("SharpCoreDB.Tests")]
[assembly: InternalsVisibleTo("SharpCoreDB.Benchmarks")]
