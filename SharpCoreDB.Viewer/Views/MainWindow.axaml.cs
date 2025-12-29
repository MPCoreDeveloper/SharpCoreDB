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
        
        // Subscribe to language changes to force UI refresh
        LocalizationService.Instance.LanguageChanged += (s, e) =>
        {
            // Force rebind by resetting DataContext
            var currentContext = DataContext;
            DataContext = null;
            DataContext = currentContext;
        };
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