# SharpCoreDB Examples

This document provides practical examples for using SharpCoreDB in time-tracking, invoicing, and project management applications.

## ⚠️ Security Notice

**Important:** The examples in this document use string interpolation for clarity and simplicity. In production applications:

1. **Always sanitize and validate all user input** before using it in SQL statements
2. **Escape single quotes** in string values: `userInput.Replace("'", "''")`
3. **Validate input types, lengths, and formats**
4. **Use allowlists** for identifiers (table names, column names)
5. **Never trust user input** in SQL statements

SharpCoreDB currently does not support parameterized queries. Future versions will add this security feature.

## Time Tracking Application Example

### Schema Setup

```csharp
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.Services;

// Initialize database
var services = new ServiceCollection();
services.AddSharpCoreDB();
var provider = services.BuildServiceProvider();
var factory = provider.GetRequiredService<DatabaseFactory>();

var db = factory.Create("timetracker.db", "secure_password");

// Create tables for time tracking
db.ExecuteSQL(@"CREATE TABLE projects (
    id INTEGER PRIMARY KEY, 
    name TEXT, 
    client TEXT, 
    hourly_rate DECIMAL, 
    created DATETIME
)");

db.ExecuteSQL(@"CREATE TABLE time_entries (
    id ULID PRIMARY KEY AUTO, 
    project_id INTEGER, 
    user_id INTEGER,
    task TEXT, 
    start_time DATETIME, 
    end_time DATETIME, 
    duration INTEGER,
    billable BOOLEAN,
    notes TEXT
)");

db.ExecuteSQL(@"CREATE TABLE users (
    id INTEGER PRIMARY KEY, 
    username TEXT, 
    email TEXT, 
    full_name TEXT,
    created DATETIME
)");

// Create indexes for better query performance
db.ExecuteSQL("CREATE INDEX idx_time_entries_project ON time_entries (project_id)");
db.ExecuteSQL("CREATE INDEX idx_time_entries_user ON time_entries (user_id)");
db.ExecuteSQL("CREATE INDEX idx_time_entries_start ON time_entries (start_time)");
```

### Recording Time Entries

```csharp
// Start a time entry
db.ExecuteSQL(@"INSERT INTO time_entries (project_id, user_id, task, start_time, billable, notes) 
                VALUES ('1', '101', 'Feature Development', NOW(), 'true', 'Working on user authentication')");

// Stop a time entry and calculate duration
db.ExecuteSQL(@"UPDATE time_entries 
                SET end_time = NOW(), 
                    duration = DATEDIFF(NOW(), start_time, 'minutes')
                WHERE id = 'entry_id_here'");
```

### Querying Time Data

```csharp
// Get today's time entries
db.ExecuteSQL(@"SELECT * FROM time_entries 
                WHERE DATE(start_time) = DATE(NOW())");

// Get time entries for a specific project this week
db.ExecuteSQL(@"SELECT * FROM time_entries 
                WHERE project_id = '1' 
                AND start_time >= DATEADD(NOW(), -7, 'days')");

// Calculate total hours by project this month
db.ExecuteSQL(@"SELECT project_id, SUM(duration) as total_minutes 
                FROM time_entries 
                WHERE start_time >= DATEADD(NOW(), -30, 'days')
                GROUP BY project_id");

// Get billable vs non-billable hours
db.ExecuteSQL(@"SELECT billable, SUM(duration) as total_minutes 
                FROM time_entries 
                GROUP BY billable");
```

### Reporting Queries

```csharp
// Weekly report by user
db.ExecuteSQL(@"SELECT 
    users.full_name,
    COUNT(*) as entry_count,
    SUM(duration) as total_minutes,
    AVG(duration) as avg_minutes
FROM time_entries
JOIN users ON time_entries.user_id = users.id
WHERE start_time >= DATEADD(NOW(), -7, 'days')
GROUP BY users.full_name");

// Project profitability report
db.ExecuteSQL(@"SELECT 
    projects.name,
    projects.hourly_rate,
    SUM(time_entries.duration) / 60.0 as total_hours,
    (SUM(time_entries.duration) / 60.0) * projects.hourly_rate as total_revenue
FROM time_entries
JOIN projects ON time_entries.project_id = projects.id
WHERE time_entries.billable = 'true'
GROUP BY projects.name, projects.hourly_rate");
```

## Using Connection Pooling for Web Applications

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.Services;

var builder = WebApplication.CreateBuilder(args);

// Register SharpCoreDB with connection pooling
builder.Services.AddSharpCoreDB();
builder.Services.AddSingleton<DatabasePool>(sp => 
    new DatabasePool(sp, maxPoolSize: 20));

var app = builder.Build();

