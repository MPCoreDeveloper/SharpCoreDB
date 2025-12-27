# SharpCoreDB.Serilog.Sinks - NuGet Package Build Guide

## Package Includes Logo

The NuGet package now includes the SharpCoreDB logo (SharpCoreDB.jpg) which will be displayed on NuGet.org.

## Logo Location

The logo is referenced from the main SharpCoreDB project:
```
..\SharpCoreDB\SharpCoreDB.jpg
```

If the logo file doesn't exist, the build will still succeed (the Condition attribute ensures this).

## Creating the NuGet Package

### Option 1: Visual Studio

1. Right-click on `SharpCoreDB.Serilog.Sinks` project in Solution Explorer
2. Click **Pack**
3. Package will be created in `bin\Release\SharpCoreDB.Serilog.Sinks.1.0.0.nupkg`

### Option 2: Command Line

```bash
cd SharpCoreDB.Serilog.Sinks
dotnet pack -c Release
```

## Verifying Package Contents

### Using NuGet Package Explorer

1. Download [NuGet Package Explorer](https://github.com/NuGetPackageExplorer/NuGetPackageExplorer)
2. Open `bin\Release\SharpCoreDB.Serilog.Sinks.1.0.0.nupkg`
3. Verify the following files are included:
   - ? `SharpCoreDB.jpg` (in root)
   - ? `README.md` (in root)
   - ? `lib/net10.0/SharpCoreDB.Serilog.Sinks.dll`
   - ? `lib/net10.0/SharpCoreDB.Serilog.Sinks.xml` (documentation)

### Using Command Line

```bash
# Extract package contents to temp folder
Expand-Archive bin\Release\SharpCoreDB.Serilog.Sinks.1.0.0.nupkg -DestinationPath temp

# Check if logo exists
dir temp\SharpCoreDB.jpg

# Check if README exists
dir temp\README.md

# Cleanup
Remove-Item -Path temp -Recurse -Force
```

## What Will Appear on NuGet.org

When you publish this package to NuGet.org, users will see:

1. **Logo**: SharpCoreDB.jpg displayed prominently
2. **README**: Full documentation with usage examples
3. **Package Metadata**:
   - Title: SharpCoreDB.Serilog.Sinks
   - Description: Serilog sink with encryption and batching
   - Tags: serilog, sink, sharpcoredb, database, logging, encryption, batch, async, net10
   - License: MIT

## Publishing to NuGet.org

```bash
dotnet nuget push bin\Release\SharpCoreDB.Serilog.Sinks.1.0.0.nupkg `
  --source https://api.nuget.org/v3/index.json `
  --api-key YOUR_API_KEY
```

**Get API key**: https://www.nuget.org/account/apikeys

## Package Structure

```
SharpCoreDB.Serilog.Sinks.1.0.0.nupkg
??? SharpCoreDB.jpg                                    # Logo
??? README.md                                          # Documentation
??? lib/
?   ??? net10.0/
?       ??? SharpCoreDB.Serilog.Sinks.dll             # Assembly
?       ??? SharpCoreDB.Serilog.Sinks.xml             # XML documentation
??? .signature.p7s                                     # NuGet signature
```

## Troubleshooting

### Logo Not Included

If the logo is missing from the package:

1. Check if `SharpCoreDB.jpg` exists in `..SharpCoreDB\` folder
2. If missing, either:
   - Add the logo file to the SharpCoreDB project
   - Or copy it to `SharpCoreDB.Serilog.Sinks\SharpCoreDB.jpg`
   - Update `.csproj`:
     ```xml
     <None Include="SharpCoreDB.jpg" Pack="true" PackagePath="/" />
     ```

### README Not Displaying on NuGet.org

If README doesn't show:
- Ensure `PackageReadmeFile` is set in `.csproj`
- Verify `README.md` is included in package (check with Package Explorer)
- NuGet.org may take a few minutes to process and display README

### Logo Not Displaying on NuGet.org

If logo doesn't show:
- Ensure `PackageIcon` is set in `.csproj`
- Verify `SharpCoreDB.jpg` is in package root (not in subdirectory)
- Logo must be JPEG or PNG format
- Recommended size: 128x128 to 200x200 pixels
- NuGet.org caches images - may take up to 1 hour to update

## Best Practices

1. **Logo Dimensions**: Keep logo square (200x200 recommended)
2. **Logo Size**: Keep under 1MB (ideally under 100KB)
3. **Logo Format**: Use JPEG or PNG
4. **README**: Keep concise with practical examples
5. **Version**: Use semantic versioning (1.0.0, 1.0.1, etc.)
6. **Tags**: Use relevant keywords for discoverability
7. **Documentation**: Always include XML documentation comments

## Updates

To update logo or README after publishing:

1. Update the files locally
2. Increment version number in `.csproj`
3. Rebuild and pack
4. Push new version to NuGet.org

**Note**: You cannot replace an existing version on NuGet.org. Always increment the version number.

## Example NuGet Package Page

When published, your package page will look like:

```
???????????????????????????????????????????
?    [SharpCoreDB Logo]                   ?
?                                         ?
? SharpCoreDB.Serilog.Sinks               ?
? v1.0.0                                  ?
?                                         ?
? A Serilog sink for SharpCoreDB...      ?
?                                         ?
? Install:                                ?
? dotnet add package SharpCoreDB...      ?
?                                         ?
? [README TAB] [Dependencies] [Versions] ?
?                                         ?
? Full README.md content displays here... ?
???????????????????????????????????????????
```

## Additional Resources

- **NuGet Docs**: https://docs.microsoft.com/nuget/
- **Package Icon Guide**: https://docs.microsoft.com/nuget/reference/nuspec#icon
- **Package README**: https://docs.microsoft.com/nuget/nuget-org/package-readme-on-nuget-org
- **SharpCoreDB Repo**: https://github.com/MPCoreDeveloper/SharpCoreDB

---

**Status**: ? Package configuration complete with logo support  
**Ready to publish**: Yes  
**Logo included**: Yes (via reference to main project)  
**README included**: Yes  
**XML documentation**: Yes
