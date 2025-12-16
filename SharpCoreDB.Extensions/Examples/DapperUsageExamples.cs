using Dapper;
using SharpCoreDB.Interfaces;
using System.Data;

namespace SharpCoreDB.Extensions.Examples;

/// <summary>
/// Comprehensive examples of using Dapper with SharpCoreDB.
/// </summary>
public static class DapperUsageExamples
{
    #region Basic Query Examples

    /// <summary>
    /// Example: Basic query using Dapper.
    /// </summary>
    public static void BasicQueryExample(IDatabase database)
    {
        using var connection = database.GetDapperConnection();
        connection.Open();

        // Simple query
        var users = connection.Query<User>("SELECT * FROM Users");
        
        foreach (var user in users)
        {
            Console.WriteLine($"{user.Id}: {user.Name}");
        }
    }

    /// <summary>
    /// Example: Parameterized query.
    /// </summary>
    public static void ParameterizedQueryExample(IDatabase database)
    {
        using var connection = database.GetDapperConnection();
        connection.Open();

        // Query with parameters
        var user = connection.QueryFirstOrDefault<User>(
            "SELECT * FROM Users WHERE Id = @UserId",
            new { UserId = 1 });

        if (user != null)
        {
            Console.WriteLine($"Found user: {user.Name}");
        }
    }

    #endregion

    #region Async Examples

    /// <summary>
    /// Example: Async query operations.
    /// </summary>
    public static async Task AsyncQueryExample(IDatabase database)
    {
        // Using extension methods
        var users = await database.QueryAsync<User>("SELECT * FROM Users");
        
        foreach (var user in users)
        {
            Console.WriteLine($"{user.Id}: {user.Name}");
        }

        // Single result
        var firstUser = await database.QueryFirstOrDefaultAsync<User>(
            "SELECT * FROM Users WHERE Id = @Id",
            new { Id = 1 });
    }

    /// <summary>
    /// Example: Async command execution.
    /// </summary>
    public static async Task AsyncCommandExample(IDatabase database)
    {
        var affectedRows = await database.ExecuteAsync(
            "UPDATE Users SET LastLogin = @Now WHERE Id = @UserId",
            new { Now = DateTime.UtcNow, UserId = 1 });

        Console.WriteLine($"Updated {affectedRows} rows");
    }

    #endregion

    #region Bulk Operations Examples

    /// <summary>
    /// Example: Bulk insert operation.
    /// </summary>
    public static void BulkInsertExample(IDatabase database)
    {
        var users = new List<User>
        {
            new() { Name = "Alice", Email = "alice@example.com" },
            new() { Name = "Bob", Email = "bob@example.com" },
            new() { Name = "Charlie", Email = "charlie@example.com" }
        };

        var inserted = database.BulkInsert("Users", users, batchSize: 100);
        Console.WriteLine($"Inserted {inserted} users");
    }

    /// <summary>
    /// Example: Bulk update operation.
    /// </summary>
    public static void BulkUpdateExample(IDatabase database)
    {
        var users = new List<User>
        {
            new() { Id = 1, Name = "Alice Updated", Email = "alice@example.com" },
            new() { Id = 2, Name = "Bob Updated", Email = "bob@example.com" }
        };

        var updated = database.BulkUpdate("Users", users, "Id", batchSize: 100);
        Console.WriteLine($"Updated {updated} users");
    }

    /// <summary>
    /// Example: Bulk delete operation.
    /// </summary>
    public static void BulkDeleteExample(IDatabase database)
    {
        var idsToDelete = new[] { 1, 2, 3, 4, 5 };
        var deleted = database.BulkDelete("Users", idsToDelete, "Id");
        Console.WriteLine($"Deleted {deleted} users");
    }

    #endregion

    #region Transaction Examples

    /// <summary>
    /// Example: Using transactions.
    /// </summary>
    public static void TransactionExample(IDatabase database)
    {
        using var connection = database.GetDapperConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        try
        {
            connection.Execute(
                "INSERT INTO Users (Name, Email) VALUES (@Name, @Email)",
                new { Name = "Alice", Email = "alice@example.com" },
                transaction);

            connection.Execute(
                "INSERT INTO Orders (UserId, Total) VALUES (@UserId, @Total)",
                new { UserId = 1, Total = 99.99 },
                transaction);

            transaction.Commit();
            Console.WriteLine("Transaction committed successfully");
        }
        catch
        {
            transaction.Rollback();
            Console.WriteLine("Transaction rolled back");
            throw;
        }
    }

    #endregion

    #region Repository Pattern Examples

    /// <summary>
    /// Example: Using repository pattern.
    /// </summary>
    public static void RepositoryPatternExample(IDatabase database)
    {
        var userRepository = new DapperRepository<User, int>(database, "Users", "Id");

        // Get by ID
        var user = userRepository.GetById(1);
        if (user != null)
        {
            Console.WriteLine($"Found user: {user.Name}");
        }

        // Get all
        var allUsers = userRepository.GetAll();
        Console.WriteLine($"Total users: {allUsers.Count()}");

        // Insert
        var newUser = new User { Name = "David", Email = "david@example.com" };
        userRepository.Insert(newUser);

        // Update
        if (user != null)
        {
            user.Name = "Updated Name";
            userRepository.Update(user);
        }

        // Delete
        userRepository.Delete(1);

        // Count
        var count = userRepository.Count();
        Console.WriteLine($"User count: {count}");
    }

