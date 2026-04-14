// <copyright file="Program.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using BenchmarkDotNet.Running;
using SharpCoreDB.CQRS.Benchmarks;

// Run: dotnet run -c Release -- --filter *CommandDispatch*
// Or:  dotnet run -c Release          (runs all benchmarks in this project)
BenchmarkRunner.Run<CommandDispatchBenchmark>(args: args);
