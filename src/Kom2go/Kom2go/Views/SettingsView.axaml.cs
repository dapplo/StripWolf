using Avalonia.Controls;
using Kom2go.ViewModels;

namespace Kom2go.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    protected override async void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        
        // Load settings when the view is displayed
        if (DataContext is SettingsViewModel viewModel)
        {
            try
            {
                await viewModel.LoadServersCommand.ExecuteAsync(null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load servers: {ex.Message}");
            }
        }
    }
}
