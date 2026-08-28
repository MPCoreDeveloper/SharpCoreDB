// <copyright file="Program.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

// .NET Aspire AppHost - Orchestrates SharpCoreDB Server with Observability
var builder = DistributedApplication.CreateBuilder(args);

// Add SharpCoreDB Server
_ = builder.AddProject<Projects.SharpCoreDB_Server>("sharpcoredb-server")
    .WithHttpEndpoint(port: 8080, name: "http")
    .WithHttpsEndpoint(port: 5001, name: "grpc")
    .WithEnvironment("ASPNETCORE_URLS", "http://+:8080;https://+:5001");

// Build and run with Aspire Dashboard
builder.Build().Run();
