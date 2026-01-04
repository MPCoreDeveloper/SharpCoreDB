# Contributing to SharpCoreDB

Thank you for your interest in contributing to SharpCoreDB! We welcome contributions from the community.

## Code of Conduct

By participating in this project, you agree to abide by our code of conduct. Please be respectful and constructive in your interactions.

## How to Contribute

### Reporting Bugs

If you find a bug, please create an issue on GitHub with:
- A clear title and description
- Steps to reproduce the issue
- Expected vs actual behavior
- Your environment (OS, .NET version, etc.)
- Any relevant code snippets or error messages

### Suggesting Features

Feature suggestions are welcome! Please create an issue with:
- A clear description of the feature
- Use cases and benefits
- Any implementation ideas you have

### Pull Requests

1. **Fork the repository** and create a new branch from `master`
2. **Make your changes** following our coding standards
3. **Add tests** for new functionality
4. **Update documentation** if needed
5. **Ensure all tests pass** by running `dotnet test`
6. **Submit a pull request** with a clear description

#### Branch Naming Convention
- `feature/your-feature-name` for new features
- `fix/issue-description` for bug fixes
- `docs/description` for documentation updates

## Development Setup

### Prerequisites
- .NET 10 SDK or later
- Git
- A code editor (Visual Studio 2026, VS Code, or Rider)

### Getting Started

```bash
# Clone your fork
git clone https://github.com/YOUR-USERNAME/SharpCoreDB.git
cd SharpCoreDB

# Add upstream remote
git remote add upstream https://github.com/MPCoreDeveloper/SharpCoreDB.git

# Restore dependencies
dotnet restore

# Build the solution
dotnet build

# Run tests
dotnet test

# Run benchmarks (optional)
cd tests/SharpCoreDB.Benchmarks
dotnet run -c Release
```

## Project Structure

```
SharpCoreDB/
├── src/                          # Source code
│   ├── SharpCoreDB/              # Core library
│   ├── SharpCoreDB.Extensions/   # Extension methods
│   ├── SharpCoreDB.Data.Provider/# ADO.NET provider
│   ├── SharpCoreDB.EntityFrameworkCore/ # EF Core provider
│   └── SharpCoreDB.Serilog.Sinks/# Serilog sink
├── tests/                        # Tests
│   ├── SharpCoreDB.Tests/        # Unit tests
│   ├── SharpCoreDB.Benchmarks/   # Performance benchmarks
│   └── SharpCoreDB.Profiling/    # Profiling tools
└── tools/                        # Tools and utilities
    ├── SharpCoreDB.Demo/         # Demo application
    ├── SharpCoreDB.Viewer/       # Database viewer
    └── SharpCoreDB.DebugBenchmark/ # Debug benchmarking
```

## Coding Standards

### General Guidelines
- Follow the existing code style
- Use meaningful variable and method names
- Add XML documentation comments for public APIs
- Keep methods focused and concise
- Write unit tests for new functionality

### C# Style Guide
- Use C# 14 features where appropriate
- Follow .NET naming conventions
- Use `var` for local variables when type is obvious
- Place braces on new lines (Allman style)
- Use `_camelCase` for private fields (optional, follow existing pattern)

### Performance Considerations
- SharpCoreDB is performance-focused. Consider:
  - Memory allocations (use `Span<T>` and `stackalloc` where appropriate)
  - SIMD optimizations for data-parallel operations
  - Async/await for I/O operations
  - Benchmark critical paths

### Testing
- Write unit tests for all new functionality
- Aim for high code coverage
- Test edge cases and error conditions
- Use descriptive test names: `MethodName_StateUnderTest_ExpectedBehavior`

Example:
```csharp
[Fact]
public void Insert_WithValidData_ReturnsSuccess()
{
    // Arrange
    var db = CreateTestDatabase();
    
    // Act
    var result = db.Insert("users", new { id = 1, name = "Test" });
    
    // Assert
    Assert.True(result.Success);
}
```

## Commit Messages

Write clear commit messages:
- Use present tense ("Add feature" not "Added feature")
- First line: brief summary (50 chars or less)
- Add details in the body if needed
- Reference issues: "Fixes #123" or "Relates to #456"

Example:
```
Add SIMD optimization for SUM aggregation

Implement AVX2 and SSE2 vectorized paths for integer
summation, providing 8x-16x speedup over scalar code.

Fixes #123
```

## Documentation

- Update README.md for significant features
- Add XML documentation for public APIs
- Update CHANGELOG.md for all changes
- Consider adding examples for new features

## Release Process

Releases are managed by maintainers:
1. Update CHANGELOG.md
2. Update version in project files
3. Create a Git tag
4. Publish to NuGet

## Questions?

Feel free to:
- Open an issue for questions
- Join discussions in existing issues
- Contact the maintainers

## License

By contributing, you agree that your contributions will be licensed under the MIT License.

---

Thank you for contributing to SharpCoreDB! 🚀
