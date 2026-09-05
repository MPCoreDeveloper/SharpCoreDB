// <copyright file="SharpCoreDbAspireExtensions.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace SharpCoreDB.Aspire.Hosting;

/// <summary>
/// .NET Aspire extension methods that register a SharpCoreDB network server container.
/// </summary>
public static class SharpCoreDbAspireExtensions
{
    /// <summary>Published OCI image for the SharpCoreDB server.</summary>
    public const string ServerImage = "ghcr.io/mpcoredeveloper/sharpcoredb-server";

    /// <summary>Default image tag used when no explicit tag is supplied.</summary>
    public const string DefaultImageTag = "latest";

    /// <summary>Minimum length (in characters) required for the JWT signing secret.</summary>
    public const int MinJwtSecretLength = 32;

    /// <summary>Default HTTPS gRPC port inside the container (see <c>src/SharpCoreDB.Server/Dockerfile</c>).</summary>
    public const int DefaultGrpcTargetPort = 5001;

    /// <summary>Default HTTPS REST API port inside the container (see <c>src/SharpCoreDB.Server/Dockerfile</c>).</summary>
    public const int DefaultHttpsApiTargetPort = 8443;

    /// <summary>
    /// Adds a SharpCoreDB network server container to the Aspire application.
    /// The resource exposes a primary HTTPS gRPC endpoint (named
    /// <see cref="SharpCoreDbServerResource.GrpcEndpointName"/>, container port 5001) and an
    /// HTTPS REST API endpoint (named <see cref="SharpCoreDbServerResource.HttpsApiEndpointName"/>,
    /// container port 8443). Use <see cref="WithServerContainer"/> as a
    /// documentation-friendly alias and <see cref="WithJwtSecret"/> to configure the JWT secret.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The resource name.</param>
    /// <param name="imageTag">Optional container image tag (defaults to <c>latest</c>).</param>
    /// <param name="grpcPort">Optional fixed host port for the gRPC endpoint (default: allocated by Aspire).</param>
    /// <param name="httpsApiPort">Optional fixed host port for the HTTPS REST API endpoint (default: allocated by Aspire).</param>
    /// <returns>The SharpCoreDB server resource builder.</returns>
    public static IResourceBuilder<SharpCoreDbServerResource> AddSharpCoreDB(
        this IDistributedApplicationBuilder builder,
        string name,
        string? imageTag = null,
        int? grpcPort = null,
        int? httpsApiPort = null)
    {
        var resource = new SharpCoreDbServerResource(name);

        return builder.AddResource(resource)
            .WithImage(ServerImage)
            .WithImageTag(imageTag ?? DefaultImageTag)
            .WithHttpsEndpoint(targetPort: DefaultGrpcTargetPort, port: grpcPort, name: SharpCoreDbServerResource.GrpcEndpointName)
            .WithHttpsEndpoint(targetPort: DefaultHttpsApiTargetPort, port: httpsApiPort, name: SharpCoreDbServerResource.HttpsApiEndpointName);
    }

    /// <summary>
    /// Documentation-friendly alias that makes the container-based hosting intent explicit
    /// (compatible with the SCDMS Aspire design, <c>docs/aspire.md</c> issue #10).
    /// </summary>
    /// <param name="builder">The SharpCoreDB server resource builder.</param>
    /// <returns>The unchanged resource builder.</returns>
    public static IResourceBuilder<SharpCoreDbServerResource> WithServerContainer(
        this IResourceBuilder<SharpCoreDbServerResource> builder) => builder;

    /// <summary>
    /// Sets the JWT signing secret that is forwarded to the container as
    /// <c>Server__Security__JwtSecretKey</c>. The server refuses to start without a secret of at
    /// least <see cref="MinJwtSecretLength"/> characters.
    /// </summary>
    /// <param name="builder">The SharpCoreDB server resource builder.</param>
    /// <param name="secret">The JWT signing secret (at least <see cref="MinJwtSecretLength"/> characters).</param>
    /// <returns>The SharpCoreDB server resource builder.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="secret"/> is null, empty or shorter than <see cref="MinJwtSecretLength"/> characters.</exception>
    public static IResourceBuilder<SharpCoreDbServerResource> WithJwtSecret(
        this IResourceBuilder<SharpCoreDbServerResource> builder,
        string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        if (secret.Length < MinJwtSecretLength)
        {
            throw new ArgumentException(
                $"The JWT secret must be at least {MinJwtSecretLength} characters.", nameof(secret));
        }

        builder.Resource.JwtSecretKey = secret;
        return builder.WithEnvironment("Server__Security__JwtSecretKey", secret);
    }
}
