# SharpCoreDB OrchardCore Example

This example demonstrates how to use SharpCoreDB with OrchardCore CMS.

## Important Note

**This project is excluded from the CI/CD builds** because it requires OrchardCore 3.0 preview packages that target .NET 10. These preview packages may not be available on the standard NuGet feed during CI builds.

## Requirements

- .NET 10.0 or later
- OrchardCore 3.0.0-preview-18884 (targets .NET 10)
- Access to OrchardCore preview NuGet feed (if needed)

## Building Locally

To build this project locally, you may need to add the OrchardCore preview feed:

```bash
dotnet nuget add source https://nuget.cloudsmith.io/orchardcore/preview/v3/index.json --name "OrchardCore Preview"
```

Then restore and build:

```bash
dotnet restore Examples/Web/Orchardcore/SharpCoreDb.Orchardcore/SharpCoreDb.Orchardcore.csproj
dotnet build Examples/Web/Orchardcore/SharpCoreDb.Orchardcore/SharpCoreDb.Orchardcore.csproj
```

## CI/CD

The main SharpCoreDB solution uses a solution filter (`SharpCoreDB.CI.slnf`) for CI builds which excludes this example project to prevent build failures when preview packages are unavailable.

For local development, you can continue to use the full `SharpCoreDB.sln` solution.
