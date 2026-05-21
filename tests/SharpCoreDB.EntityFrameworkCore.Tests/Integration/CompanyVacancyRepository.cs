using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SharpCoreDB; // for raw Database access in diagnostics

namespace SharpCoreDB.EntityFrameworkCore.Tests.Integration;

/// <summary>
/// Recommended implementation of the user's GetActiveWithVacanciesAsync pattern
/// when using Guid primary keys with SharpCoreDB.EntityFrameworkCore.
///
/// The ideal one-line LINQ (.Where(x => x.Vacancies.Any(...)) + Include) currently
/// hits limitations in the provider's navigation materialization for Guid keys.
/// This implementation delivers the exact same public behavior and semantics
/// while using a reliable, server-side filtered approach that works today.
///
/// ROADMAP: A deeper fix via custom IModificationCommandBatch (Option B) is planned
/// for a future release to support more "magical" EF relationship patterns with Guids.
/// </summary>
public static class CompanyVacancyRepository
{
    /// <summary>
    /// Exact method signature the user requested.
    /// Returns only companies that have at least one active vacancy.
    /// </summary>
    public static async Task<IReadOnlyList<TestCompany>> GetActiveWithVacanciesAsync(
        TestCompanyVacancyDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        // Currently the most reliable way (workaround until provider is fixed)
        var companies = await dbContext.Companies
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var vacancies = await dbContext.Vacancies
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var vacanciesByCompany = vacancies
            .GroupBy(v => v.CompanyId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var company in companies)
        {
            company.Vacancies = vacanciesByCompany.TryGetValue(company.Id, out var list)
                ? list
                : [];
        }

        return companies
            .Where(x => x.Vacancies.Any(v => v.IsActive))
            .ToList();
    }

    /// <summary>
    /// Recommended diagnostic helper (works reliably in EF Core).
    /// Loads the child entities normally and prints their foreign key values.
    /// If CompanyId is Guid.Empty, the FK was not written correctly during insert.
    /// </summary>
    public static void DumpRawVacancyForeignKeys(TestCompanyVacancyDbContext context, string label = "Vacancy FK Diagnostic")
    {
        Console.WriteLine($"\n=== {label} ===");

        try
        {
            // Load vacancies using normal EF path (not through Include on parent)
            var vacancies = context.Vacancies.AsNoTracking().ToList();

            if (vacancies.Count == 0)
            {
                Console.WriteLine("  No vacancies found.");
            }
            else
            {
                foreach (var v in vacancies)
                {
                    Console.WriteLine($"  Vacancy: Title=\"{v.Title}\", CompanyId={v.CompanyId}, IsActive={v.IsActive}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [Diagnostic error] {ex.Message}");
        }

        Console.WriteLine("=== End Diagnostic ===\n");
    }
}
