// <copyright file="SharpCoreDbServerResource.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Aspire.Hosting.ApplicationModel;

namespace SharpCoreDB.Aspire.Hosting;

/// <summary>
/// A container resource representing the SharpCoreDB network server image.
/// The server always exposes two HTTPS endpoints: a primary gRPC endpoint
/// (container port 5001) and an HTTPS REST API endpoint (container port 8443).
/// Plain HTTP is not supported by the server (TLS 1.2+ is enforced).
/// </summary>
public sealed class SharpCoreDbServerResource(string name)
    : ContainerResource(name), IResourceWithConnectionString
{
    /// <summary>Name of the primary HTTPS gRPC endpoint.</summary>
    public const string GrpcEndpointName = "grpc";

    /// <summary>Name of the HTTPS REST API endpoint.</summary>
    public const string HttpsApiEndpointName = "https";

    /// <summary>
    /// Gets or sets the JWT signing secret forwarded to the container as
    /// <c>Server__Security__JwtSecretKey</c>. Must be at least 32 characters.
    /// </summary>
    public string? JwtSecretKey { get; set; }

    /// <inheritdoc />
    public ReferenceExpression ConnectionStringExpression =>
        ReferenceExpression.Create(
            $"Host={GrpcEndpoint.Property(EndpointProperty.Host)};Port={GrpcEndpoint.Property(EndpointProperty.Port)};SSL=True");

    /// <summary>Gets a reference to the HTTPS gRPC endpoint of the container.</summary>
    public EndpointReference GrpcEndpoint => new(this, GrpcEndpointName);

    /// <summary>Gets a reference to the HTTPS REST API endpoint of the container.</summary>
    public EndpointReference HttpsApiEndpoint => new(this, HttpsApiEndpointName);
}
