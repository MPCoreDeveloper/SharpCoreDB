using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia;
using SharpCoreDB.Viewer.ViewModels;
using SharpCoreDB.Data.Provider;
using SharpCoreDB.Viewer.Services;
using Avalonia.VisualTree;
using Avalonia.Styling;
using SharpCoreDB.Viewer.Models;
using System.Collections.ObjectModel;
using System.Text;
using System.Globalization;
using Avalonia.Platform.Storage;
using System.Diagnostics;
using System.Data;
using Avalonia.Media;

namespace SharpCoreDB.Viewer.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _subscribedViewModel;

    public MainWindow()
    {
        InitializeComponent();
        
        DataContextChanged += OnDataContextChanged;
        
        // Subscribe to language changes to force UI refresh
        LocalizationService.Instance.LanguageChanged += (s, e) =>
        {
            // Force rebind by resetting DataContext
            var currentContext = DataContext;
            DataContext = null;
            DataContext = currentContext;
        };
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.Disconnect();
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.ColumnsChanged -= OnColumnsChanged;
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedViewModel = null;
        }

        if (DataContext is MainWindowViewModel viewModel)
        {
            _subscribedViewModel = viewModel;
#if DEBUG
            System.Diagnostics.Debug.WriteLine("[MainWindow] DataContext set - subscribing to ColumnsChanged");
#endif
            viewModel.ColumnsChanged += OnColumnsChanged;
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnColumnsChanged(object? sender, List<string> columnNames)
    {
#if DEBUG
        System.Diagnostics.Debug.WriteLine($"[MainWindow] OnColumnsChanged fired with {columnNames.Count} columns");
#endif
        
        var dataGrid = this.FindControl<DataGrid>("ResultsDataGrid");
        if (dataGrid == null)
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine("[MainWindow] ResultsDataGrid not found!");
#endif
            return;
        }
        
        dataGrid.Columns.Clear();
        
        // Calculate equal width for all columns
        var columnWidth = columnNames.Count > 0 
            ? new DataGridLength(1.0 / columnNames.Count, DataGridLengthUnitType.Star) 
            : new DataGridLength(1, DataGridLengthUnitType.Star);
        
        var viewModel = DataContext as MainWindowViewModel;

        for (int i = 0; i < columnNames.Count; i++)
        {
            var header = columnNames[i];
            var index = i; // capture for closure
            var editorKind = InferColumnEditorKind(viewModel, index);
            
            // Create custom header template with transparent background
            var headerTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<object>((_, _) =>
            {
                var headerText = new TextBlock
                {
                    Text = header,
                    FontWeight = Avalonia.Media.FontWeight.Bold,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Padding = new Avalonia.Thickness(8, 6),
                    Background = Avalonia.Media.Brushes.Transparent
                };
                return headerText;
            });
            
            var column = new DataGridTemplateColumn
            {
                Header = header,
                HeaderTemplate = headerTemplate,
                Width = columnWidth,
                MinWidth = 80,
                IsReadOnly = false
            };

            // Read template
            var cellTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<SharpCoreDB.Viewer.Models.QueryResultRow>((row, _) =>
            {
                if (editorKind == ColumnEditorKind.Boolean)
                {
                    var readCheck = new CheckBox
                    {
                        IsEnabled = false,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    readCheck.Bind(
                        CheckBox.IsCheckedProperty,
                        new Avalonia.Data.Binding($"[{index}]")
                        {
                            Mode = Avalonia.Data.BindingMode.OneWay,
                            Converter = new SharpCoreDB.Viewer.Converters.ObjectToBooleanConverter()
                        });

                    return readCheck;
                }

                var textBlock = new TextBlock
                {
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Padding = new Avalonia.Thickness(8, 4),
                    TextWrapping = Avalonia.Media.TextWrapping.NoWrap,
                    TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
                    Background = Avalonia.Media.Brushes.Transparent
                };

                textBlock.Bind(
                    TextBlock.TextProperty,
                    new Avalonia.Data.Binding($"[{index}]")
                    {
                        Mode = Avalonia.Data.BindingMode.OneWay,
                        Converter = new SharpCoreDB.Viewer.Converters.ObjectToStringConverter()
                    });

                return textBlock;
            });

            // Edit template
            var editingTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<SharpCoreDB.Viewer.Models.QueryResultRow>((row, _) =>
            {
                if (editorKind == ColumnEditorKind.Boolean)
                {
                    var editCheck = new CheckBox
                    {
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    editCheck.Bind(
                        CheckBox.IsCheckedProperty,
                        new Avalonia.Data.Binding($"[{index}]")
                        {
                            Mode = Avalonia.Data.BindingMode.TwoWay,
                            UpdateSourceTrigger = Avalonia.Data.UpdateSourceTrigger.PropertyChanged,
                            Converter = new SharpCoreDB.Viewer.Converters.ObjectToBooleanConverter()
                        });

                    return editCheck;
                }

                var editor = new TextBox
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    MinWidth = 60
                };

                Avalonia.Data.Converters.IValueConverter converter = editorKind switch
                {
                    ColumnEditorKind.Number => new SharpCoreDB.Viewer.Converters.ObjectToDecimalConverter(),
                    ColumnEditorKind.DateTime => new SharpCoreDB.Viewer.Converters.ObjectToDateTimeConverter(),
                    _ => new SharpCoreDB.Viewer.Converters.ObjectToStringConverter()
                };

                editor.Bind(
                    TextBox.TextProperty,
                    new Avalonia.Data.Binding($"[{index}]")
                    {
                        Mode = Avalonia.Data.BindingMode.TwoWay,
                        UpdateSourceTrigger = Avalonia.Data.UpdateSourceTrigger.PropertyChanged,
                        Converter = converter
                    });

                return editor;
            });

            column.CellTemplate = cellTemplate;
            column.CellEditingTemplate = editingTemplate;
            dataGrid.Columns.Add(column);
            
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[MainWindow]   Added DataGridTemplateColumn '{header}' with indexer binding '[{index}]'");
#endif
        }

        // Ensure grid refreshes
        dataGrid.InvalidateMeasure();
        dataGrid.InvalidateArrange();
        dataGrid.InvalidateVisual();
        dataGrid.ItemsSource = null;
        dataGrid.ItemsSource = (DataContext as MainWindowViewModel)?.QueryResults;
        
#if DEBUG
        System.Diagnostics.Debug.WriteLine($"[MainWindow] ? DataGrid configured with {dataGrid.Columns.Count} columns using indexer binding");
#endif
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "HasResults" && sender is MainWindowViewModel viewModel && viewModel.HasResults)
        {
            var dataGrid = this.FindControl<DataGrid>("ResultsDataGrid");
            if (dataGrid != null)
            {
                dataGrid.InvalidateVisual();
                dataGrid.InvalidateArrange();
                dataGrid.InvalidateMeasure();
                
#if DEBUG
                System.Diagnostics.Debug.WriteLine("[MainWindow] DataGrid invalidated after HasResults became true");
#endif
            }
        }
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        
        // Show connection dialog on startup
        await ShowConnectionDialog();
    }

    private async void OnConnectClicked(object? sender, RoutedEventArgs e)
    {
        await ShowConnectionDialog();
    }

    private async void OnSettingsClicked(object? sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog();
        await dialog.ShowDialog(this);
    }
    
    private async void OnToolsClicked(object? sender, RoutedEventArgs e)
    {
        var dialog = new ToolsDialog
        {
            DataContext = new ViewModels.ToolsViewModel()
        };
        await dialog.ShowDialog(this);
    }

    private void OnExitClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.CleanupOnWindowClose();
        }

        Close();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.CleanupOnWindowClose();
        }

        base.OnClosing(e);
    }

    private async Task ShowConnectionDialog()
    {
        var dialog = new Views.ConnectionDialog
        {
            DataContext = new ViewModels.ConnectionDialogViewModel()
        };

        var result = await dialog.ShowDialog<SharpCoreDBConnection?>(this);

        if (result != null && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SetConnection(result);
        }
    }

    private async void OnSelectTopNTableClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var tableName = ResolveTableNameFromContextMenu(sender);
        if (viewModel.SelectTop100Command.CanExecute(tableName))
        {
            await viewModel.SelectTop100Command.ExecuteAsync(tableName).ConfigureAwait(true);
        }
    }

    private async void OnScriptTableClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var tableName = ResolveTableNameFromContextMenu(sender);
        if (viewModel.ScriptSelectedTableCommand.CanExecute(tableName))
        {
            await viewModel.ScriptSelectedTableCommand.ExecuteAsync(tableName).ConfigureAwait(true);
        }
    }

    private async void OnRenameTableClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var tableName = ResolveTableNameFromContextMenu(sender) ?? viewModel.SelectedTable;
        if (string.IsNullOrWhiteSpace(tableName))
        {
            viewModel.StatusMessage = "Table name is required.";
            // return;
        }

        viewModel.SelectedTable = tableName;

        var newName = await PromptForTextAsync(
            title: "Rename Table",
            prompt: $"New name for '{tableName}':",
            initialValue: tableName).ConfigureAwait(true);

        if (string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        viewModel.RenameTableName = newName.Trim();
        if (viewModel.RenameSelectedTableCommand.CanExecute(tableName))
        {
            await viewModel.RenameSelectedTableCommand.ExecuteAsync(tableName).ConfigureAwait(true);
        }
    }

    private async void OnDropSelectedTableClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || string.IsNullOrWhiteSpace(viewModel.SelectedTable))
        {
            return;
        }

        var tableName = viewModel.SelectedTable;
        var confirm = await ConfirmDropByTypingAsync(tableName).ConfigureAwait(true);
        if (!confirm)
        {
            return;
        }

        if (viewModel.DropSelectedTableCommand.CanExecute(tableName))
        {
            await viewModel.DropSelectedTableCommand.ExecuteAsync(tableName).ConfigureAwait(true);
        }
    }

    private async void OnOpenCreateTableDialogClicked(object? sender, RoutedEventArgs e)
    {
        await OpenTableDesignerDialogAsync(null).ConfigureAwait(true);
    }

    private async void OnOpenDesignTableClicked(object? sender, RoutedEventArgs e)
    {
        var tableName = ResolveTableNameFromContextMenu(sender);
        await OpenTableDesignerDialogAsync(tableName).ConfigureAwait(true);
    }

    private async void OnOpenCreateProcedureDialogClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (viewModel.CreateNewProcedureCommand.CanExecute(null))
        {
            await viewModel.CreateNewProcedureCommand.ExecuteAsync(null).ConfigureAwait(true);
        }
    }

    private async void OnOpenCreateTriggerDialogClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (viewModel.CreateNewTriggerCommand.CanExecute(null))
        {
            await viewModel.CreateNewTriggerCommand.ExecuteAsync(null).ConfigureAwait(true);
        }
    }

    private async void OnOpenCreateTableFromTemplateClicked(object? sender, RoutedEventArgs e)
    {
        await OpenTableDesignerDialogAsync(null).ConfigureAwait(true);
    }

    private async void OnImportCsvToNewTableClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select CSV File",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("CSV Files") { Patterns = ["*.csv"] }]
        });

        if (files.Count == 0)
        {
            return;
        }

        var path = files[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var tableName = await PromptForTextAsync("Import CSV", "Table name to create:", System.IO.Path.GetFileNameWithoutExtension(path)).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(tableName) || viewModel.ActiveConnection is null)
        {
            return;
        }

        try
        {
            var lines = await System.IO.File.ReadAllLinesAsync(path).ConfigureAwait(true);
            if (lines.Length == 0)
            {
                viewModel.StatusMessage = "CSV file is empty.";
                return;
            }

            var headers = lines[0].Split(',');
            var columnsSql = string.Join(", ", headers.Select(h => $"\"{h.Trim().Replace("\"", "\"\"", StringComparison.Ordinal)}\" TEXT"));
            var createSql = $"CREATE TABLE \"{tableName}\" ({columnsSql})";

            if (viewModel.ExecuteQueryCommand.CanExecute(createSql))
            {
                await viewModel.ExecuteQueryCommand.ExecuteAsync(createSql).ConfigureAwait(true);
            }

            var insertStatements = new List<string>();
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                var values = lines[i].Split(',');
                var valuesSql = string.Join(", ", values.Select(v => $"'{v.Trim().Replace("'", "''", StringComparison.Ordinal)}'"));
                insertStatements.Add($"INSERT INTO \"{tableName}\" VALUES ({valuesSql})");
            }

            foreach (var stmt in insertStatements)
            {
                if (viewModel.ExecuteQueryCommand.CanExecute(stmt))
                {
                    await viewModel.ExecuteQueryCommand.ExecuteAsync(stmt).ConfigureAwait(true);
                }
            }

            if (viewModel.RefreshTablesCommand.CanExecute(null))
            {
                viewModel.RefreshTablesCommand.Execute(null);
            }

            viewModel.StatusMessage = $"Imported CSV to table '{tableName}'.";
        }
        catch (Exception ex)
        {
            viewModel.StatusMessage = $"CSV import failed: {ex.Message}";
        }
    }

    private async void OnPreviewTableClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var tableName = ResolveTableNameFromContextMenu(sender);
        if (viewModel.PreviewSelectedTableCommand.CanExecute(tableName))
        {
            await viewModel.PreviewSelectedTableCommand.ExecuteAsync(tableName).ConfigureAwait(true);
        }
    }

    private async void OnScriptObjectClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var node = ResolveExplorerNodeFromContextMenu(sender);
        if (node is not null)
        {
            viewModel.SelectedExplorerNode = node;
        }

        if (viewModel.ScriptSelectedObjectCommand.CanExecute(null))
        {
            await viewModel.ScriptSelectedObjectCommand.ExecuteAsync(null).ConfigureAwait(true);
        }
    }

    private async void OnInsertTemplateClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var tableName = ResolveTableNameFromContextMenu(sender);
        if (viewModel.GenerateInsertTemplateCommand.CanExecute(tableName))
        {
            await viewModel.GenerateInsertTemplateCommand.ExecuteAsync(tableName).ConfigureAwait(true);
        }
    }

    private async void OnUpdateTemplateClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var tableName = ResolveTableNameFromContextMenu(sender);
        if (viewModel.GenerateUpdateTemplateCommand.CanExecute(tableName))
        {
            await viewModel.GenerateUpdateTemplateCommand.ExecuteAsync(tableName).ConfigureAwait(true);
        }
    }

    private async void OnDeleteTemplateClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var tableName = ResolveTableNameFromContextMenu(sender);
        if (viewModel.GenerateDeleteTemplateCommand.CanExecute(tableName))
        {
            await viewModel.GenerateDeleteTemplateCommand.ExecuteAsync(tableName).ConfigureAwait(true);
        }
    }

    private async void OnRenameObjectClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var node = ResolveExplorerNodeFromContextMenu(sender);
        if (node is not null)
        {
            viewModel.SelectedExplorerNode = node;
        }

        var objectName = ResolveTableNameFromContextMenu(sender) ?? viewModel.SelectedTable;
        if (string.IsNullOrWhiteSpace(objectName))
        {
            return;
        }

        var newName = await PromptForTextAsync("Rename", $"New name for '{objectName}':", objectName).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        viewModel.RenameTableName = newName.Trim();
        if (viewModel.RenameSelectedTableCommand.CanExecute(objectName))
        {
            await viewModel.RenameSelectedTableCommand.ExecuteAsync(objectName).ConfigureAwait(true);
        }
    }

    private async void OnDropObjectClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var node = ResolveExplorerNodeFromContextMenu(sender);
        if (node is not null)
        {
            viewModel.SelectedExplorerNode = node;
        }

        var objectName = ResolveTableNameFromContextMenu(sender);
        if (string.IsNullOrWhiteSpace(objectName))
        {
            return;
        }

        var confirm = await ConfirmDropByTypingAsync(objectName).ConfigureAwait(true);
        if (!confirm)
        {
            return;
        }

        if (viewModel.DropSelectedObjectCommand.CanExecute(null))
        {
            await viewModel.DropSelectedObjectCommand.ExecuteAsync(null).ConfigureAwait(true);
        }
    }

    private void OnOpenSqlSyntaxReferenceClicked(object? sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://sharpcoredb.com/docs/sql-syntax") { UseShellExecute = true });
    }

    private void OnOpenUserManualClicked(object? sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://sharpcoredb.com/docs") { UseShellExecute = true });
    }

    private static ExplorerNode? ResolveExplorerNodeFromContextMenu(object? sender)
    {
        if (sender is not MenuItem menuItem)
        {
            return null;
        }

        if (menuItem.DataContext is ExplorerNode node)
        {
            return node;
        }

        var visualParent = menuItem.GetVisualParent();
        while (visualParent is not null)
        {
            if (visualParent is StyledElement styledElement && styledElement.DataContext is ExplorerNode parentNode)
            {
                return parentNode;
            }

            visualParent = visualParent.GetVisualParent();
        }

        return null;
    }

    private enum ColumnEditorKind
    {
        Text,
        Number,
        DateTime,
        Boolean
    }

    private static ColumnEditorKind InferColumnEditorKind(MainWindowViewModel? viewModel, int index)
    {
        if (viewModel is null) return ColumnEditorKind.Text;
        var columnName = viewModel.ResultColumns[index];
        var sampleValue = viewModel.QueryResults.Count > 0
            ? viewModel.QueryResults[0].GetValue(columnName, viewModel.ResultColumns)
            : null;
        if (sampleValue is bool) return ColumnEditorKind.Boolean;
        if (sampleValue is DateTime or DateTimeOffset) return ColumnEditorKind.DateTime;
        if (sampleValue is int or long or decimal or double or float) return ColumnEditorKind.Number;
        return ColumnEditorKind.Text;
    }

    private static string? ResolveTableNameFromContextMenu(object? sender)
    {
        if (sender is not MenuItem menuItem)
        {
            return null;
        }
        if (menuItem.DataContext is string directName)
        {
            return directName;
        }
        if (menuItem.DataContext is ExplorerNode node)
        {
            return node.NodeType == ExplorerNodeType.Table ? node.Name : node.TableName;
        }
        var visualParent = menuItem.GetVisualParent();
        while (visualParent is not null)
        {
            if (visualParent is StyledElement styledElement)
            {
                if (styledElement.DataContext is string tableName)
                {
                    return tableName;
                }
                if (styledElement.DataContext is ExplorerNode parentNode)
                {
                    return parentNode.NodeType == ExplorerNodeType.Table ? parentNode.Name : parentNode.TableName;
                }
            }
            visualParent = visualParent.GetVisualParent();
        }
        return null;
    }

    private async Task<string?> PromptForTextAsync(string title, string prompt, string? initialValue = null)
    {
        var panel = new StackPanel { Spacing = 10, Margin = new Thickness(10) };
        panel.Children.Add(new TextBlock { Text = prompt, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var input = new TextBox { Text = initialValue, Watermark = "Enter text" };
        var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "OK", MinWidth = 80 };
        var cancel = new Button { Content = "Cancel", MinWidth = 80 };
        buttonPanel.Children.Add(ok);
        buttonPanel.Children.Add(cancel);
        panel.Children.Add(input);
        panel.Children.Add(buttonPanel);
        var dialog = new Window
        {
            Title = title,
            Width = 400,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel
        };
        string? result = null;
        ok.Click += (_, _) => { result = input.Text; dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this).ConfigureAwait(true);
        return result;
    }

    private async Task<bool> ConfirmDropByTypingAsync(string tableName)
    {
        var panel = new StackPanel { Spacing = 10, Margin = new Thickness(10) };
        panel.Children.Add(new TextBlock { Text = $"Type the table name '{tableName}' to confirm drop.", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var input = new TextBox { Watermark = "Type table name to confirm" };
        var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var drop = new Button { Content = "Drop", MinWidth = 80 };
        var cancel = new Button { Content = "Cancel", MinWidth = 80 };
        buttonPanel.Children.Add(drop);
        buttonPanel.Children.Add(cancel);
        panel.Children.Add(input);
        panel.Children.Add(buttonPanel);
        var dialog = new Window
        {
            Title = "Confirm Drop Table",
            Width = 470,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel
        };
        bool confirmed = false;
        drop.Click += (_, _) => { confirmed = string.Equals(input.Text?.Trim(), tableName, StringComparison.Ordinal); dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this).ConfigureAwait(true);
        return confirmed;
    }

    private async Task<List<TableDesignerColumnRow>> LoadDesignerRowsForTableAsync(SharpCoreDBConnection connection, string tableName, List<TableDesignerColumnRow> rows)
    {
        rows.Clear();
        using var command = new SharpCoreDBCommand($"PRAGMA table_info('{tableName}')", connection);
        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(true);
        while (await reader.ReadAsync().ConfigureAwait(true))
        {
            rows.Add(new TableDesignerColumnRow
            {
                Name = reader.GetString(1),
                Type = reader.GetString(2),
                IsPrimaryKey = reader.GetBoolean(5)
            });
        }
        return rows;
    }

    private static string BuildColumnSql(TableDesignerColumnRow row)
    {
        var sql = $"\"{row.Name.Replace("\"", "\"\"", StringComparison.Ordinal)}\" {row.Type.ToUpperInvariant()}";
        if (row.IsPrimaryKey) sql += " PRIMARY KEY";
        return sql;
    }

    private static List<string> BuildRecreateTableSql(string tableName, List<TableDesignerColumnRow> rows)
    {
        var tempName = $"{tableName}_temp";
        var columnsSql = string.Join(", ", rows.Select(BuildColumnSql));
        var columnsList = string.Join(", ", rows.Select(r => $"\"{r.Name.Replace("\"", "\"\"", StringComparison.Ordinal)}\""));
        return [
            $"CREATE TABLE {tempName} ({columnsSql})",
            $"INSERT INTO {tempName} ({columnsList}) SELECT {columnsList} FROM {tableName}",
            $"DROP TABLE {tableName}",
            $"ALTER TABLE {tempName} RENAME TO {tableName}"
        ];
    }

    private static string BuildColumnsSqlFromDesigner(List<TableDesignerColumnRow> rows)
    {
        return string.Join(", ", rows.Select(BuildColumnSql));
    }

    private async Task OpenTableDesignerDialogAsync(string? tableName)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var isNew = string.IsNullOrWhiteSpace(tableName);
        var rows = new System.Collections.ObjectModel.ObservableCollection<TableDesignerColumnRow>();

        if (!isNew && viewModel.ActiveConnection is not null)
        {
            var list = new List<TableDesignerColumnRow>();
            await LoadDesignerRowsForTableAsync(viewModel.ActiveConnection, tableName!, list).ConfigureAwait(true);
            foreach (var r in list) rows.Add(r);
        }

        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock
        {
            Text = isNew ? "Create New Table" : $"Design Table: {tableName}",
            FontWeight = Avalonia.Media.FontWeight.Bold
        });

        var nameBox = new TextBox { Watermark = "Table name", Text = isNew ? string.Empty : tableName };
        if (!isNew) nameBox.IsEnabled = false;
        panel.Children.Add(nameBox);

        var grid = new DataGrid
        {
            ItemsSource = rows,
            AutoGenerateColumns = false,
            Height = 240
        };
        grid.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = new Avalonia.Data.Binding("Name"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Type", Binding = new Avalonia.Data.Binding("Type"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "PK", Binding = new Avalonia.Data.Binding("IsPrimaryKey"), Width = new DataGridLength(60) });
        panel.Children.Add(grid);

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var saveBtn = new Button { Content = isNew ? "Create" : "Save", MinWidth = 80 };
        var cancelBtn = new Button { Content = "Cancel", MinWidth = 80 };
        btnPanel.Children.Add(saveBtn);
        btnPanel.Children.Add(cancelBtn);
        panel.Children.Add(btnPanel);

        var dialog = new Window
        {
            Title = isNew ? "Create Table" : "Design Table",
            Width = 520,
            Height = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel
        };

        saveBtn.Click += async (_, _) =>
        {
            var name = nameBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(BuildColumnsSqlFromDesigner([.. rows]))) return;

            if (isNew)
            {
                var sql = $"CREATE TABLE \"{name}\" ({BuildColumnsSqlFromDesigner([.. rows])})";
                if (viewModel.ExecuteQueryCommand.CanExecute(sql))
                    await viewModel.ExecuteQueryCommand.ExecuteAsync(sql).ConfigureAwait(true);
            }
            else
            {
                foreach (var stmt in BuildRecreateTableSql(tableName!, [.. rows]))
                {
                    if (viewModel.ExecuteQueryCommand.CanExecute(stmt))
                        await viewModel.ExecuteQueryCommand.ExecuteAsync(stmt).ConfigureAwait(true);
                }
            }

            if (viewModel.RefreshTablesCommand.CanExecute(null))
                viewModel.RefreshTablesCommand.Execute(null);

            dialog.Close();
        };

        cancelBtn.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this).ConfigureAwait(true);
    }
}
