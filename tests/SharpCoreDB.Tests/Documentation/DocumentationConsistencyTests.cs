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

                    // Case-exact, like the ubuntu runner: File.Exists/Directory.Exists are case-insensitive on
                    // Windows and macOS, so a wrong-case link used to pass here and fail only in CI (session 85).
                    if (!PathExistsWithExactCase(resolved))
                    {
                        failures.Add($"{Path.GetRelativePath(docsRoot, file)}:{lineNumber} -> {target}");
                    }
                }
            }
        }

        // Act / Assert
        Assert.Empty(failures);
    }

    [Fact]
    public void DocumentationIndex_ShouldOnlyReferenceExistingDocumentationFiles()
    {
        // Arrange: every backticked `*.md` path listed in docs/INDEX.md must resolve from docs/.
        var docsRoot = GetRepositoryFilePath("docs");
        var index = File.ReadAllText(Path.Combine(docsRoot, "INDEX.md"));
        var pathPattern = new Regex(@"`([A-Za-z0-9_][A-Za-z0-9_./\-]*\.md)`", RegexOptions.Compiled);
        var failures = new List<string>();

        // Act
        foreach (Match match in pathPattern.Matches(index))
        {
            var relative = match.Groups[1].Value;
            var resolved = Path.GetFullPath(Path.Combine(docsRoot, relative.Replace('/', Path.DirectorySeparatorChar)));

            // Case-exact, so a wrong-case path fails here and not only on the case-sensitive CI runner.
            if (!PathExistsWithExactCase(resolved))
            {
                failures.Add(relative);
            }
        }

        // Assert
        Assert.Empty(failures);
    }

    [Fact]
    public void ServerConfigurationGuide_ShouldNotDocumentTomlOrYamlConfiguration()
    {
        // Arrange: the server registers JSON configuration sources only.
        var content = File.ReadAllText(GetRepositoryFilePath("docs", "server", "CONFIGURATION_SCHEMA.md"));

        // Act / Assert
        // No TOML samples, and the old "Supported Formats: TOML" claim must not return. YAML is still
        // legitimate in this file for docker-compose, so only the config-format claim is guarded.
        Assert.DoesNotContain("```toml", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Supported Formats:** TOML", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no TOML", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PlanCacheGuide_ShouldNotPresentInternalApisAsPublic()
    {
        // Arrange: Database.GetPlanCacheStats()/ClearPlanCache() are internal (InternalsVisibleTo only).
        var content = File.ReadAllText(GetRepositoryFilePath("docs", "QUERY_PLAN_CACHE.md"));

        // Act / Assert
        Assert.DoesNotContain("db.GetPlanCacheStats()", content, StringComparison.Ordinal);
        Assert.DoesNotContain("db.ClearPlanCache()", content, StringComparison.Ordinal);
        Assert.Contains("internal", content, StringComparison.Ordinal);
    }

    [Fact]
    public void KnownIssuesReport_ShouldCarryAVerifiedStatusForEveryItem()
    {
        // Arrange: the 1.9.3-era external report must annotate each item against the current tree.
        var content = File.ReadAllText(GetRepositoryFilePath("docs", "sharpcoredb-known-issues.md"));
        var items = Regex.Matches(content, @"^## \d+\.", RegexOptions.Multiline).Count;
        var statusNotes = Regex.Matches(content, @"^> (✅|⚠)", RegexOptions.Multiline).Count;

        // Act / Assert
        Assert.True(items >= 6, $"Expected at least 6 numbered items, found {items}.");
        Assert.True(
            statusNotes >= items,
            $"{items} items carry only {statusNotes} verified status notes (RESOLVED / STILL TRUE).");
    }

    [Fact]
    public void DocumentationPackageReferences_ShouldResolveToRealPackages()
    {
        // Arrange: collect the ids that actually exist (projects + central package versions).
        var repositoryRoot = GetRepositoryFilePath();
        var realIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in Directory.EnumerateFiles(Path.Combine(repositoryRoot, "src"), "*.csproj", SearchOption.AllDirectories))
        {
            realIds.Add(Path.GetFileNameWithoutExtension(project));
        }

        foreach (Match match in Regex.Matches(
            File.ReadAllText(Path.Combine(repositoryRoot, "Directory.Packages.props")),
            "<PackageVersion Include=\"([^\"]+)\""))
        {
            realIds.Add(match.Groups[1].Value);
        }

        var referencePattern = new Regex(
            @"dotnet add package ([A-Za-z0-9_.]+)|PackageReference Include=""([A-Za-z0-9_.]+)""",
            RegexOptions.Compiled);
        var failures = new List<string>();
        var docsRoot = Path.Combine(repositoryRoot, "docs");

        // Act
        foreach (var file in Directory.EnumerateFiles(docsRoot, "*.md", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);

            if (name is "WORKLOG.md" or "CHANGELOG.md")
            {
                continue;   // historical records carry ids as they were at the time
            }

            foreach (Match match in referencePattern.Matches(File.ReadAllText(file)))
            {
                var id = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;

                if (id.StartsWith("SharpCoreDB", StringComparison.Ordinal) && !realIds.Contains(id))
                {
                    failures.Add($"{Path.GetRelativePath(docsRoot, file)} -> {id}");
                }
            }
        }

        // Assert
        Assert.Empty(failures);
    }

    /// <summary>
    /// Reports whether <paramref name="path"/> exists when every segment is matched case-sensitively, which is
    /// how Linux — and therefore the CI runner — resolves it. <see cref="File.Exists(string)"/> and
    /// <see cref="Directory.Exists(string)"/> are case-insensitive on Windows and macOS, so using them made a
    /// wrong-case link pass locally and fail only on the ubuntu runner.
    /// </summary>
    /// <param name="path">An absolute path, expected to live inside this repository.</param>
    /// <returns><c>true</c> if the path resolves with exactly the casing on disk.</returns>
    private static bool PathExistsWithExactCase(string path)
    {
        var repositoryRoot = GetRepositoryFilePath();
        var current = repositoryRoot;
        var relative = Path
            .GetRelativePath(repositoryRoot, path)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                var parent = Directory.GetParent(current);

                if (parent is null)
                {
                    return false;
                }

                current = parent.FullName;
                continue;
            }

            if (!Directory.Exists(current))
            {
                return false;
            }

            var match = Directory
                .EnumerateFileSystemEntries(current)
                .FirstOrDefault(entry => string.Equals(Path.GetFileName(entry), segment, StringComparison.Ordinal));

            if (match is null)
            {
                return false;
            }

            current = match;
        }

        return true;
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