    /// <summary>
    /// Example: Using Unit of Work pattern.
    /// </summary>
    public static void UnitOfWorkExample(IDatabase database)
    {
        using var uow = new DapperUnitOfWork(database);
        
        try
        {
            uow.BeginTransaction();

            var userRepo = uow.GetRepository<User, int>("Users");
            var orderRepo = uow.GetRepository<Order, int>("Orders");

            var user = new User { Name = "Alice", Email = "alice@example.com" };
            userRepo.Insert(user);

            var order = new Order { UserId = 1, Total = 99.99m };
            orderRepo.Insert(order);

            uow.Commit();
            Console.WriteLine("Unit of work committed");
        }
        catch
        {
            uow.Rollback();
            Console.WriteLine("Unit of work rolled back");
            throw;
        }
    }

    #endregion

    #region Mapping Examples

    /// <summary>
    /// Example: Custom mapping.
    /// </summary>
    public static void CustomMappingExample(IDatabase database)
    {
        var users = database.QueryWithMapping(
            "SELECT * FROM Users",
            row => new User
            {
                Id = Convert.ToInt32(row["Id"]),
                Name = row["Name"].ToString() ?? string.Empty,
                Email = row["Email"].ToString() ?? string.Empty
            });

        foreach (var user in users)
        {
            Console.WriteLine($"{user.Id}: {user.Name}");
        }
    }

    /// <summary>
    /// Example: Multi-table join mapping.
    /// </summary>
    public static void MultiTableMappingExample(IDatabase database)
    {
        var sql = @"
            SELECT u.*, o.*
            FROM Users u
            INNER JOIN Orders o ON u.Id = o.UserId";

        var results = database.QueryMultiMapped<User, Order, UserWithOrders>(
            sql,
            (user, order) =>
            {
                var userWithOrders = new UserWithOrders
                {
                    Id = user.Id,
                    Name = user.Name,
                    Email = user.Email,
                    Orders = new List<Order> { order }
                };
                return userWithOrders;
            },
            splitOn: "Id");

        foreach (var item in results)
        {
            Console.WriteLine($"User: {item.Name}, Orders: {item.Orders.Count}");
        }
    }

    #endregion

    #region Performance Monitoring Examples

    /// <summary>
    /// Example: Query with performance metrics.
    /// </summary>
    public static void PerformanceMonitoringExample(IDatabase database)
    {
        var result = database.QueryWithMetrics<User>(
            "SELECT * FROM Users",
            queryName: "GetAllUsers");

        Console.WriteLine($"Execution time: {result.Metrics.ExecutionTime.TotalMilliseconds}ms");
        Console.WriteLine($"Rows returned: {result.Metrics.RowCount}");
        Console.WriteLine($"Memory used: {result.Metrics.MemoryUsed} bytes");

        // Get performance report
        var report = DapperPerformanceExtensions.GetPerformanceReport();
        Console.WriteLine($"Total queries: {report.TotalQueries}");
        Console.WriteLine($"Average time: {report.AverageExecutionTime.TotalMilliseconds}ms");
        
        if (report.SlowestQuery != null)
        {
            Console.WriteLine($"Slowest query: {report.SlowestQuery.QueryName} " +
                            $"({report.SlowestQuery.ExecutionTime.TotalMilliseconds}ms)");
        }
    }

    /// <summary>
    /// Example: Query with timeout warning.
    /// </summary>
    public static void TimeoutWarningExample(IDatabase database)
    {
        var results = database.QueryWithTimeout<User>(
            "SELECT * FROM Users",
            timeout: TimeSpan.FromSeconds(1),
            onTimeout: elapsed =>
            {
                Console.WriteLine($"WARNING: Query took {elapsed.TotalSeconds}s");
            });
    }

    #endregion

    #region Pagination Examples

    /// <summary>
    /// Example: Paginated query.
    /// </summary>
    public static async Task PaginationExample(IDatabase database)
    {
        var page1 = await database.QueryPagedAsync<User>(
            "SELECT * FROM Users",
            pageNumber: 1,
            pageSize: 10);

        Console.WriteLine($"Page {page1.PageNumber} of {page1.TotalPages}");
        Console.WriteLine($"Total items: {page1.TotalCount}");
        Console.WriteLine($"Has next: {page1.HasNextPage}");

        foreach (var user in page1.Items)
        {
            Console.WriteLine($"  {user.Id}: {user.Name}");
        }
    }

    #endregion

    #region Type Mapping Examples

    /// <summary>
    /// Example: Using type mapper.
    /// </summary>
    public static void TypeMapperExample(IDatabase database)
    {
        // Create custom column mappings
        var mappings = new Dictionary<string, string>
        {
            ["user_id"] = "Id",
            ["user_name"] = "Name",
            ["user_email"] = "Email"
        };

        DapperMappingExtensions.CreateTypeMap<User>(mappings);

        using var connection = database.GetDapperConnection();
        connection.Open();

        var users = connection.Query<User>(
            "SELECT user_id, user_name, user_email FROM Users");
    }

    #endregion

    #region Advanced Examples

    /// <summary>
    /// Example: Dynamic query results.
    /// </summary>
    public static void DynamicQueryExample(IDatabase database)
    {
        var results = database.QueryDynamic("SELECT * FROM Users");

        foreach (dynamic row in results)
        {
            Console.WriteLine($"{row.Id}: {row.Name} ({row.Email})");
        }
    }

    /// <summary>
    /// Example: Stored procedure execution (future support).
    /// </summary>
    public static async Task StoredProcedureExample(IDatabase database)
    {
        var results = await database.ExecuteStoredProcedureAsync<User>(
            "GetUsersByRole",
            new { Role = "Admin" });

        foreach (var user in results)
        {
            Console.WriteLine($"{user.Name}: {user.Email}");
        }
    }

    #endregion
}

#region Sample Entity Classes

public class User
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime? LastLogin { get; set; }
}

public class Order
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public decimal Total { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class UserWithOrders
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public List<Order> Orders { get; set; } = [];
}

#endregion
