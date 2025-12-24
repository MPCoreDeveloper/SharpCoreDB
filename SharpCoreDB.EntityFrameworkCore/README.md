# SharpCoreDB.EntityFrameworkCore

Entity Framework Core provider voor SharpCoreDB - de lightweight, encrypted, file-based database engine.

## ?? Wat is dit?

SharpCoreDB.EntityFrameworkCore is een **EF Core provider** waarmee je de kracht van Entity Framework Core kunt gebruiken met de SharpCoreDB encrypted database. Je krijgt:

- ? **LINQ queries** met compile-time type checking
- ? **Migrations** voor database schema beheer
- ? **Change tracking** voor automatische updates
- ? **Relationships** (one-to-many, many-to-many)
- ? **AES-256-GCM encryption** (van SharpCoreDB)
- ? **Platform optimizations** (AVX2/NEON intrinsics)

## ?? Installatie

### Via NuGet Package Manager
```powershell
Install-Package SharpCoreDB.EntityFrameworkCore
```

### Via .NET CLI
```bash
dotnet add package SharpCoreDB.EntityFrameworkCore
```

### Via Package Reference
```xml
<PackageReference Include="SharpCoreDB.EntityFrameworkCore" Version="1.0.0" />
```

## ?? Quick Start

### 1. Definieer je Entities

```csharp
public class Employee
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public decimal Salary { get; set; }
    public DateTime HireDate { get; set; }
    
    // Navigation properties
    public int DepartmentId { get; set; }
    public Department? Department { get; set; }
    public List<TimeEntry> TimeEntries { get; set; } = new();
}

public class Department
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<Employee> Employees { get; set; } = new();
}

public class TimeEntry
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public DateTime Date { get; set; }
    public TimeSpan Hours { get; set; }
    public string Description { get; set; } = string.Empty;
}
```

### 2. Maak je DbContext

```csharp
using Microsoft.EntityFrameworkCore;
using SharpCoreDB.EntityFrameworkCore;

public class CompanyContext : DbContext
{
    public DbSet<Employee> Employees { get; set; }
    public DbSet<Department> Departments { get; set; }
    public DbSet<TimeEntry> TimeEntries { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSharpCoreDB(
            connectionString: "Data Source=company.db;Encryption=true;Password=MySecurePassword123",
            options => 
            {
                // Optionele configuratie
                options.EnableSensitiveDataLogging(isDevelopment: true);
                options.CommandTimeout(30);
            });
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configureer relationships
        modelBuilder.Entity<Employee>()
            .HasOne(e => e.Department)
            .WithMany(d => d.Employees)
            .HasForeignKey(e => e.DepartmentId);

        modelBuilder.Entity<TimeEntry>()
            .HasOne(t => t.Employee)
            .WithMany(e => e.TimeEntries)
            .HasForeignKey(t => t.EmployeeId);

        // Indexes voor performance
        modelBuilder.Entity<Employee>()
            .HasIndex(e => e.Email)
            .IsUnique();

        modelBuilder.Entity<TimeEntry>()
            .HasIndex(t => new { t.EmployeeId, t.Date });
    }
}
```

### 3. Gebruik EF Core

```csharp
using var context = new CompanyContext();

// Ensure database is created
context.Database.EnsureCreated();

// Create
var engineering = new Department { Name = "Engineering" };
context.Departments.Add(engineering);
await context.SaveChangesAsync();

var employee = new Employee
{
    Name = "John Doe",
    Email = "john.doe@company.com",
    Salary = 85000,
    HireDate = DateTime.Today,
    DepartmentId = engineering.Id
};
context.Employees.Add(employee);
await context.SaveChangesAsync();

// Read with LINQ
var highEarners = await context.Employees
    .Include(e => e.Department)
    .Where(e => e.Salary > 80000)
    .OrderByDescending(e => e.Salary)
    .ToListAsync();

foreach (var emp in highEarners)
{
    Console.WriteLine($"{emp.Name} - {emp.Department?.Name} - ${emp.Salary:N0}");
}

// Update
employee.Salary = 90000;
await context.SaveChangesAsync();

// Delete
context.Employees.Remove(employee);
await context.SaveChangesAsync();
```

## ?? Geavanceerde Voorbeelden

Zie de volledige README voor:
- Dependency Injection (ASP.NET Core)
- Migrations
- Complex Queries & Joins
- Raw SQL
- Bulk Operations
- Security & Encryption
- Performance Tips
- Testing
- Troubleshooting

## ?? Platform Support

Ondersteunt: Windows, Linux, macOS, Android, iOS, en IoT/embedded devices met platform-specifieke optimalisaties (AVX2/NEON).

## ?? Resources

- **Main Package**: [SharpCoreDB](https://www.nuget.org/packages/SharpCoreDB)
- **GitHub**: https://github.com/MPCoreDeveloper/SharpCoreDB
- **Full Documentation**: Zie USAGE_GUIDE.md

## ?? License

MIT License