// Example API endpoint
// WARNING: This is a simplified example. In production, you MUST:
// 1. Validate and sanitize all user input
// 2. Use parameterized queries or proper escaping
// 3. Implement authentication and authorization
app.MapPost("/api/time-entries", async (DatabasePool pool, TimeEntryRequest request) =>
{
    var db = pool.GetDatabase("timetracker.db", "secure_password");
    
    try
    {
        // Production-ready input validation and sanitization
        // 1. Validate ProjectId and UserId are positive integers
        if (request.ProjectId <= 0 || request.UserId <= 0)
            return Results.BadRequest("Invalid project or user ID");
        
        // 2. Sanitize task description
        if (string.IsNullOrWhiteSpace(request.Task))
            return Results.BadRequest("Task description is required");
        
        if (request.Task.Length > 500)
            return Results.BadRequest("Task description too long");
        
        // 3. Escape single quotes for SQL safety
        var sanitizedTask = request.Task.Replace("'", "''");
        
        // 4. Validate timestamp format
        var formattedTime = request.StartTime.ToString("yyyy-MM-dd HH:mm:ss");
        
        db.ExecuteSQL($@"INSERT INTO time_entries 
            (project_id, user_id, task, start_time, billable) 
            VALUES ('{request.ProjectId}', '{request.UserId}', '{sanitizedTask}', 
                    '{formattedTime}', '{request.Billable}')");
        
        return Results.Ok(new { success = true });
    }
    finally
    {
        pool.ReturnDatabase(db);
    }
});

app.Run();

public record TimeEntryRequest(int ProjectId, int UserId, string Task, DateTime StartTime, bool Billable);
```

## Using Auto Maintenance

```csharp
using SharpCoreDB.Services;

// Set up automatic database maintenance
var db = factory.Create("timetracker.db", "password");
using var maintenance = new AutoMaintenanceService(
    db,
    intervalSeconds: 300,    // Run maintenance every 5 minutes
    writeThreshold: 1000     // Or after 1000 write operations
);

// Track writes in your application
void LogTimeEntry(string sql)
{
    db.ExecuteSQL(sql);
    maintenance.IncrementWriteCount();
}

// Maintenance happens automatically in the background
// Manual trigger if needed
maintenance.TriggerMaintenance();
```

## Dapper Integration Example

```csharp
using SharpCoreDB.Extensions;
using Dapper;

// Define models
public class Project
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Client { get; set; }
    public decimal HourlyRate { get; set; }
    public DateTime Created { get; set; }
}

public class TimeEntry
{
    public string Id { get; set; }
    public int ProjectId { get; set; }
    public int UserId { get; set; }
    public string Task { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int Duration { get; set; }
    public bool Billable { get; set; }
}

// Use Dapper for queries
var connection = db.GetDapperConnection();

// Query with Dapper
var projects = connection.Query<Project>("SELECT * FROM projects");

// Parameterized queries (basic implementation)
// Note: Full parameter support requires extending the implementation
var entries = connection.Query<TimeEntry>(
    "SELECT * FROM time_entries WHERE project_id = '1'");
```

## Health Monitoring Setup

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add database with health checks
builder.Services.AddSharpCoreDB();
builder.Services.AddHealthChecks()
    .AddSharpCoreDB(
        db, 
        name: "database",
        testQuery: "SELECT COUNT(*) FROM users",
        tags: new[] { "db", "ready" }
    );

var app = builder.Build();

// Health check endpoints
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.Run();
```

## Invoice Generation Example

```csharp
// Create invoices table
db.ExecuteSQL(@"CREATE TABLE invoices (
    id INTEGER PRIMARY KEY,
    client TEXT,
    invoice_date DATETIME,
    due_date DATETIME,
    total_amount DECIMAL,
    status TEXT
)");

db.ExecuteSQL(@"CREATE TABLE invoice_items (
    id INTEGER PRIMARY KEY,
    invoice_id INTEGER,
    description TEXT,
    hours DECIMAL,
    rate DECIMAL,
    amount DECIMAL
)");

// Generate invoice from time entries
db.ExecuteSQL(@"INSERT INTO invoices (id, client, invoice_date, due_date, status)
                VALUES ('1001', 'Acme Corp', NOW(), DATEADD(NOW(), 30, 'days'), 'draft')");

// Add invoice items from billable time entries
db.ExecuteSQL(@"INSERT INTO invoice_items (invoice_id, description, hours, rate, amount)
                SELECT 
                    '1001',
                    GROUP_CONCAT(task, ', '),
                    SUM(duration) / 60.0,
                    '150.00',
                    (SUM(duration) / 60.0) * 150.00
                FROM time_entries
                WHERE project_id = '1' 
                AND billable = 'true'
                AND start_time >= '2024-01-01'
                AND start_time < '2024-02-01'");

// Calculate invoice total
db.ExecuteSQL(@"UPDATE invoices
                SET total_amount = (
                    SELECT SUM(amount) 
                    FROM invoice_items 
                    WHERE invoice_id = '1001'
                )
                WHERE id = '1001'");

## Console Application with Dependency Injection

This example demonstrates setting up SharpCoreDB in a console application using Microsoft.Extensions.DependencyInjection.

```csharp
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;

class Program
{
    static void Main(string[] args)
    {
        // Set up dependency injection
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var serviceProvider = services.BuildServiceProvider();

        // Get database factory
        var factory = serviceProvider.GetRequiredService<DatabaseFactory>();

        // Create database
        var db = factory.Create("console.db", "secure_password");

        // Create table
        db.ExecuteSQL("CREATE TABLE messages (id INTEGER PRIMARY KEY, content TEXT, timestamp DATETIME)");

        // Insert data
        db.ExecuteSQL("INSERT INTO messages VALUES (1, 'Hello from SharpCoreDB!', NOW())");

        // Query data
        var result = db.ExecuteSQL("SELECT * FROM messages");
        Console.WriteLine("Database contents:");
        Console.WriteLine(result);

        // Async operations
        Task.Run(async () =>
        {
            await db.ExecuteSQLAsync("INSERT INTO messages VALUES (2, 'Async message', NOW())");
            var asyncResult = await db.ExecuteSQLAsync("SELECT COUNT(*) FROM messages");
            Console.WriteLine($"Total messages: {asyncResult}");
        }).Wait();

        Console.WriteLine("Console app completed successfully!");
    }
}
```

To run this example:
1. Create a new console project: `dotnet new console`
2. Add SharpCoreDB: `dotnet add package SharpCoreDB`
3. Replace Program.cs with the code above
4. Run: `dotnet run`

## Entity Framework Core Migration Example

SharpCoreDB supports EF Core with full migration capabilities. Here's how to set up migrations:

### 1. Create EF Core Project

```bash
dotnet new classlib -n MyApp.Data
cd MyApp.Data
dotnet add package Microsoft.EntityFrameworkCore.Design
dotnet add package SharpCoreDB.EntityFrameworkCore
```

### 2. Define DbContext

```csharp
using Microsoft.EntityFrameworkCore;
using SharpCoreDB.EntityFrameworkCore;

public class AppDbContext : DbContext
{
    private readonly string _connectionString;

