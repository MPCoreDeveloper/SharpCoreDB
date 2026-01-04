using Avalonia.Controls;
using SharpCoreDB.Viewer.ViewModels;

namespace SharpCoreDB.Viewer.Views;

public partial class ConnectionDialog : Window
{
    public ConnectionDialog()
    {
        InitializeComponent();
        var vm = new ConnectionDialogViewModel();
        DataContext = vm;
        
        // Inject StorageProvider into ViewModel
        Opened += (s, e) =>
        {
            if (DataContext is ConnectionDialogViewModel viewModel)
            {
                viewModel.StorageProvider = StorageProvider;
            }
        };
    }

    public ConnectionDialog(ConnectionDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        
        // Inject StorageProvider into ViewModel
        Opened += (s, e) =>
        {
            viewModel.StorageProvider = StorageProvider;
        };
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        
        // Subscribe to connection success and cancel
        if (DataContext is ConnectionDialogViewModel vm)
        {
            vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ConnectionDialogViewModel.IsConnected))
                {
                    if (vm.IsConnected)
                    {
                        // Close dialog on successful connection
                        Close(vm.Connection);
                    }
                    else if (vm.WasCancelled)
                    {
                        // Close dialog on cancel
                        Close(null);
                    }
                }
            };
        }
    }
}
