# SharpCoreDB Server × .NET Aspire Integration

> **Status:** SharpCoreDB-side prerequisites implemented · **Date:** 2026-09-05
>
> This repository delivers the two SharpCoreDB prerequisites defined by the SCDMS design doc
> [`docs/aspire.md`](https://github.com/MPCoreDeveloper/SCDMS/blob/main/docs/aspire.md)
> (SCDMS issue #10). Once the `SCDMS.Aspire.Hosting` package is built in the SCDMS repository,
> SharpCoreDB server + SCDMS can run as one Aspire application with all SCDMS ⇄ SharpCoreDB
> traffic over **gRPC**.

## Goal (from the SCDMS design doc)

Run SharpCoreDB server + SCDMS as a single Aspire app, like pgAdmin next to PostgreSQL:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var sharpCoreDb = builder.AddSharpCoreDB("db")
                         .WithServerContainer();      // SharpCoreDB server container

builder.AddSCDMS("admin")
       .WithGrpcReference(sharpCoreDb)                // SCDMS container linked via gRPC
       .WithHttpEndpoint(port: 8080, name: "http");

builder.Build().Run();
```

## What this repository now provides

### 1. Server container image → `ghcr.io/mpcoredeveloper/sharpcoredb-server`

- `.github/workflows/docker-publish.yml` builds and pushes the server image on every `v*` tag
  and via `workflow_dispatch`.
- Multi-architecture: `linux/amd64` + `linux/arm64` (Docker Buildx + QEMU).
- Tags: `<version>` (tag without the leading `v`) and `latest`.
- Build context is the repository root; the Dockerfile lives at
  `src/SharpCoreDB.Server/Dockerfile`.

### 2. `SharpCoreDB.Aspire.Hosting` NuGet package

The package (project `src/SharpCoreDB.Aspire.Hosting/`) registers the server container in an
Aspire application:

```csharp
using SharpCoreDB.Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var db = builder.AddSharpCoreDB("db")        // ghcr.io/mpcoredeveloper/sharpcoredb-server:latest
    .WithServerContainer()
    .WithImageTag("2.0.0.2")                 // optional pinned tag
    .WithJwtSecret("a-secret-of-at-least-32-characters");

builder.Build().Run();
```

The resource (`SharpCoreDbServerResource`) implements `IResourceWithConnectionString` and always
exposes **two** HTTPS endpoints:

| Endpoint | Container port | Purpose |
|---|---|---|
| `grpc` | 5001 | Primary gRPC protocol (HTTP/2), matching `Server.GrpcPort` |
| `https` | 8443 | HTTPS REST API + `/health`, matching `Server.HttpsApiPort` |

The connection string of the resource points at the gRPC endpoint:
`Host=<host>;Port=<port>;SSL=True`.

## TLS in development (important)

SharpCoreDB server **enforces TLS 1.2+** and refuses to start on plain HTTP
(`Server.Security.TlsEnabled` must stay `true` and a certificate path is required). Both
endpoints are therefore modeled with `WithHttpsEndpoint` in the hosting package.

- **Production / behind a reverse proxy:** terminate TLS at a publicly trusted proxy (see the
  SCDMS `samples/docker/` pattern) and mount the real certificate into `/app/certs`.
- **Local Aspire development:** the container still needs a certificate. Use the Aspire
  development certificate (trust it in the consuming client) or mount a development PFX and
  point `Server__Security__TlsCertificatePath` at it. gRPC clients in development may need
  certificate validation disabled for the local `grpc` endpoint.

## Environment configuration

| Setting | Container env var | Notes |
|---|---|---|
| JWT secret | `Server__Security__JwtSecretKey` | Min. 32 chars; use `WithJwtSecret(...)` |
| TLS cert | `Server__Security__TlsCertificatePath` | Default `./certs/server.pfx`; mount into `/app/certs` |
| HTTPS API | `Server__EnableHttpsApi` / `Server__HttpsApiPort` | Defaults `true` / `8443` |
| gRPC | `Server__EnableGrpc` / `Server__GrpcPort` | Defaults `true` / `5001` |

## Consuming the resource from another hosting package

`WithGrpcReference` (to be added by `SCDMS.Aspire.Hosting`) can resolve the gRPC endpoint and
forward `SCDMS__DefaultServer*` settings:

```csharp
var grpcEndpoint = sharpCoreDb.GetEndpoint("grpc");
scdms.WithEnvironment("SCDMS__DefaultServerHost", grpcEndpoint)
     .WithEnvironment("SCDMS__DefaultServerPort", grpcEndpoint.Property(EndpointProperty.Port))
     .WithEnvironment("SCDMS__DefaultServerUseSsl", "true");
```

## Related

- Server quick start: [`QUICKSTART.md`](QUICKSTART.md)
- Server configuration reference: [`CONFIGURATION_SCHEMA.md`](CONFIGURATION_SCHEMA.md)
- Server Docker Compose: `src/SharpCoreDB.Server/docker-compose.yml`
- SCDMS Aspire design doc (the plan this implements):
  https://github.com/MPCoreDeveloper/SCDMS/blob/main/docs/aspire.md
