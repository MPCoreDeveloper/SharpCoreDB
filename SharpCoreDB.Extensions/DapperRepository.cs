using Dapper;
using SharpCoreDB.Interfaces;
using System.Linq.Expressions;

namespace SharpCoreDB.Extensions;

/// <summary>
/// Generic repository interface for typed data access.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TKey">The key type.</typeparam>
public interface IDapperRepository<TEntity, TKey> where TEntity : class
{
    /// <summary>
    /// Gets an entity by its key.
    /// </summary>
    TEntity? GetById(TKey id);

    /// <summary>
    /// Gets an entity by its key asynchronously.
    /// </summary>
    Task<TEntity?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all entities.
    /// </summary>
    IEnumerable<TEntity> GetAll();

    /// <summary>
    /// Gets all entities asynchronously.
    /// </summary>
    Task<IEnumerable<TEntity>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds entities matching a condition.
    /// </summary>
    IEnumerable<TEntity> Find(Expression<Func<TEntity, bool>> predicate);

    /// <summary>
    /// Inserts a new entity.
    /// </summary>
    void Insert(TEntity entity);

    /// <summary>
    /// Inserts a new entity asynchronously.
    /// </summary>
    Task InsertAsync(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing entity.
    /// </summary>
    void Update(TEntity entity);

    /// <summary>
    /// Updates an existing entity asynchronously.
    /// </summary>
    Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an entity by its key.
    /// </summary>
    void Delete(TKey id);

    /// <summary>
    /// Deletes an entity by its key asynchronously.
    /// </summary>
    Task DeleteAsync(TKey id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the count of all entities.
    /// </summary>
    long Count();

    /// <summary>
    /// Gets the count of all entities asynchronously.
    /// </summary>
    Task<long> CountAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Base repository implementation using Dapper.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TKey">The key type.</typeparam>
public class DapperRepository<TEntity, TKey> : IDapperRepository<TEntity, TKey> 
    where TEntity : class, new()
{
    protected readonly IDatabase Database;
    protected readonly string TableName;
    protected readonly string KeyColumn;

    /// <summary>
    /// Initializes a new instance of the DapperRepository class.
    /// </summary>
    /// <param name="database">The database instance.</param>
    /// <param name="tableName">The table name.</param>
    /// <param name="keyColumn">The key column name (default: "Id").</param>
    public DapperRepository(IDatabase database, string tableName, string keyColumn = "Id")
    {
        Database = database ?? throw new ArgumentNullException(nameof(database));
        TableName = tableName ?? throw new ArgumentNullException(nameof(tableName));
        KeyColumn = keyColumn ?? throw new ArgumentNullException(nameof(keyColumn));
    }

    /// <inheritdoc />
    public virtual TEntity? GetById(TKey id)
    {
        var sql = $"SELECT * FROM {TableName} WHERE {KeyColumn} = @Id";
        using var connection = Database.GetDapperConnection();
        connection.Open();
        return connection.QueryFirstOrDefault<TEntity>(sql, new { Id = id });
    }

    /// <inheritdoc />
    public virtual async Task<TEntity?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default)
    {
        var sql = $"SELECT * FROM {TableName} WHERE {KeyColumn} = @Id";
        using var connection = Database.GetDapperConnection();
        connection.Open();
        return await connection.QueryFirstOrDefaultAsync<TEntity>(sql, new { Id = id });
    }

    /// <inheritdoc />
    public virtual IEnumerable<TEntity> GetAll()
    {
        var sql = $"SELECT * FROM {TableName}";
        using var connection = Database.GetDapperConnection();
        connection.Open();
        return connection.Query<TEntity>(sql);
    }

    /// <inheritdoc />
    public virtual async Task<IEnumerable<TEntity>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var sql = $"SELECT * FROM {TableName}";
        using var connection = Database.GetDapperConnection();
        connection.Open();
        return await connection.QueryAsync<TEntity>(sql);
    }

    /// <inheritdoc />
    public virtual IEnumerable<TEntity> Find(Expression<Func<TEntity, bool>> predicate)
    {
        // Simplified implementation - in a full version, this would translate the expression to SQL
        var all = GetAll();
        var compiled = predicate.Compile();
        return all.Where(compiled);
    }

    /// <inheritdoc />
    public virtual void Insert(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var properties = typeof(TEntity).GetProperties()
            .Where(p => p.CanRead && p.Name != KeyColumn)
            .ToList();

        var columns = string.Join(", ", properties.Select(p => p.Name));
        var values = string.Join(", ", properties.Select(p => $"@{p.Name}"));

        var sql = $"INSERT INTO {TableName} ({columns}) VALUES ({values})";
        
        using var connection = Database.GetDapperConnection();
        connection.Open();
        connection.Execute(sql, entity);
    }

    /// <inheritdoc />
    public virtual async Task InsertAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var properties = typeof(TEntity).GetProperties()
            .Where(p => p.CanRead && p.Name != KeyColumn)
            .ToList();

        var columns = string.Join(", ", properties.Select(p => p.Name));
        var values = string.Join(", ", properties.Select(p => $"@{p.Name}"));

        var sql = $"INSERT INTO {TableName} ({columns}) VALUES ({values})";
        
        using var connection = Database.GetDapperConnection();
        connection.Open();
        await connection.ExecuteAsync(sql, entity);
    }

