using Avalonia.Controls;
using Avalonia.Interactivity;
using SharpCoreDB.Viewer.ViewModels;
using SharpCoreDB.Data.Provider;

namespace SharpCoreDB.Viewer.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
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