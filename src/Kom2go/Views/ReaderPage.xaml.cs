using Kom2go.ViewModels;

namespace Kom2go.Views;

public partial class ReaderPage : ContentPage
{
    public ReaderPage(ReaderViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override bool OnBackButtonPressed()
    {
        if (BindingContext is ReaderViewModel vm)
        {
            vm.GoBackCommand.Execute(null);
            return true;
        }
        return base.OnBackButtonPressed();
    }
}