    /// <inheritdoc />
    public virtual void Update(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var properties = typeof(TEntity).GetProperties()
            .Where(p => p.CanRead && p.Name != KeyColumn)
            .ToList();

        var setClause = string.Join(", ", properties.Select(p => $"{p.Name} = @{p.Name}"));
        var sql = $"UPDATE {TableName} SET {setClause} WHERE {KeyColumn} = @{KeyColumn}";
        
        using var connection = Database.GetDapperConnection();
        connection.Open();
        connection.Execute(sql, entity);
    }

    /// <inheritdoc />
    public virtual async Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var properties = typeof(TEntity).GetProperties()
            .Where(p => p.CanRead && p.Name != KeyColumn)
            .ToList();

        var setClause = string.Join(", ", properties.Select(p => $"{p.Name} = @{p.Name}"));
        var sql = $"UPDATE {TableName} SET {setClause} WHERE {KeyColumn} = @{KeyColumn}";
        
        using var connection = Database.GetDapperConnection();
        connection.Open();
        await connection.ExecuteAsync(sql, entity);
    }

    /// <inheritdoc />
    public virtual void Delete(TKey id)
    {
        var sql = $"DELETE FROM {TableName} WHERE {KeyColumn} = @Id";
        using var connection = Database.GetDapperConnection();
        connection.Open();
        connection.Execute(sql, new { Id = id });
    }

    /// <inheritdoc />
    public virtual async Task DeleteAsync(TKey id, CancellationToken cancellationToken = default)
    {
        var sql = $"DELETE FROM {TableName} WHERE {KeyColumn} = @Id";
        using var connection = Database.GetDapperConnection();
        connection.Open();
        await connection.ExecuteAsync(sql, new { Id = id });
    }

    /// <inheritdoc />
    public virtual long Count()
    {
        var sql = $"SELECT COUNT(*) FROM {TableName}";
        using var connection = Database.GetDapperConnection();
        connection.Open();
        return connection.ExecuteScalar<long>(sql);
    }

    /// <inheritdoc />
    public virtual async Task<long> CountAsync(CancellationToken cancellationToken = default)
    {
        var sql = $"SELECT COUNT(*) FROM {TableName}";
        using var connection = Database.GetDapperConnection();
        connection.Open();
        return await connection.ExecuteScalarAsync<long>(sql);
    }

    /// <summary>
    /// Executes a custom query.
    /// </summary>
    protected IEnumerable<TEntity> ExecuteQuery(string sql, object? param = null)
    {
        using var connection = Database.GetDapperConnection();
        connection.Open();
        return connection.Query<TEntity>(sql, param);
    }

    /// <summary>
    /// Executes a custom query asynchronously.
    /// </summary>
    protected async Task<IEnumerable<TEntity>> ExecuteQueryAsync(string sql, object? param = null)
    {
        using var connection = Database.GetDapperConnection();
        connection.Open();
        return await connection.QueryAsync<TEntity>(sql, param);
    }
}

