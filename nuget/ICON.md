# NuGet Package Icon

Place a 128x128 or 256x256 PNG image named `icon.png` in this directory for NuGet packaging.

## Current Setup

The project currently references `SharpCoreDB.jpg` from the root. To use a proper icon:

1. Create or convert your logo to PNG format (128x128 or 256x256 pixels)
2. Name it `icon.png`
3. Place it in this `nuget/` directory
4. Update `Directory.Build.props` to reference it:
   ```xml
   <PackageIcon>icon.png</PackageIcon>
   ```
5. Add to project files that create packages:
   ```xml
   <ItemGroup>
     <None Include="..\..\nuget\icon.png" Pack="true" PackagePath="\" />
   </ItemGroup>
   ```

## Icon Requirements

- Format: PNG
- Size: 128x128 or 256x256 pixels recommended
- Max file size: 1 MB
- Should work on both light and dark backgrounds
- Transparent background recommended
