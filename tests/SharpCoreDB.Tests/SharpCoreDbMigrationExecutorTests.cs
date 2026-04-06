using System.Collections;
using System.Data;
using System.Data.Common;
using FluentMigrator;
using FluentMigrator.Runner;
using FluentMigrator.Runner.Initialization;
using FluentMigrator.Runner.Processors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using SharpCoreDB.Extensions.Extensions;
using SharpCoreDB.Extensions.Processor;
using SharpCoreDB.Extensions.Runner;
using SharpCoreDB.Interfaces;

namespace SharpCoreDB.Tests;

public sealed class SharpCoreDbMigrationExecutorTests
{
    private const string GrpcConnectionString = "Server=localhost;Port=5001;Database=master;SSL=false;Username=admin;Password=test";

    [Fact]
    public void ExecuteSql_WhenCustomExecutorRegistered_UsesCustomExecutorBeforeFallbacks()
    {
        // Arrange
        var customExecutor = new Mock<ISharpCoreDbMigrationSqlExecutor>(MockBehavior.Strict);
        customExecutor.Setup(x => x.ExecuteSql("CREATE TABLE audit (id INT)"));

        var fallbackConnection = new FakeDbConnection();

        var services = new ServiceCollection();
        services.AddSingleton(customExecutor.Object);
        services.AddSingleton<DbConnection>(fallbackConnection);

        var executor = new SharpCoreDbMigrationExecutor(services.BuildServiceProvider());

        // Act
        executor.ExecuteSql("CREATE TABLE audit (id INT)");

        // Assert
        customExecutor.Verify(x => x.ExecuteSql("CREATE TABLE audit (id INT)"), Times.Once);
        Assert.Empty(fallbackConnection.ExecutedNonQuerySql);
    }

    [Fact]
    public void AddSharpCoreDBFluentMigrator_WhenCalled_RegistersGrpcSqlExecutor()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSharpCoreDBFluentMigratorGrpc("Server=localhost;Port=5001;Database=master;SSL=false;Username=admin;Password=test");

        // Act
        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var executor = scope.ServiceProvider.GetRequiredService<ISharpCoreDbMigrationSqlExecutor>();

