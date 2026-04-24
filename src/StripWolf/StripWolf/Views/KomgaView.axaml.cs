using Avalonia.Controls;
using StripWolf.ViewModels;

namespace StripWolf.Views;

public partial class KomgaView : UserControl
{
    public KomgaView()
    {
        InitializeComponent();
    }

    protected override async void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        
        // Initialize when the view is displayed
        if (DataContext is KomgaViewModel viewModel)
        {
            try
            {
                await viewModel.InitializeCommand.ExecuteAsync(null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize Komga: {ex.Message}");
            }
        }
    }
}
