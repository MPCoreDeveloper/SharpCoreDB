using Avalonia.Controls;
using SharpCoreDB.Viewer.ViewModels;
using SharpCoreDB.Viewer.Services;

namespace SharpCoreDB.Viewer.Views;

public partial class SettingsDialog : Window
{
    public SettingsDialog()
    {
        InitializeComponent();
        DataContext = new SettingsViewModel();
    }

    public SettingsDialog(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        
        // Subscribe to save/cancel
        if (DataContext is SettingsViewModel vm)
        {
            vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SettingsViewModel.IsSaved))
                {
                    Console.WriteLine($"Settings saved: {vm.IsSaved}");
                    
                    // Close dialog
                    Close(vm.IsSaved);
                }
            };
        }
    }
}
