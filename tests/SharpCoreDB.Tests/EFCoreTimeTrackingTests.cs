using Microsoft.EntityFrameworkCore;
using SharpCoreDB.EntityFrameworkCore;

namespace SharpCoreDB.Tests;

/// <summary>
/// Tests for SharpCoreDB Entity Framework Core provider with TimeTrackingContext.
/// </summary>
public class EFCoreTimeTrackingTests : IDisposable
{
    private readonly string _testDbPath;

    public EFCoreTimeTrackingTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"efcore_test_{Guid.NewGuid()}");
    }

    [Fact]
    public void CanCreateTimeTrackingContext()
    {
        // Arrange
        var connectionString = $"Data Source={_testDbPath};Password=TestPassword123";

        // Act & Assert
        using var context = new TimeTrackingContext(connectionString);
        Assert.NotNull(context);
    }

    [Fact]
    public void CanAddTimeEntry()
    {
        // Arrange
        var connectionString = $"Data Source={_testDbPath};Password=TestPassword123";
        using var context = new TimeTrackingContext(connectionString);

        // Ensure database is created
        context.Database.EnsureCreated();

        var entry = new TimeEntry
        {
            Id = 1,
            ProjectName = "CoralTime",
            Description = "Bug fix",
            StartTime = DateTime.Now,
            EndTime = DateTime.Now.AddHours(2),
            UserId = 1
        };

        // Act
        context.TimeEntries.Add(entry);
        var result = context.SaveChanges();

        // Assert
        Assert.Equal(1, result);
    }

    [Fact(Skip = "EF provider not fully implemented: Query translation not supported yet")]
    public void CanQueryTimeEntries()
    {
        // Arrange
        var connectionString = $"Data Source={_testDbPath};Password=TestPassword123";
        using var context = new TimeTrackingContext(connectionString);
        context.Database.EnsureCreated();

        var entry = new TimeEntry
        {
            Id = 2,
            ProjectName = "CoralTime",
            Description = "Feature development",
            StartTime = new DateTime(2025, 12, 1, 9, 0, 0),
            EndTime = new DateTime(2025, 12, 1, 17, 0, 0),
            UserId = 1
        };

        context.TimeEntries.Add(entry);
        context.SaveChanges();

        // Act
        var entries = context.TimeEntries.AsEnumerable().Where(e => e.ProjectName == "CoralTime").ToList();

        // Assert
        Assert.Single(entries);
        Assert.Equal("CoralTime", entries[0].ProjectName);
        Assert.Equal("Feature development", entries[0].Description);
    }

    [Fact(Skip = "EF provider not fully implemented: Aggregates not supported yet")]
    public void CanUseLINQSumAggregation()
    {
        // Arrange
        var connectionString = $"Data Source={_testDbPath};Password=TestPassword123";
        using var context = new TimeTrackingContext(connectionString);
        context.Database.EnsureCreated();

        var entries = new[]
        {
            new TimeEntry { Id = 3, ProjectName = "Alpha", Description = "Task 1", StartTime = DateTime.Now, EndTime = DateTime.Now.AddHours(3), UserId = 1, DurationHours = 3 },
            new TimeEntry { Id = 4, ProjectName = "Alpha", Description = "Task 2", StartTime = DateTime.Now, EndTime = DateTime.Now.AddHours(5), UserId = 1, DurationHours = 5 },
            new TimeEntry { Id = 5, ProjectName = "Beta", Description = "Task 3", StartTime = DateTime.Now, EndTime = DateTime.Now.AddHours(2), UserId = 1, DurationHours = 2 }
        };

        context.TimeEntries.AddRange(entries);
        context.SaveChanges();

        // Act - Sum of duration hours for Alpha project
        var alphaHours = context.TimeEntries.AsEnumerable()
            .Where(e => e.ProjectName == "Alpha")
            .Sum(e => e.DurationHours);

        // Assert
        Assert.Equal(8, alphaHours);
    }

    [Fact(Skip = "GroupBy requires full EF Core query provider implementation - not yet supported")]
    public void CanUseLINQGroupBy()
    {
        // Arrange
        var connectionString = $"Data Source={_testDbPath};Password=TestPassword123";
        using var context = new TimeTrackingContext(connectionString);
        context.Database.EnsureCreated();

        var entries = new[]
        {
            new TimeEntry { Id = 6, ProjectName = "Project A", Description = "Task", StartTime = DateTime.Now, EndTime = DateTime.Now.AddHours(4), UserId = 1, DurationHours = 4 },
            new TimeEntry { Id = 7, ProjectName = "Project A", Description = "Task", StartTime = DateTime.Now, EndTime = DateTime.Now.AddHours(2), UserId = 1, DurationHours = 2 },
            new TimeEntry { Id = 8, ProjectName = "Project B", Description = "Task", StartTime = DateTime.Now, EndTime = DateTime.Now.AddHours(6), UserId = 1, DurationHours = 6 }
        };

        context.TimeEntries.AddRange(entries);
        context.SaveChanges();

        // Act - Group by project and count
        // NOTE: This requires full query provider implementation
        var projectCounts = context.TimeEntries
            .AsEnumerable() // Force client-side evaluation for now
            .GroupBy(e => e.ProjectName)
            .Select(g => new { Project = g.Key, Count = g.Count() })
            .ToList();

        // Assert
        Assert.Equal(2, projectCounts.Count);
        var projectA = projectCounts.First(p => p.Project == "Project A");
        Assert.Equal(2, projectA.Count);
    }

    /// <summary>
    /// Cleans up test resources.
    /// </summary>
    public void Dispose()
    {
        // Clean up test database
        if (Directory.Exists(_testDbPath))
        {
            try
            {
                Directory.Delete(_testDbPath, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Entity Framework Core DbContext for time tracking.
/// </summary>
public class TimeTrackingContext : DbContext
{
    private readonly string _connectionString;

    public TimeTrackingContext(string connectionString)
    {
        _connectionString = connectionString;
    }

    public DbSet<TimeEntry> TimeEntries => Set<TimeEntry>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSharpCoreDB(_connectionString);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TimeEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ProjectName).IsRequired();
            entity.Property(e => e.Description);
            entity.Property(e => e.StartTime).IsRequired();
            entity.Property(e => e.EndTime).IsRequired();
            entity.Property(e => e.UserId).IsRequired();
        });
    }
}

/// <summary>
/// Time entry entity for tracking work hours.
/// </summary>
public class TimeEntry
{
    public int Id { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int UserId { get; set; }
    public int DurationHours { get; set; }
}
