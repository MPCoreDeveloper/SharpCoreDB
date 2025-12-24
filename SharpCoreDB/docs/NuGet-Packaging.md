NuGet packaging guide for SharpCoreDB

Scope
- Packages: SharpCoreDB, SharpCoreDB.EntityFrameworkCore, SharpCoreDB.Extensions
- Target framework: .NET 10

Prerequisites
- Accounts: nuget.org account with API key
- Tools: .NET SDK 10+, git configured

Package metadata (add to each .csproj)
- Properties to include:
  - PackageId: unique id per project
  - Version: semantic version
  - Authors: your name or org
  - Description: short summary
  - PackageLicenseExpression: license (e.g., MIT)
  - RepositoryUrl: GitHub repo URL
  - PackageReadmeFile: README.md (placed at project root)
  - PackageTags: keywords
  - Nullable: enable
  - GenerateDocumentationFile: true (for XML docs)
  - PackageProjectUrl: project site URL (optional)
  - PackageIcon: icon file (optional; include file in project)

Example snippet for .csproj
<PropertyGroup>
  <PackageId>SharpCoreDB</PackageId>
  <Version>1.0.0</Version>
  <Authors>MPCoreDeveloper</Authors>
  <Description>Core runtime and primary API for SharpCoreDB.</Description>
  <PackageLicenseExpression>MIT</PackageLicenseExpression>
  <RepositoryUrl>https://github.com/MPCoreDeveloper/SharpCoreDB</RepositoryUrl>
  <PackageReadmeFile>README.md</PackageReadmeFile>
  <PackageTags>database;sharp;dotnet</PackageTags>
  <Nullable>enable</Nullable>
  <GenerateDocumentationFile>true</GenerateDocumentationFile>
  <PackageProjectUrl>https://github.com/MPCoreDeveloper/SharpCoreDB</PackageProjectUrl>
</PropertyGroup>

Dependencies setup
- SharpCoreDB.EntityFrameworkCore
  - Reference: SharpCoreDB
  - NuGet: Microsoft.EntityFrameworkCore (match major version used)
- SharpCoreDB.Extensions
  - Reference: SharpCoreDB
  - Avoid external dependencies unless essential

Readme per package
- Place a README.md at the root of each project with:
  - What the package does
  - Install command: dotnet add package <PackageId>
  - Basic usage snippet
  - Link to docs and repo

Packing locally
- Clean: dotnet clean
- Restore: dotnet restore
- Pack single project: dotnet pack <path to .csproj> -c Release
- Pack all (run from solution root): dotnet pack -c Release
- Artifacts: bin/Release/*.nupkg

Local testing
- Create local NuGet source: mkdir .nuget-local (once)
- Copy packages: copy bin\Release\*.nupkg .nuget-local
- Add source: dotnet nuget add source .nuget-local --name local
- Consume from a sample app: dotnet new console -n pkg-test; cd pkg-test; dotnet add package SharpCoreDB --source ../.nuget-local

Publishing to nuget.org
- Set API key: dotnet nuget add source https://api.nuget.org/v3/index.json --name nuget
- Push: dotnet nuget push bin/Release/SharpCoreDB.*.nupkg --source nuget --api-key <API_KEY>
- Repeat for other packages
- If version exists, bump Version in .csproj and re-pack

Versioning strategy
- Keep SharpCoreDB and Extensions independent where possible
- Align EF integration versions with the minimum supported EF Core version
- Use semantic versioning: MAJOR for breaking changes, MINOR for features, PATCH for fixes

CI/CD (optional)
- Use GitHub Actions with dotnet restore/build/test/pack/push
- Trigger on new tags (e.g., v1.2.3)

Validation checks
- Ensure XML docs generated
- Ensure README and license included (check .nupkg contents)
- Verify dependencies are correct and minimal

Naming and tags
- PackageId values:
  - SharpCoreDB
  - SharpCoreDB.EntityFrameworkCore
  - SharpCoreDB.Extensions
- Tags: sharp, database, orm, efcore (only for EF package), extensions

Common issues
- Missing README: set PackageReadmeFile and include README.md in project
- Icon missing: remove PackageIcon or include the file
- License: use SPDX identifiers (e.g., MIT, Apache-2.0)

Release checklist
- Update CHANGELOG.md
- Ensure tests pass
- Build succeeds with Release config
- Pack produces expected .nupkg
- Docs and samples updated