/// <summary>
/// Read-only repository for query-only scenarios.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TKey">The key type.</typeparam>
public class ReadOnlyDapperRepository<TEntity, TKey> where TEntity : class, new()
{
    protected readonly IDatabase Database;
    protected readonly string TableName;
    protected readonly string KeyColumn;

    public ReadOnlyDapperRepository(IDatabase database, string tableName, string keyColumn = "Id")
    {
        Database = database ?? throw new ArgumentNullException(nameof(database));
        TableName = tableName ?? throw new ArgumentNullException(nameof(tableName));
        KeyColumn = keyColumn ?? throw new ArgumentNullException(nameof(keyColumn));
    }

    public virtual TEntity? GetById(TKey id)
    {
        var sql = $"SELECT * FROM {TableName} WHERE {KeyColumn} = @Id";
        using var connection = Database.GetDapperConnection();
        connection.Open();
        return connection.QueryFirstOrDefault<TEntity>(sql, new { Id = id });
    }

    public virtual async Task<TEntity?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default)
    {
        var sql = $"SELECT * FROM {TableName} WHERE {KeyColumn} = @Id";
        using var connection = Database.GetDapperConnection();
        connection.Open();
        return await connection.QueryFirstOrDefaultAsync<TEntity>(sql, new { Id = id });
    }

    public virtual IEnumerable<TEntity> GetAll()
    {
        var sql = $"SELECT * FROM {TableName}";
        using var connection = Database.GetDapperConnection();
        connection.Open();
        return connection.Query<TEntity>(sql);
    }

    public virtual async Task<IEnumerable<TEntity>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var sql = $"SELECT * FROM {TableName}";
        using var connection = Database.GetDapperConnection();
        connection.Open();
        return await connection.QueryAsync<TEntity>(sql);
    }

    public virtual long Count()
    {
        var sql = $"SELECT COUNT(*) FROM {TableName}";
        using var connection = Database.GetDapperConnection();
        connection.Open();
        return connection.ExecuteScalar<long>(sql);
    }
}

/// <summary>
/// Unit of Work pattern for managing transactions across repositories.
/// </summary>
public class DapperUnitOfWork : IDisposable
{
    private readonly IDatabase _database;
    private DapperConnection? _connection;
    private DapperTransaction? _transaction;
    private bool _disposed;

    public DapperUnitOfWork(IDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    /// <summary>
    /// Begins a transaction.
    /// </summary>
    public void BeginTransaction()
    {
        _connection = (DapperConnection)_database.GetDapperConnection();
        _connection.Open();
        _transaction = (DapperTransaction)_connection.BeginTransaction();
    }

    /// <summary>
    /// Commits the transaction.
    /// </summary>
    public void Commit()
    {
        _transaction?.Commit();
    }

    /// <summary>
    /// Rolls back the transaction.
    /// </summary>
    public void Rollback()
    {
        _transaction?.Rollback();
    }

    /// <summary>
    /// Gets a repository instance.
    /// </summary>
    public DapperRepository<TEntity, TKey> GetRepository<TEntity, TKey>(string tableName, string keyColumn = "Id") 
        where TEntity : class, new()
    {
        return new DapperRepository<TEntity, TKey>(_database, tableName, keyColumn);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            _transaction?.Dispose();
            _connection?.Dispose();
        }

        _disposed = true;
    }
}
