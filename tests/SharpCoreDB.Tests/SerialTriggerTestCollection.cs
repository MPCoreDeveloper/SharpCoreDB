// <copyright file="SerialTriggerTestCollection.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using Xunit;

/// <summary>
/// Collection definition for trigger tests that share the static
/// SqlParser._triggers registry. Tests in this collection run serially
/// to prevent race conditions when one test class clears all triggers
/// while another expects them to exist.
/// </summary>
[CollectionDefinition("SerialTriggerTests", DisableParallelization = true)]
public class SerialTriggerTestCollection;
