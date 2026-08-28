using Microsoft.EntityFrameworkCore;
using SharpCoreDB.EntityFrameworkCore;
using System.Text.Json;

Console.WriteLine("?? SharpCoreDB + EF Core - Full CRUD Demo (Companies & Vacancies)");
Console.WriteLine("Using recommended reliable patterns for Guid relationships\n");

// Setup database (in-memory file for demo)
var dbPath = $"./crud_demo_{Guid.NewGuid():N}.scdb";
var connectionString = $"Data Source={dbPath};Password=DemoPassword123;Cache=Shared";

var options = new DbContextOptionsBuilder<CompanyVacancyContext>()
    .UseSharpCoreDB(connectionString)
    .Options;

using var context = new CompanyVacancyContext(options);
await context.Database.EnsureCreatedAsync();

// =====================================================================
// 1. LOAD SEED DATA
// =====================================================================
Console.WriteLine("?? Loading seed data from companies.vacancies.seed.json...");

var json = await File.ReadAllTextAsync("companies.vacancies.seed.json");
var seedData = JsonSerializer.Deserialize<SeedDataRoot>(json, new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true
});

Console.WriteLine($"   Loaded {seedData.Companies.Count} companies from seed file.\n");

// =====================================================================
// 2. CREATE (Seed the database)
// =====================================================================
Console.WriteLine("?? CREATE - Seeding companies and vacancies...");

foreach (var seed in seedData.Companies)
{
    var company = new Company
    {
        Id = Guid.NewGuid(),
        Name = seed.Name,
        Address = seed.Address
    };

    foreach (var v in seed.Vacancies)
    {
        company.Vacancies.Add(new Vacancy
        {
            Id = Guid.NewGuid(),
            Title = v.Title,
            Description = v.Description,
            IsActive = v.IsActive,
            CompanyId = company.Id
        });
    }

    context.Companies.Add(company);
}

await context.SaveChangesAsync();
Console.WriteLine("   Seeded successfully.\n");

// =====================================================================
// 3. READ - Using recommended reliable pattern
// =====================================================================
Console.WriteLine("?? READ - Getting companies with active vacancies (recommended pattern)...");

var activeCompanies = await GetActiveWithVacanciesAsync(context);

Console.WriteLine($"   Found {activeCompanies.Count} companies with at least one active vacancy:");
foreach (var c in activeCompanies)
{
    Console.WriteLine($"   - {c.Name} ({c.Vacancies.Count(v => v.IsActive)} active vacancies)");
}
Console.WriteLine();

// =====================================================================
// 4. UPDATE
// =====================================================================
Console.WriteLine("?? UPDATE - Changing a vacancy...");

var delta = await context.Companies
    .AsNoTracking()
    .FirstAsync(c => c.Name == "Delta Logistics");

var backendDev = await context.Vacancies.FirstAsync(v => v.Title == "Backend Developer" && v.CompanyId == delta.Id);

backendDev.Title = "Senior Backend Developer";
backendDev.IsActive = false;

await context.SaveChangesAsync();

Console.WriteLine("   Updated 'Backend Developer' → 'Senior Backend Developer' (IsActive = false)\n");

// Re-read using recommended pattern
var afterUpdate = await GetActiveWithVacanciesAsync(context);
var deltaAfter = afterUpdate.First(c => c.Name == "Delta Logistics");
Console.WriteLine($"   Delta Logistics now has {deltaAfter.Vacancies.Count(v => v.IsActive)} active vacancies.\n");

// =====================================================================
// 5. CREATE NEW
// =====================================================================
Console.WriteLine("?? CREATE - Adding new company + vacancy...");

var newCompany = new Company
{
    Id = Guid.NewGuid(),
    Name = "Future Systems",
    Address = "Innovation Park 42, Amsterdam"
};
newCompany.Vacancies.Add(new Vacancy
{
    Id = Guid.NewGuid(),
    Title = "AI Engineer",
    IsActive = true,
    CompanyId = newCompany.Id
});

context.Companies.Add(newCompany);
await context.SaveChangesAsync();

Console.WriteLine("   Added 'Future Systems' with AI Engineer vacancy.\n");

// =====================================================================
// 6. DELETE
// =====================================================================
Console.WriteLine("?? DELETE - Removing a vacancy...");

var toDelete = await context.Vacancies
    .FirstAsync(v => v.Title == "Project Coordinator");

context.Vacancies.Remove(toDelete);
await context.SaveChangesAsync();

Console.WriteLine("   Removed 'Project Coordinator' vacancy.\n");

// Final read
var final = await GetActiveWithVacanciesAsync(context);
Console.WriteLine($"   Final result: {final.Count} companies with active vacancies.");

// Cleanup
try { File.Delete(dbPath); } catch { /* Intentionally empty */ }

Console.WriteLine("\n? Full CRUD demo completed successfully!");
Console.WriteLine("   (Using recommended reliable patterns for Guid-based relationships)");

// =====================================================================
// Recommended reliable helper (same pattern as CompanyVacancyRepository)
// =====================================================================
static async Task<List<Company>> GetActiveWithVacanciesAsync(CompanyVacancyContext dbContext)
{
    var companies = await dbContext.Companies.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
    var vacancies = await dbContext.Vacancies.AsNoTracking().ToListAsync();

    var byCompany = vacancies.GroupBy(v => v.CompanyId).ToDictionary(g => g.Key, g => g.ToList());

    foreach (var c in companies)
    {
        c.Vacancies = byCompany.TryGetValue(c.Id, out var list) ? list : [];
    }

    return companies.Where(x => x.Vacancies.Any(v => v.IsActive)).ToList();
}

// =====================================================================
// Entity models (minimal for demo)
// =====================================================================
// NOSONAR:S3903 - top-level statement file; trailing demo types cannot be moved into a namespace.
public class Company // NOSONAR:S3903 - top-level statement file; trailing demo types cannot be moved into a namespace.
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public ICollection<Vacancy> Vacancies { get; set; } = [];
}

// NOSONAR:S3903 - top-level statement file; trailing demo types cannot be moved into a namespace.
public class Vacancy // NOSONAR:S3903 - top-level statement file; trailing demo types cannot be moved into a namespace.
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public Guid CompanyId { get; set; }
}

// NOSONAR:S3903 - top-level statement file; trailing demo types cannot be moved into a namespace.
public class CompanyVacancyContext(DbContextOptions<CompanyVacancyContext> options) : DbContext(options) // NOSONAR:S3903 - top-level statement file; trailing demo types cannot be moved into a namespace.
{
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Vacancy> Vacancies => Set<Vacancy>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Company>(e =>
        {
            e.ToTable("Companies");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.HasMany(x => x.Vacancies).WithOne().HasForeignKey(v => v.CompanyId);
        });

        modelBuilder.Entity<Vacancy>(e =>
        {
            e.ToTable("Vacancies");
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).IsRequired().HasMaxLength(200);
        });
    }
}

// Seed DTOs
public class SeedDataRoot { public List<SeedCompany> Companies { get; set; } = []; } // NOSONAR:S3903 - top-level statement file; trailing demo types cannot be moved into a namespace.
public class SeedCompany { public string Name { get; set; } = ""; public string Address { get; set; } = ""; public List<SeedVacancy> Vacancies { get; set; } = []; } // NOSONAR:S3903 - top-level statement file; trailing demo types cannot be moved into a namespace.
public class SeedVacancy { public string Title { get; set; } = ""; public string Description { get; set; } = ""; public bool IsActive { get; set; } } // NOSONAR:S3903 - top-level statement file; trailing demo types cannot be moved into a namespace.
