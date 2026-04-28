using SharpCoreDB.Data.Provider;
using SharpCoreDB.Viewer.ViewModels;
using System.Reflection;

namespace SharpCoreDB.Viewer.Tests;

public sealed class MainWindowViewModelSmokeTests
{
    [Fact]
    public void EnableResultEditing_WithResults_SetsEditMode()
    {
        var viewModel = new MainWindowViewModel
        {
            HasResults = true
        };

        viewModel.EnableResultEditingCommand.Execute(null);

        Assert.True(viewModel.IsResultEditable);
        Assert.Equal(ResultMode.Edit, viewModel.ResultMode);
    }

    [Fact]
    public void Disconnect_ResetsPagingAndState()
    {
        var viewModel = new MainWindowViewModel
        {
            IsConnected = true,
            IsResultEditable = true,
            ResultMode = ResultMode.Edit,
            CurrentOffset = 400,
            CurrentPreviewTotalRows = 1000,
            CanGoNextPage = true,
            CanGoPreviousPage = true
        };

        viewModel.DisconnectCommand.Execute(null);

        Assert.False(viewModel.IsConnected);
        Assert.False(viewModel.IsResultEditable);
        Assert.Equal(ResultMode.View, viewModel.ResultMode);
        Assert.Equal(0, viewModel.CurrentOffset);
        Assert.Equal(0, viewModel.CurrentPreviewTotalRows);
        Assert.False(viewModel.CanGoNextPage);
        Assert.False(viewModel.CanGoPreviousPage);
    }

    [Fact]
    public void SetConnection_WithExistingConnection_ReplacesConnectionSafely()
    {
        var viewModel = new MainWindowViewModel();

        using var first = CreateConnection();
        using var second = CreateConnection();

        viewModel.SetConnection(first);
        viewModel.SetConnection(second);

        Assert.Same(second, viewModel.ActiveConnection);
    }

    [Fact]
    public void ToSqlLiteral_WithNull_ReturnsNullLiteral()
    {
        var method = typeof(MainWindowViewModel).GetMethod("ToSqlLiteral", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var result = (string?)method!.Invoke(null, [null]);

        Assert.Equal("NULL", result);
    }

    [Fact]
    public void ExplorerCommands_WithoutSelectedTable_AreDisabled()
    {
        var viewModel = new MainWindowViewModel
        {
            SelectedTable = null
        };

        Assert.False(viewModel.PreviewSelectedTableCommand.CanExecute(null));
        Assert.False(viewModel.SelectTop100Command.CanExecute(null));
        Assert.False(viewModel.ScriptSelectedTableCommand.CanExecute(null));
        Assert.False(viewModel.DropSelectedTableCommand.CanExecute(null));
    }

    [Fact]
    public void ExplorerCommands_WithSelectedTable_AreEnabled()
    {
        var viewModel = new MainWindowViewModel
        {
            SelectedTable = "Users"
        };

        Assert.True(viewModel.PreviewSelectedTableCommand.CanExecute(null));
        Assert.True(viewModel.SelectTop100Command.CanExecute(null));
        Assert.True(viewModel.ScriptSelectedTableCommand.CanExecute(null));
        Assert.True(viewModel.DropSelectedTableCommand.CanExecute(null));
    }

    [Fact]
    public void IsValidSqlIdentifier_WithInvalidName_ReturnsFalse()
    {
        var method = typeof(MainWindowViewModel).GetMethod("IsValidSqlIdentifier", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var viewModel = new MainWindowViewModel();
        var result = (bool?)method!.Invoke(viewModel, ["123 bad-name"]);

        Assert.False(result);
    }

    [Fact]
    public void IsValidSqlIdentifier_WithValidName_ReturnsTrue()
    {
        var method = typeof(MainWindowViewModel).GetMethod("IsValidSqlIdentifier", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var viewModel = new MainWindowViewModel();
        var result = (bool?)method!.Invoke(viewModel, ["users_2026"]);

        Assert.True(result);
    }

    [Fact]
    public void ResolveSelectTopNOrDefault_WithInvalidText_ReturnsDefault100()
    {
        var method = typeof(MainWindowViewModel).GetMethod("ResolveSelectTopNOrDefault", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var viewModel = new MainWindowViewModel
        {
            SelectTopNText = "abc"
        };

        var result = (int?)method!.Invoke(viewModel, []);

        Assert.Equal(100, result);
    }

    [Fact]
    public void ResolveSelectTopNOrDefault_WithOutOfRangeValue_ClampsToBounds()
    {
        var method = typeof(MainWindowViewModel).GetMethod("ResolveSelectTopNOrDefault", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var viewModel = new MainWindowViewModel
        {
            SelectTopNText = "999999"
        };

        var result = (int?)method!.Invoke(viewModel, []);

        Assert.Equal(10_000, result);
    }

    [Fact]
    public async Task CreateTable_Disconnect_Reconnect_TableRemainsVisibleInExplorer()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"viewer-persist-{Guid.NewGuid():N}.scdb");
        var connectionString = $"Path={dbPath};Password=test;StorageMode=SingleFile";

        try
        {
            var viewModel = new MainWindowViewModel();

            using (var connection = new SharpCoreDBConnection(connectionString))
            {
                viewModel.SetConnection(connection);
                viewModel.NewTableName = "PersistUsers";
                viewModel.NewTableColumnsDefinition = "Id INTEGER PRIMARY KEY, Name TEXT";
                await viewModel.CreateNewTableCommand.ExecuteAsync(null);

                Assert.Contains("PersistUsers", viewModel.Tables);
            }

            viewModel.DisconnectCommand.Execute(null);

            using (var reconnect = new SharpCoreDBConnection(connectionString))
            {
                viewModel.SetConnection(reconnect);

                // Allow async table load to complete in ViewModel fire-and-forget path.
                await Task.Delay(200);

                Assert.Contains("PersistUsers", viewModel.Tables);
                Assert.Contains(viewModel.ExplorerNodes, n => n.Name == "PersistUsers");
            }
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                try
                {
                    File.Delete(dbPath);
                }
                catch
                {
                    // Ignore cleanup errors.
                }
            }
        }
    }

    private static SharpCoreDBConnection CreateConnection()
    {
        var path = Path.Combine(Path.GetTempPath(), $"viewer-smoke-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        var connectionString = $"Path={path};Password=test;StorageMode=Directory";
        return new SharpCoreDBConnection(connectionString);
    }
}