    public AppDbContext(string connectionString)
    {
        _connectionString = connectionString;
    }

    public DbSet<User> Users { get; set; }
    public DbSet<Project> Projects { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSharpCoreDB(_connectionString);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired();
            entity.Property(e => e.Email).IsRequired();
        });

        modelBuilder.Entity<Project>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired();
            entity.HasMany(e => e.Users).WithMany(e => e.Projects);
        });
    }
}

public class User
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
    public ICollection<Project> Projects { get; set; }
}

public class Project
{
    public int Id { get; set; }
    public string Name { get; set; }
    public ICollection<User> Users { get; set; }
}
```

### 3. Add Migration

```bash
# Install EF Core tools globally if not already installed
dotnet tool install --global dotnet-ef

# Add migration
dotnet ef migrations add InitialCreate --project MyApp.Data --startup-project MyApp.Console

# Update database
dotnet ef database update --project MyApp.Data --startup-project MyApp.Console
```

### 4. Use in Application

```csharp
using Microsoft.Extensions.DependencyInjection;

class Program
{
    static void Main()
    {
        var connectionString = "Data Source=app.db;Password=MySecret123";
        using var context = new AppDbContext(connectionString);

        // Ensure database is created
        context.Database.EnsureCreated();

        // Add data
        context.Users.Add(new User { Name = "Alice", Email = "alice@example.com" });
        context.SaveChanges();

        // Query data
        var users = context.Users.ToList();
        foreach (var user in users)
        {
            Console.WriteLine($"{user.Name} - {user.Email}");
        }
    }
}
```

### Migration Commands

```bash
# Add new migration
dotnet ef migrations add AddUserProjects

# Apply migrations
dotnet ef database update

# Revert last migration
dotnet ef database update LastMigrationName

# Remove last migration (if not applied)
dotnet ef migrations remove

# Generate SQL script
dotnet ef migrations script --output migration.sql
```

## Performance Tuning Guide

### 1. Database Configuration

Choose the right configuration for your use case:

```csharp
// High-performance mode (no encryption, trusted environments)
var config = DatabaseConfig.HighPerformance;
var db = factory.Create(dbPath, password, false, config);

// Default encrypted mode
var config = DatabaseConfig.Default;
var db = factory.Create(dbPath, password, false, config);

// Custom configuration
var config = new DatabaseConfig 
{ 
    EnableQueryCache = true,     // Cache repeated queries
    QueryCacheSize = 1000,       // Cache size
    EnableHashIndexes = true,    // Enable hash indexes
    WalBufferSize = 1024 * 1024, // 1MB WAL buffer
    UseBufferedIO = true         // Buffered I/O
};
```

### 2. Indexing Strategy

Create indexes on frequently queried columns:

```csharp
// Hash indexes for O(1) lookups
db.ExecuteSQL("CREATE INDEX idx_user_email ON users (email)");
db.ExecuteSQL("CREATE INDEX idx_orders_status ON orders (status)");

// Composite indexes
db.ExecuteSQL("CREATE INDEX idx_time_user_date ON time_entries (user_id, start_time)");
