using Avalonia.Controls;
using Avalonia.Interactivity;
using SharpCoreDB.Viewer.ViewModels;
using SharpCoreDB.Data.Provider;
using SharpCoreDB.Viewer.Services;

namespace SharpCoreDB.Viewer.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        
        // Subscribe to ViewModel events
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
        if (DataContext is MainWindowViewModel viewModel)
        {
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
        
        for (int i = 0; i < columnNames.Count; i++)
        {
            var header = columnNames[i];
            var index = i; // capture for closure
            
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
                MaxWidth = 400,
                IsReadOnly = true
            };

            // Bind via indexer: row => row[index]
            var cellTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<SharpCoreDB.Viewer.Models.QueryResultRow>((row, _) =>
            {
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

            column.CellTemplate = cellTemplate;
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

    private void OnExitClicked(object? sender, RoutedEventArgs e)
    {
        Close();
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
}