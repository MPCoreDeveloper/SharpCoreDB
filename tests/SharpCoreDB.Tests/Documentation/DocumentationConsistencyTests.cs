namespace SharpCoreDB.Tests.Documentation;

public sealed class DocumentationConsistencyTests
{
    [Fact]
    public void DocumentationIndex_ShouldReferenceCurrentMaintainedPackageGuides()
    {
        // Arrange
        var index = File.ReadAllText(GetRepositoryFilePath("docs", "INDEX.md"));

        // Act / Assert
        Assert.Contains("../src/SharpCoreDB.EntityFrameworkCore/USAGE.md", index, StringComparison.Ordinal);
        Assert.Contains("../src/SharpCoreDB.Functional.Dapper/README.md", index, StringComparison.Ordinal);
        Assert.Contains("../src/SharpCoreDB.Functional.EntityFrameworkCore/README.md", index, StringComparison.Ordinal);
        Assert.Contains("../src/SharpCoreDB.Identity/README.md", index, StringComparison.Ordinal);
        Assert.Contains("../src/SharpCoreDB.Serilog.Sinks/README.md", index, StringComparison.Ordinal);
    }

    [Fact]
    public void DocumentationHubs_ShouldUseCurrentReleaseLabel()
    {
        // Arrange
        var files = new[]
        {
            GetRepositoryFilePath("README.md"),
            GetRepositoryFilePath("docs", "INDEX.md"),
            GetRepositoryFilePath("docs", "README.md"),
            GetRepositoryFilePath("src", "SharpCoreDB.EntityFrameworkCore", "README.md"),
            GetRepositoryFilePath("src", "SharpCoreDB.EntityFrameworkCore", "USAGE.md"),
        };

        // Act / Assert
        foreach (var file in files)
        {
            var content = File.ReadAllText(file);
            Assert.Contains("1.7.1", content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ObsoleteDocumentationFiles_ShouldBeRemoved()
    {
        // Arrange
        var obsoleteFiles = new[]
        {
            GetRepositoryFilePath("src", "SharpCoreDB", "README_NUGET.md"),
            GetRepositoryFilePath("src", "SharpCoreDB.EntityFrameworkCore", "USAGE_GUIDE.md"),
            GetRepositoryFilePath("src", "SharpCoreDB.EntityFrameworkCore", "VISUAL_STUDIO_GUIDE.md"),
        };

        // Act / Assert
        foreach (var file in obsoleteFiles)
        {
            Assert.False(File.Exists(file), $"Obsolete documentation file should be removed: {file}");
        }
    }

    private static string GetRepositoryFilePath(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SharpCoreDB.sln")))
            {
                return Path.Combine([current.FullName, .. segments]);
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root for documentation tests.");
    }
}
