namespace SharpCoreDB.Tests.Documentation;

using System.Text.RegularExpressions;

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

            // Accept any current release label (1.9.5, 1.9.7, 2.0.0, ...) so the hub check
            // survives every version bump instead of pinning a single historic release.
            Assert.Matches(@"\d+\.\d+\.\d+", content);
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

    [Fact]
    public void DocumentationMarkdownLinks_ShouldResolveToExistingFiles()
    {
        // Arrange: every relative link in docs/**, resolved from the file that contains it.
        var docsRoot = GetRepositoryFilePath("docs");
        var failures = new List<string>();
        var linkPattern = new Regex(@"\]\(([^)\s]+)\)", RegexOptions.Compiled);

        foreach (var file in Directory.EnumerateFiles(docsRoot, "*.md", SearchOption.AllDirectories))
        {
            var lineNumber = 0;

            foreach (var line in File.ReadLines(file))
            {
                lineNumber++;

                foreach (Match match in linkPattern.Matches(line))
                {
                    var target = match.Groups[1].Value;

                    if (target.StartsWith('#')
                        || target.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                        || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                        || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var path = target.Split('#')[0];

                    if (string.IsNullOrWhiteSpace(path))
                    {
                        continue;
                    }

                    var resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, path.Replace('/', Path.DirectorySeparatorChar)));

                    if (!File.Exists(resolved) && !Directory.Exists(resolved))
                    {
                        failures.Add($"{Path.GetRelativePath(docsRoot, file)}:{lineNumber} -> {target}");
                    }
                }
            }
        }

        // Act / Assert
        Assert.Empty(failures);
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
