using Kom2go.ViewModels;

namespace Kom2go.Views;

public partial class KomgaPage : ContentPage
{
    private readonly KomgaViewModel _viewModel;

    public KomgaPage(KomgaViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.InitializeCommand.ExecuteAsync(null);
    }
}
