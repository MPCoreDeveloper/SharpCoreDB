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
            return;
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

    private async Task OpenTableDesignerDialogAsync(string? initialTableName)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var tableName = initialTableName ?? string.Empty;

        var panel = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(10)
        };

        var nameBox = new TextBox
        {
            Watermark = "Table name",
            Text = tableName,
            IsEnabled = string.IsNullOrWhiteSpace(initialTableName)
        };

        var rows = new ObservableCollection<TableDesignerColumnRow>();

        if (!string.IsNullOrWhiteSpace(initialTableName) && viewModel.ActiveConnection is not null)
        {
            await LoadDesignerRowsForTableAsync(viewModel.ActiveConnection, initialTableName, rows).ConfigureAwait(true);
        }

        if (rows.Count == 0)
        {
            rows.Add(new TableDesignerColumnRow { Name = "Id", Type = "INTEGER", IsNullable = false, IsPrimaryKey = true });
            rows.Add(new TableDesignerColumnRow { Name = "Name", Type = "TEXT", IsNullable = false, IsPrimaryKey = false });
        }

        var supportedTypes = new[]
        {
            "INTEGER",
            "BIGINT",
            "TEXT",
            "REAL",
            "BLOB",
            "BOOLEAN",
            "DATETIME",
            "LONG",
            "DECIMAL",
            "ULID",
            "GUID",
            "ROWREF",
            "VECTOR"
        };

        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            Height = 190,
            ItemsSource = rows,
            CanUserReorderColumns = false,
            CanUserResizeColumns = true
        };

        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Name",
            Binding = new Avalonia.Data.Binding(nameof(TableDesignerColumnRow.Name)) { Mode = Avalonia.Data.BindingMode.TwoWay },
            Width = new DataGridLength(2, DataGridLengthUnitType.Star)
        });

        var typeTemplateColumn = new DataGridTemplateColumn
        {
            Header = "Type",
            Width = new DataGridLength(2, DataGridLengthUnitType.Star)
        };

        typeTemplateColumn.CellTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<TableDesignerColumnRow>((row, _) =>
        {
            var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            text.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(TableDesignerColumnRow.Type)));
            return text;
        });

        typeTemplateColumn.CellEditingTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<TableDesignerColumnRow>((row, _) =>
        {
            var combo = new ComboBox
            {
                ItemsSource = supportedTypes,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinWidth = 120,
                SelectedItem = string.IsNullOrWhiteSpace(row.Type) ? "TEXT" : row.Type.ToUpperInvariant(),
                IsDropDownOpen = true
            };

            combo.Bind(
                ComboBox.SelectedItemProperty,
                new Avalonia.Data.Binding(nameof(TableDesignerColumnRow.Type))
                {
                    Mode = Avalonia.Data.BindingMode.TwoWay,
                    UpdateSourceTrigger = Avalonia.Data.UpdateSourceTrigger.PropertyChanged
                });

            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is string selected)
                {
                    row.Type = selected;
                }
            };

            combo.AttachedToVisualTree += (_, _) =>
            {
                combo.Focus();
                combo.IsDropDownOpen = true;
            };

            return combo;
        });

        grid.Columns.Add(typeTemplateColumn);

        grid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "Nullable",
            Binding = new Avalonia.Data.Binding(nameof(TableDesignerColumnRow.IsNullable)) { Mode = Avalonia.Data.BindingMode.TwoWay },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });

        grid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "Primary Key",
            Binding = new Avalonia.Data.Binding(nameof(TableDesignerColumnRow.IsPrimaryKey)) { Mode = Avalonia.Data.BindingMode.TwoWay },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var addColumnButton = new Button { Content = "Add Column", MinWidth = 100 };
        var removeColumnButton = new Button { Content = "Remove Selected", MinWidth = 120 };
        var createButton = new Button { Content = "Create", MinWidth = 90 };

        buttonPanel.Children.Add(addColumnButton);
        buttonPanel.Children.Add(removeColumnButton);
        buttonPanel.Children.Add(createButton);

        panel.Children.Add(new TextBlock { Text = "Design new table" });
        panel.Children.Add(nameBox);
        panel.Children.Add(grid);
        panel.Children.Add(buttonPanel);

        var dialog = new Window
        {
            Title = "Open Design Table",
            Width = 780,
            Height = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel
        };

        addColumnButton.Click += (_, _) => rows.Add(new TableDesignerColumnRow { Type = "TEXT", IsNullable = true });

        removeColumnButton.Click += (_, _) =>
        {
            if (grid.SelectedItem is TableDesignerColumnRow selected)
            {
                rows.Remove(selected);
            }
        };

        createButton.Click += async (_, _) =>
        {
            var tableNameText = nameBox.Text?.Trim() ?? string.Empty;
            var columnSql = BuildColumnsSqlFromDesigner(rows);
            if (string.IsNullOrWhiteSpace(tableNameText) || string.IsNullOrWhiteSpace(columnSql))
            {
                viewModel.StatusMessage = "Table name and at least one valid column are required.";
                return;
            }

            var previousName = viewModel.NewTableName;
            var previousColumns = viewModel.NewTableColumnsDefinition;

            viewModel.NewTableName = tableNameText;
            viewModel.NewTableColumnsDefinition = columnSql;

            if (viewModel.CreateNewTableCommand.CanExecute(null))
            {
                await viewModel.CreateNewTableCommand.ExecuteAsync(null).ConfigureAwait(true);

                if (string.Equals(viewModel.SelectedTable, tableNameText, StringComparison.OrdinalIgnoreCase))
                {
                    dialog.Close();
                }
                else
                {
                    viewModel.NewTableName = previousName;
                    viewModel.NewTableColumnsDefinition = previousColumns;
                }
            }
        };

        await dialog.ShowDialog(this);
    }

    private async void OnDropTableClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var tableName = ResolveTableNameFromContextMenu(sender) ?? viewModel.SelectedTable;
        if (string.IsNullOrWhiteSpace(tableName))
        {
            return;
        }

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

    private async void OnOpenCreateTableFromTemplateClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var panel = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(10)
        };

        var templateCombo = new ComboBox
        {
            ItemsSource = new[] { "Basic (Id, Name)", "Audit (Id, Name, CreatedAt, UpdatedAt)", "Graph Edge (FromNode, ToNode)" },
            SelectedIndex = 0
        };

        var nameBox = new TextBox
        {
            Watermark = "Table name"
        };

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var create = new Button { Content = "Create", MinWidth = 80 };
        var cancel = new Button { Content = "Cancel", MinWidth = 80 };

        buttonPanel.Children.Add(create);
        buttonPanel.Children.Add(cancel);

        panel.Children.Add(new TextBlock { Text = "Choose a table template" });
        panel.Children.Add(templateCombo);
        panel.Children.Add(nameBox);
        panel.Children.Add(buttonPanel);

        var dialog = new Window
        {
            Title = "New Table from Template",
            Width = 460,
            Height = 230,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel
        };

        create.Click += async (_, _) =>
        {
            var tableName = nameBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(tableName))
            {
                viewModel.StatusMessage = "Table name is required.";
                return;
            }

            var selectedTemplate = templateCombo.SelectedItem?.ToString() ?? "Basic (Id, Name)";
            viewModel.NewTableName = tableName;
            viewModel.NewTableColumnsDefinition = selectedTemplate switch
            {
                "Audit (Id, Name, CreatedAt, UpdatedAt)" => "Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, CreatedAt DATETIME NOT NULL, UpdatedAt DATETIME NOT NULL",
                "Graph Edge (FromNode, ToNode)" => "FromNode ROWREF NOT NULL, ToNode ROWREF NOT NULL",
                _ => "Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL"
            };

            if (viewModel.CreateNewTableCommand.CanExecute(null))
            {
                await viewModel.CreateNewTableCommand.ExecuteAsync(null).ConfigureAwait(true);
            }

            dialog.Close();
        };

        cancel.Click += (_, _) => dialog.Close();

        await dialog.ShowDialog(this);
    }

    private async void OnImportCsvToNewTableClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select CSV file",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("CSV") { Patterns = ["*.csv"] },
                FilePickerFileTypes.All
            ]
        }).ConfigureAwait(true);

        var file = files.FirstOrDefault();
        var filePath = file?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return;
        }

        string[] lines;
        try
        {
            lines = await File.ReadAllLinesAsync(filePath).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            viewModel.StatusMessage = $"Failed to read CSV: {ex.Message}";
            return;
        }

        if (lines.Length == 0)
        {
            viewModel.StatusMessage = "CSV file is empty.";
            return;
        }

        var headers = lines[0].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (headers.Length == 0)
        {
            viewModel.StatusMessage = "CSV header is invalid.";
            return;
        }

        var suggestedName = Path.GetFileNameWithoutExtension(filePath);
        var tableName = await PromptForTextAsync("Import CSV", "Table name:", suggestedName).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(tableName))
        {
            return;
        }

        var sanitizedColumns = headers
            .Select(h => string.Concat(h.Where(c => char.IsLetterOrDigit(c) || c == '_')))
            .Select(h => string.IsNullOrWhiteSpace(h) ? "Column" : h)
            .Select((h, i) => i == 0 ? h : (h == headers[0] ? $"{h}_{i}" : h))
            .ToList();

        var uniqueColumns = new List<string>();
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in sanitizedColumns)
        {
            if (!seen.TryAdd(column, 1))
            {
                seen[column]++;
                uniqueColumns.Add($"{column}_{seen[column]}");
            }
            else
            {
                uniqueColumns.Add(column);
            }
        }

        var createCols = string.Join(", ", uniqueColumns.Select(c => $"\"{c.Replace("\"", "\"\"", StringComparison.Ordinal)}\" TEXT NULL"));
        string escapedTable = tableName.Trim().Replace("\"", "\"\"");

        try
        {
            using var createCmd = new SharpCoreDBCommand($"CREATE TABLE \"{escapedTable}\" ({createCols});", viewModel.ActiveConnection!);
            await createCmd.ExecuteNonQueryAsync().ConfigureAwait(true);

            for (var i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                {
                    continue;
                }

                var values = lines[i].Split(',', StringSplitOptions.None);
                var valueSql = new List<string>(uniqueColumns.Count);
                for (var j = 0; j < uniqueColumns.Count; j++)
                {
                    var raw = j < values.Length ? values[j].Trim() : string.Empty;
                    if (string.IsNullOrEmpty(raw))
                    {
                        valueSql.Add("NULL");
                        continue;
                    }

                    var escaped = raw.Replace("'", "''", StringComparison.Ordinal);
                    valueSql.Add($"'{escaped}'");
                }

                var insertSql = $"INSERT INTO \"{escapedTable}\" ({string.Join(", ", uniqueColumns.Select(c => $"\"{c.Replace("\"", "\"\"", StringComparison.Ordinal)}\""))}) VALUES ({string.Join(", ", valueSql)});";
                using var insertCmd = new SharpCoreDBCommand(insertSql, viewModel.ActiveConnection!);
                await insertCmd.ExecuteNonQueryAsync().ConfigureAwait(true);
            }

            await viewModel.RefreshTablesCommand.ExecuteAsync(null).ConfigureAwait(true);
            viewModel.SelectedTable = tableName.Trim();
            viewModel.StatusMessage = $"CSV imported into table '{viewModel.SelectedTable}'.";
        }
        catch (Exception ex)
        {
            viewModel.StatusMessage = $"CSV import failed: {ex.Message}";
        }
    }

    private async Task<bool> ConfirmDropByTypingAsync(string tableName)
    {
        var panel = new StackPanel
        {
            Spacing = 10,
            Margin = new Thickness(10)
        };

        panel.Children.Add(new TextBlock
        {
            Text = $"Type the table name '{tableName}' to confirm drop.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });

        var input = new TextBox
        {
            Watermark = "Type table name to confirm"
        };

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };

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

        drop.Click += (_, _) => dialog.Close(string.Equals(input.Text?.Trim(), tableName, StringComparison.Ordinal));
        cancel.Click += (_, _) => dialog.Close(false);

        return await dialog.ShowDialog<bool>(this).ConfigureAwait(true);
    }

    private async Task<string?> PromptForTextAsync(string title, string prompt, string initialValue)
    {
        var panel = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(10)
        };

        var promptBlock = new TextBlock { Text = prompt };
        var input = new TextBox { Text = initialValue };

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var ok = new Button { Content = "OK", MinWidth = 80 };
        var cancel = new Button { Content = "Cancel", MinWidth = 80 };

        buttonPanel.Children.Add(ok);
        buttonPanel.Children.Add(cancel);

        panel.Children.Add(promptBlock);
        panel.Children.Add(input);
        panel.Children.Add(buttonPanel);

        var dialog = new Window
        {
            Title = title,
            Width = 420,
            Height = 170,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel
        };

        ok.Click += (_, _) => dialog.Close(input.Text);
        cancel.Click += (_, _) => dialog.Close(null);

        return await dialog.ShowDialog<string?>(this).ConfigureAwait(true);
    }

    private async void OnInsertTemplateClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var tableName = ResolveTableNameFromContextMenu(sender) ?? viewModel.SelectedTable;
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

        var tableName = ResolveTableNameFromContextMenu(sender) ?? viewModel.SelectedTable;
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

        var tableName = ResolveTableNameFromContextMenu(sender) ?? viewModel.SelectedTable;
        if (viewModel.GenerateDeleteTemplateCommand.CanExecute(tableName))
        {
            await viewModel.GenerateDeleteTemplateCommand.ExecuteAsync(tableName).ConfigureAwait(true);
        }
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
            if (node.NodeType == ExplorerNodeType.Table)
            {
                return node.Name;
            }

            return node.TableName;
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
                    if (parentNode.NodeType == ExplorerNodeType.Table)
                    {
                        return parentNode.Name;
                    }

                    return parentNode.TableName;
                }
            }

            visualParent = visualParent.GetVisualParent();
        }

        return null;
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

    private static string BuildColumnsSqlFromDesigner(IEnumerable<TableDesignerColumnRow> rows)
    {
        var parts = new List<string>();

        foreach (var row in rows)
        {
            var columnName = row.Name?.Trim() ?? string.Empty;
            if (!IsValidSqlIdentifier(columnName))
            {
                continue;
            }

            var columnType = string.IsNullOrWhiteSpace(row.Type) ? "TEXT" : row.Type.Trim().ToUpperInvariant();

            var builder = new StringBuilder();
            builder.Append(columnName).Append(' ').Append(columnType);

            if (!row.IsNullable)
            {
                builder.Append(" NOT NULL");
            }

            if (row.IsPrimaryKey)
            {
                builder.Append(" PRIMARY KEY");
                if (columnType.Contains("INT", StringComparison.Ordinal))
                {
                    builder.Append(" AUTOINCREMENT");
                }
            }

            parts.Add(builder.ToString());
        }

        return string.Join(", ", parts);
    }

    private static bool IsValidSqlIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return false;
        }

        return System.Text.RegularExpressions.Regex.IsMatch(
            identifier,
            "^[A-Za-z_][A-Za-z0-9_]*$",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
    }

    private void OnOpenSqlSyntaxReferenceClicked(object? sender, RoutedEventArgs e)
    {
        OpenDocumentationFile(Path.Combine("docs", "viewer", "SQL_SYNTAX_REFERENCE.md"), "SQL syntax reference");
    }

    private void OnOpenUserManualClicked(object? sender, RoutedEventArgs e)
    {
        OpenDocumentationFile(Path.Combine("docs", "USER_MANUAL.md"), "User manual");
    }

    private void OpenDocumentationFile(string relativeDocPath, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeDocPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        var fullDocPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativeDocPath));

        if (!File.Exists(fullDocPath))
        {
            if (DataContext is MainWindowViewModel missingViewModel)
            {
                missingViewModel.StatusMessage = $"{displayName} not found: {fullDocPath}";
            }

            return;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fullDocPath,
                UseShellExecute = true
            };

            Process.Start(psi);

            if (DataContext is MainWindowViewModel successViewModel)
            {
                successViewModel.StatusMessage = $"Opened {displayName}.";
            }
        }
        catch (Exception ex)
        {
            if (DataContext is MainWindowViewModel errorViewModel)
            {
                errorViewModel.StatusMessage = $"Failed to open {displayName}: {ex.Message}";
            }
        }
    }

    private static async Task LoadDesignerRowsForTableAsync(
        SharpCoreDBConnection connection,
        string tableName,
        ObservableCollection<TableDesignerColumnRow> rows)
    {
        var escapedTable = tableName.Replace("'", "''", StringComparison.Ordinal);
        using var command = new SharpCoreDBCommand($"PRAGMA table_info('{escapedTable}')", connection);
        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(true);

        while (await reader.ReadAsync().ConfigureAwait(true))
        {
            var name = reader.GetValue(1)?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var type = reader.GetValue(2)?.ToString() ?? "TEXT";
            var notNull = string.Equals(reader.GetValue(3)?.ToString(), "1", StringComparison.Ordinal);
            var isPk = string.Equals(reader.GetValue(5)?.ToString(), "1", StringComparison.Ordinal);

            rows.Add(new TableDesignerColumnRow
            {
                Name = name,
                Type = NormalizeDesignerType(type),
                IsNullable = !notNull,
                IsPrimaryKey = isPk
            });
        }
    }

    private static string NormalizeDesignerType(string type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return "TEXT";
        }

        var upper = type.ToUpperInvariant();
        if (upper.StartsWith("VECTOR", StringComparison.Ordinal))
        {
            return "VECTOR";
        }

        return upper;
    }

    private enum ColumnEditorKind
    {
        Text = 0,
        Boolean = 1,
        Number = 2,
        DateTime = 3
    }

    private static ColumnEditorKind InferColumnEditorKind(MainWindowViewModel? viewModel, int index)
    {
        if (viewModel is null)
        {
            return ColumnEditorKind.Text;
        }

        foreach (var row in viewModel.QueryResults)
        {
            if (row.Values is null || index < 0 || index >= row.Values.Length)
            {
                continue;
            }

            var value = row.Values[index];
            if (value is null || value == DBNull.Value)
            {
                continue;
            }

            if (value is bool)
            {
                return ColumnEditorKind.Boolean;
            }

            if (value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
            {
                return ColumnEditorKind.Number;
            }

            if (value is DateTime or DateTimeOffset)
            {
                return ColumnEditorKind.DateTime;
            }

            if (value is string textValue)
            {
                if (decimal.TryParse(textValue, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                {
                    return ColumnEditorKind.Number;
                }

                if (DateTime.TryParse(textValue, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out _))
                {
                    return ColumnEditorKind.DateTime;
                }
            }

            break;
        }

        return ColumnEditorKind.Text;
    }
}