        // Assert
        Assert.IsType<SharpCoreDbGrpcMigrationSqlExecutor>(executor);
    }

    [Fact]
    public void AddSharpCoreDBFluentMigrator_WhenServicesNull_Throws()
    {
        // Arrange
        IServiceCollection? services = null;

        // Act
        var action = () => services!.AddSharpCoreDBFluentMigrator();

        // Assert
        Assert.Throws<ArgumentNullException>(action);
    }

    [Fact]
    public void AddSharpCoreDBFluentMigratorGrpc_WhenConnectionStringEmpty_Throws()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var action = () => services.AddSharpCoreDBFluentMigratorGrpc(string.Empty);

        // Assert
        Assert.Throws<ArgumentException>(action);
    }

    [Fact]
    public void ExecuteSql_WhenNoExecutionSourceRegistered_Throws()
    {
        // Arrange
        var services = new ServiceCollection();
        var executor = new SharpCoreDbMigrationExecutor(services.BuildServiceProvider());

        // Act
        var action = () => executor.ExecuteSql("DELETE FROM users");

        // Assert
        Assert.Throws<InvalidOperationException>(action);
    }

    [Fact]
    public void ExecuteScalar_WhenCustomExecutorRegistered_ReturnsCustomValue()
    {
        // Arrange
        var customExecutor = new Mock<ISharpCoreDbMigrationSqlExecutor>(MockBehavior.Strict);
        customExecutor.Setup(x => x.ExecuteScalar("SELECT 42")).Returns(42);

        var services = new ServiceCollection();
        services.AddSingleton(customExecutor.Object);

        var executor = new SharpCoreDbMigrationExecutor(services.BuildServiceProvider());

        // Act
        var value = executor.ExecuteScalar("SELECT 42");

        // Assert
        Assert.Equal(42, value);
    }

    [Fact]
    public void ExecuteScalar_WhenDatabaseReturnsNoRows_ReturnsNull()
    {
        // Arrange
        var databaseMock = new Mock<IDatabase>(MockBehavior.Strict);
        databaseMock
            .Setup(x => x.ExecuteQuery("SELECT 1", null))
            .Returns([]);

        var services = new ServiceCollection();
        services.AddSingleton(databaseMock.Object);

        var executor = new SharpCoreDbMigrationExecutor(services.BuildServiceProvider());

        // Act
        var value = executor.ExecuteScalar("SELECT 1");

        // Assert
        Assert.Null(value);
    }

    [Fact]
    public void ExecuteScalar_WhenDatabaseReturnsRow_ReturnsFirstValue()
    {
        // Arrange
        var databaseMock = new Mock<IDatabase>(MockBehavior.Strict);
        databaseMock
            .Setup(x => x.ExecuteQuery("SELECT id FROM users", null))
            .Returns([new Dictionary<string, object> { ["id"] = 7 }]);

        var services = new ServiceCollection();
        services.AddSingleton(databaseMock.Object);

        var executor = new SharpCoreDbMigrationExecutor(services.BuildServiceProvider());

        // Act
        var value = executor.ExecuteScalar("SELECT id FROM users");

        // Assert
        Assert.Equal(7, value);
    }

    [Fact]
    public void Read_WhenDatabaseReturnsRows_MapsDataSet()
    {
        // Arrange
        var databaseMock = new Mock<IDatabase>(MockBehavior.Strict);
        databaseMock
            .Setup(x => x.ExecuteQuery("SELECT * FROM users", null))
            .Returns(
            [
                new Dictionary<string, object> { ["id"] = 1, ["name"] = "Alice" },
                new Dictionary<string, object> { ["id"] = 2, ["name"] = "Bob" }
            ]);

        var services = new ServiceCollection();
        services.AddSingleton(databaseMock.Object);

        var executor = new SharpCoreDbMigrationExecutor(services.BuildServiceProvider());

        // Act
        var result = executor.Read("SELECT * FROM users");

        // Assert
        Assert.Equal(2, result.Tables[0].Rows.Count);
    }

    [Fact]
    public void GetOperationConnection_WhenCustomExecutorRegistered_ReturnsCustomConnection()
    {
        // Arrange
        var customConnection = new FakeDbConnection();
        var customExecutor = new Mock<ISharpCoreDbMigrationSqlExecutor>(MockBehavior.Strict);
        customExecutor.Setup(x => x.GetOperationConnection()).Returns(customConnection);

        var services = new ServiceCollection();
        services.AddSingleton(customExecutor.Object);

        var executor = new SharpCoreDbMigrationExecutor(services.BuildServiceProvider());

        // Act
        var operationConnection = executor.GetOperationConnection();

        // Assert
        Assert.Same(customConnection, operationConnection);
    }

    [Fact]
    public void ExecuteSql_WhenDatabaseRegistered_UsesDatabaseExecution()
    {
        // Arrange
        var databaseMock = new Mock<IDatabase>(MockBehavior.Strict);
        databaseMock
            .Setup(x => x.ExecuteSQL("CREATE TABLE users (id INT)"));

        var services = new ServiceCollection();
        services.AddSingleton(databaseMock.Object);

        var executor = new SharpCoreDbMigrationExecutor(services.BuildServiceProvider());

        // Act
        executor.ExecuteSql("CREATE TABLE users (id INT)");

        // Assert
        databaseMock.Verify(x => x.ExecuteSQL("CREATE TABLE users (id INT)"), Times.Once);
    }

    [Fact]
    public void ExecuteSql_WhenConnectionRegistered_UsesConnectionCommand()
    {
        // Arrange
        var connection = new FakeDbConnection();
        var services = new ServiceCollection();
        services.AddSingleton<DbConnection>(connection);

        var executor = new SharpCoreDbMigrationExecutor(services.BuildServiceProvider());

        // Act
        executor.ExecuteSql("CREATE TABLE products (id INT)");

        // Assert
        Assert.Contains("CREATE TABLE products (id INT)", connection.ExecutedNonQuerySql);
    }

    [Fact]
    public void EnsureVersionTable_WhenCalled_CreatesSharpMigrationsTable()
    {
        // Arrange
        var connection = new FakeDbConnection();
        var services = new ServiceCollection();
        services.AddSingleton<DbConnection>(connection);

        var executor = new SharpCoreDbMigrationExecutor(services.BuildServiceProvider());

        // Act
        SharpCoreDbMigrationExecutor.EnsureVersionTable(executor);

        // Assert
        Assert.Contains(connection.ExecutedNonQuerySql, sql => sql.Contains("__SharpMigrations", StringComparison.Ordinal));
    }

    [Fact]
    public void Read_WhenConnectionReturnsReader_MapsRowsIntoDataSet()
    {
        // Arrange
        var connection = new FakeDbConnection
        {
            ReaderFactory = _ =>
            {
                var table = new DataTable();
                table.Columns.Add("id", typeof(int));
                table.Columns.Add("name", typeof(string));
                table.Rows.Add(1, "Alice");
                return table.CreateDataReader();
            }
        };

        var services = new ServiceCollection();
        services.AddSingleton<DbConnection>(connection);

        var executor = new SharpCoreDbMigrationExecutor(services.BuildServiceProvider());

        // Act
        var dataSet = executor.Read("SELECT id, name FROM users");

        // Assert
        Assert.Single(dataSet.Tables);
        Assert.Single(dataSet.Tables[0].Rows);
        Assert.Equal("Alice", dataSet.Tables[0].Rows[0]["name"]);
    }

    [Fact]
    public void ProcessorExecute_WhenConnectionRegistered_ExecutesSqlViaExecutor()
    {
        // Arrange
        var connection = new FakeDbConnection();
        var services = new ServiceCollection();
        services.AddSingleton<DbConnection>(connection);

        var executor = new SharpCoreDbMigrationExecutor(services.BuildServiceProvider());

        var processorOptions = new ProcessorOptions
        {
            PreviewOnly = false,
            ProviderSwitches = string.Empty,
            Timeout = null,
        };

        var processor = new SharpCoreDbProcessor("sharpcoredb://test", processorOptions, executor);

        // Act
        processor.Execute("DELETE FROM users");

        // Assert
        Assert.Contains("DELETE FROM users", connection.ExecutedNonQuerySql);
    }

    [Fact]
    public void ProcessorExecuteTemplate_WhenCalled_FormatsSql()
    {
        // Arrange
        var customExecutor = new Mock<ISharpCoreDbMigrationSqlExecutor>(MockBehavior.Strict);
        customExecutor.Setup(x => x.ExecuteSql("DELETE FROM users WHERE id = 5"));

        var processor = CreateProcessorWithCustomExecutor(customExecutor.Object);

        // Act
        processor.Execute("DELETE FROM users WHERE id = {0}", 5);

        // Assert
        customExecutor.Verify(x => x.ExecuteSql("DELETE FROM users WHERE id = 5"), Times.Once);
    }

    [Fact]
    public void ProcessorReadTemplate_WhenCalled_FormatsSql()
    {
        // Arrange
        var customExecutor = new Mock<ISharpCoreDbMigrationSqlExecutor>(MockBehavior.Strict);
        customExecutor
            .Setup(x => x.Read("SELECT * FROM users WHERE id = 9"))
            .Returns(new DataSet());

        var processor = CreateProcessorWithCustomExecutor(customExecutor.Object);

        // Act
        _ = processor.Read("SELECT * FROM users WHERE id = {0}", 9);

        // Assert
        customExecutor.Verify(x => x.Read("SELECT * FROM users WHERE id = 9"), Times.Once);
    }

    [Fact]
    public void ProcessorExists_WhenExecutorReturnsNull_ReturnsFalse()
    {
        // Arrange
        var customExecutor = new Mock<ISharpCoreDbMigrationSqlExecutor>(MockBehavior.Strict);
        customExecutor.Setup(x => x.ExecuteScalar("SELECT 1")).Returns((object?)null);

        var processor = CreateProcessorWithCustomExecutor(customExecutor.Object);

        // Act
        var exists = processor.Exists("SELECT 1");

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public void ProcessorExists_WhenExecutorReturnsValue_ReturnsTrue()
    {
        // Arrange
        var customExecutor = new Mock<ISharpCoreDbMigrationSqlExecutor>(MockBehavior.Strict);
        customExecutor.Setup(x => x.ExecuteScalar("SELECT 1")).Returns(1);

        var processor = CreateProcessorWithCustomExecutor(customExecutor.Object);

        // Act
        var exists = processor.Exists("SELECT 1");

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public void ProcessorSchemaExists_WhenSchemaProvided_ReturnsFalse()
    {
        // Arrange
        var processor = CreateProcessorWithoutExecutionSource();

        // Act
        var exists = processor.SchemaExists("dbo");

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public void ProcessorSchemaExists_WhenSchemaEmpty_ReturnsTrue()
    {
        // Arrange
        var processor = CreateProcessorWithoutExecutionSource();

        // Act
        var exists = processor.SchemaExists(string.Empty);

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public void ProcessorDefaultValueExists_WhenPragmaReturnsEmptyTable_ReturnsFalse()
    {
        // Arrange
        var dataSet = new DataSet();
        dataSet.Tables.Add(new DataTable("Result"));

        var customExecutor = new Mock<ISharpCoreDbMigrationSqlExecutor>(MockBehavior.Strict);
        customExecutor
            .Setup(x => x.Read("PRAGMA table_info(\"users\")"))
            .Returns(dataSet);

        var processor = CreateProcessorWithCustomExecutor(customExecutor.Object);

        // Act
        var exists = processor.DefaultValueExists(string.Empty, "users", "status", "active");

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public void ProcessorIndexExists_WhenExecutorReturnsNull_ReturnsFalse()
    {
        // Arrange
        var customExecutor = new Mock<ISharpCoreDbMigrationSqlExecutor>(MockBehavior.Strict);
        customExecutor
            .Setup(x => x.ExecuteScalar("SELECT 1 FROM sqlite_master WHERE type = 'index' AND name = 'IX_users_email' LIMIT 1"))
            .Returns((object?)null);

        var processor = CreateProcessorWithCustomExecutor(customExecutor.Object);

        // Act
        var exists = processor.IndexExists(string.Empty, "users", "IX_users_email");

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public void ProcessorSequenceExists_WhenCalled_ReturnsFalse()
    {
        // Arrange
        var processor = CreateProcessorWithoutExecutionSource();

        // Act
        var exists = processor.SequenceExists(string.Empty, "seq_users");

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public void ProcessorConstraintExists_WhenCalled_ReturnsFalse()
    {
        // Arrange
        var processor = CreateProcessorWithoutExecutionSource();

        // Act
        var exists = processor.ConstraintExists(string.Empty, "users", "FK_users_roles");

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public void FluentMigratorSharpRunnerMigrateUp_WhenCalled_DelegatesToMigrationRunner()
    {
        // Arrange
        var migrationRunner = new Mock<IMigrationRunner>(MockBehavior.Strict);
        migrationRunner.Setup(x => x.MigrateUp());
        var runner = new FluentMigratorSharpRunner(migrationRunner.Object);

        // Act
        runner.MigrateUp();

        // Assert
        migrationRunner.Verify(x => x.MigrateUp(), Times.Once);
    }

    [Fact]
    public void FluentMigratorSharpRunnerMigrateUpTo_WhenCalled_DelegatesToMigrationRunner()
    {
        // Arrange
        var migrationRunner = new Mock<IMigrationRunner>(MockBehavior.Strict);
        migrationRunner.Setup(x => x.MigrateUp(42));
        var runner = new FluentMigratorSharpRunner(migrationRunner.Object);

        // Act
        runner.MigrateUpTo(42);

        // Assert
        migrationRunner.Verify(x => x.MigrateUp(42), Times.Once);
    }

    [Fact]
    public void FluentMigratorSharpRunnerRollback_WhenStepsPositive_DelegatesToMigrationRunner()
    {
        // Arrange
        var migrationRunner = new Mock<IMigrationRunner>(MockBehavior.Strict);
        migrationRunner.Setup(x => x.Rollback(2));
        var runner = new FluentMigratorSharpRunner(migrationRunner.Object);

        // Act
        runner.Rollback(2);

        // Assert
        migrationRunner.Verify(x => x.Rollback(2), Times.Once);
    }

    [Fact]
    public void FluentMigratorSharpRunnerRollback_WhenStepsNotPositive_Throws()
    {
        // Arrange
        var migrationRunner = new Mock<IMigrationRunner>(MockBehavior.Strict);
        var runner = new FluentMigratorSharpRunner(migrationRunner.Object);

        // Act
        var action = () => runner.Rollback(0);

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(action);
    }

    [Fact]
    public void VersionTableMetadata_WhenRead_HasExpectedDefaults()
    {
        // Arrange
        var metadata = new SharpCoreDbVersionTableMetaData();

        // Act
        var columnName = metadata.ColumnName;

        // Assert
        Assert.Equal("Version", columnName);
        Assert.Equal("Description", metadata.DescriptionColumnName);
        Assert.Equal("AppliedOn", metadata.AppliedOnColumnName);
        Assert.Equal("UX___SharpMigrations_Version", metadata.UniqueIndexName);
        Assert.False(metadata.OwnsSchema);
        Assert.True(metadata.CreateWithPrimaryKey);
    }

    [Fact]
    public void GrpcMigrationSqlExecutorGetOperationConnection_WhenCalled_ReturnsNull()
    {
        // Arrange
        var executor = new SharpCoreDbGrpcMigrationSqlExecutor(new SharpCoreDbGrpcMigrationOptions(GrpcConnectionString));

        // Act
        var connection = executor.GetOperationConnection();

        // Assert
        Assert.Null(connection);
    }

    [Fact]
    public void GrpcMigrationSqlExecutorExecuteSql_WhenSqlInvalid_Throws()
    {
        // Arrange
        var executor = new SharpCoreDbGrpcMigrationSqlExecutor(new SharpCoreDbGrpcMigrationOptions(GrpcConnectionString));

        // Act
        var action = () => executor.ExecuteSql(string.Empty);

        // Assert
        Assert.Throws<ArgumentException>(action);
    }

    [Fact]
    public void GrpcMigrationSqlExecutorExecuteScalar_WhenSqlInvalid_Throws()
    {
        // Arrange
        var executor = new SharpCoreDbGrpcMigrationSqlExecutor(new SharpCoreDbGrpcMigrationOptions(GrpcConnectionString));

        // Act
        var action = () => executor.ExecuteScalar(" ");

        // Assert
        Assert.Throws<ArgumentException>(action);
    }

    [Fact]
    public void GrpcMigrationSqlExecutorRead_WhenSqlInvalid_Throws()
    {
        // Arrange
        var executor = new SharpCoreDbGrpcMigrationSqlExecutor(new SharpCoreDbGrpcMigrationOptions(GrpcConnectionString));

        // Act
        var action = () => executor.Read(string.Empty);

        // Assert
        Assert.Throws<ArgumentException>(action);
    }

    private static SharpCoreDbProcessor CreateProcessorWithCustomExecutor(ISharpCoreDbMigrationSqlExecutor customExecutor)
    {
        var services = new ServiceCollection();
        services.AddSingleton(customExecutor);

        var provider = services.BuildServiceProvider();
        var executor = new SharpCoreDbMigrationExecutor(provider);

        return new SharpCoreDbProcessor("sharpcoredb://test", CreateProcessorOptions(), executor);
    }

    private static SharpCoreDbProcessor CreateProcessorWithoutExecutionSource()
    {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var executor = new SharpCoreDbMigrationExecutor(provider);

        return new SharpCoreDbProcessor("sharpcoredb://test", CreateProcessorOptions(), executor);
    }

    private static ProcessorOptions CreateProcessorOptions()
    {
        return new ProcessorOptions
        {
            PreviewOnly = false,
            ProviderSwitches = string.Empty,
            Timeout = null,
        };
    }

    private sealed class FakeDbConnection : DbConnection
    {
        private ConnectionState _state = ConnectionState.Closed;

        public List<string> ExecutedNonQuerySql { get; } = [];

        public Func<string, object?>? ScalarFactory { get; set; }

        public Func<string, DbDataReader>? ReaderFactory { get; set; }

        public override string ConnectionString { get; set; } = string.Empty;

        public override string Database => "FakeSharpCoreDB";

        public override string DataSource => "Fake";

        public override string ServerVersion => "1.0";

        public override ConnectionState State => _state;

        public override void ChangeDatabase(string databaseName)
        {
        }

        public override void Open()
        {
            _state = ConnectionState.Open;
        }

        public override void Close()
        {
            _state = ConnectionState.Closed;
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            throw new NotSupportedException();
        }

        protected override DbCommand CreateDbCommand()
        {
            return new FakeDbCommand(this);
        }
    }

    private sealed class FakeDbCommand(FakeDbConnection connection) : DbCommand
    {
        private readonly FakeDbConnection _connection = connection;
        private string _commandText = string.Empty;

        public override string CommandText
        {
            get => _commandText;
            set => _commandText = value;
        }

        public override int CommandTimeout { get; set; }

        public override CommandType CommandType { get; set; } = CommandType.Text;

        public override bool DesignTimeVisible { get; set; }

        public override UpdateRowSource UpdatedRowSource { get; set; }

        protected override DbConnection DbConnection { get; set; } = connection;

        protected override DbParameterCollection DbParameterCollection { get; } = new FakeDbParameterCollection();

        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel()
        {
        }

        public override int ExecuteNonQuery()
        {
            _connection.ExecutedNonQuerySql.Add(CommandText);
            return 1;
        }

        public override object? ExecuteScalar()
        {
            return _connection.ScalarFactory?.Invoke(CommandText);
        }

        public override void Prepare()
        {
        }

        protected override DbParameter CreateDbParameter()
        {
            return new FakeDbParameter();
        }

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            return _connection.ReaderFactory?.Invoke(CommandText) ?? new DataTable().CreateDataReader();
        }
    }

    private sealed class FakeDbParameterCollection : DbParameterCollection
    {
        private readonly List<DbParameter> _items = [];

        public override int Count => _items.Count;

        public override object SyncRoot => _items;

        public override int Add(object value)
        {
            ArgumentNullException.ThrowIfNull(value);
            _items.Add((DbParameter)value);
            return _items.Count - 1;
        }

        public override void AddRange(Array values)
        {
            foreach (var value in values)
            {
                _ = Add(value!);
            }
        }

        public override void Clear() => _items.Clear();

        public override bool Contains(object value) => _items.Contains((DbParameter)value);

        public override bool Contains(string value) => _items.Any(x => x.ParameterName == value);

        public override void CopyTo(Array array, int index)
        {
            _items.ToArray().CopyTo(array, index);
        }

        public override IEnumerator GetEnumerator() => _items.GetEnumerator();

        protected override DbParameter GetParameter(int index) => _items[index];

        protected override DbParameter GetParameter(string parameterName) => _items.First(x => x.ParameterName == parameterName);

        public override int IndexOf(object value) => _items.IndexOf((DbParameter)value);

        public override int IndexOf(string parameterName) => _items.FindIndex(x => x.ParameterName == parameterName);

        public override void Insert(int index, object value)
        {
            _items.Insert(index, (DbParameter)value);
        }

        public override void Remove(object value)
        {
            _items.Remove((DbParameter)value);
        }

        public override void RemoveAt(int index)
        {
            _items.RemoveAt(index);
        }

        public override void RemoveAt(string parameterName)
        {
            var index = IndexOf(parameterName);
            if (index >= 0)
            {
                _items.RemoveAt(index);
            }
        }

        protected override void SetParameter(int index, DbParameter value)
        {
            _items[index] = value;
        }

        protected override void SetParameter(string parameterName, DbParameter value)
        {
            var index = IndexOf(parameterName);
            if (index < 0)
            {
                _items.Add(value);
                return;
            }

            _items[index] = value;
        }
    }

    private sealed class FakeDbParameter : DbParameter
    {
        public override DbType DbType { get; set; }

        public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;

        public override bool IsNullable { get; set; }

        public override string ParameterName { get; set; } = string.Empty;

        public override string SourceColumn { get; set; } = string.Empty;

        public override object? Value { get; set; }

        public override bool SourceColumnNullMapping { get; set; }

        public override int Size { get; set; }

        public override void ResetDbType()
        {
        }
    }
}
