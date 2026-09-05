# SharpCoreDB.Aspire.Hosting

.NET Aspire hosting integration for the **SharpCoreDB network server** container image
(`ghcr.io/mpcoredeveloper/sharpcoredb-server`).

## Quick start

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var db = builder.AddSharpCoreDB("db")
    .WithServerContainer()
    .WithImageTag("2.0.0.2")
    .WithJwtSecret("replace-with-a-secret-of-at-least-32-characters");

builder.Build().Run();
```

The server container exposes two HTTPS endpoints (TLS 1.2+ is enforced, plain HTTP is not supported):

| Endpoint name | Container port | Purpose |
|---|---|---|
| `grpc` | 5001 | Primary gRPC protocol (HTTP/2) |
| `https` | 8443 | HTTPS REST API + `/health` |

The resource implements `IResourceWithConnectionString`; the connection string points at the
`grpc` endpoint (`Host=…;Port=…;SSL=True`).

## Extension methods

- `AddSharpCoreDB(name)` — adds the container resource.
- `WithServerContainer()` — documentation alias for the container-based hosting intent.
- `WithJwtSecret(secret)` — forwards `Server__Security__JwtSecretKey` (min. 32 chars).
- `WithImageTag(tag)` — pick a published image tag instead of `latest`.

## Prerequisites

- The image is published to `ghcr.io/mpcoredeveloper/sharpcoredb-server` by the repository
  Docker workflow on every `v*` tag (`linux/amd64` + `linux/arm64`).

## Development notes (TLS)

In local Aspire development the container keeps enforcing TLS. Trust the Aspire development
certificate in the consuming app, or connect over the Aspire network with certificate
validation disabled for the `grpc` endpoint. See
[docs/server/ASPIRE_INTEGRATION.md](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/server/ASPIRE_INTEGRATION.md)
for the full guide.
