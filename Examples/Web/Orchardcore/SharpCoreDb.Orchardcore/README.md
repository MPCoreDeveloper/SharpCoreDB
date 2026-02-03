# OrchardCore Example (Currently Disabled)

This example project demonstrates SharpCoreDB integration with OrchardCore CMS.

## Why is this disabled?

The project file is currently renamed to `.csproj.disabled` to prevent CI build failures. This is because:

1. OrchardCore 3.0 preview packages required by this .NET 10.0 project are not consistently available from public NuGet feeds
2. OrchardCore 2.x stable versions have dependency conflicts with .NET 10.0

## How to enable this project

To use this example locally:

1. Rename `SharpCoreDb.Orchardcore.csproj.disabled` to `SharpCoreDb.Orchardcore.csproj`
2. Ensure you have access to OrchardCore 3.0 preview packages by adding the Cloudsmith feed to your NuGet.Config:
   ```xml
   <add key="orchardcore-preview" value="https://nuget.cloudsmith.io/orchardcore/preview/v3/index.json" />
   ```
3. Update the OrchardCore package reference to a compatible 3.0 preview version
4. Run `dotnet restore` and `dotnet build`

## Alternative

For a working OrchardCore integration example with stable packages, consider using OrchardCore 2.x with a .NET 8.0 target framework.
