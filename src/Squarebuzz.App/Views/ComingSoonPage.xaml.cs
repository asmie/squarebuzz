using Squarebuzz.App.ViewModels;

namespace Squarebuzz.App.Views;

public partial class ComingSoonPage : ContentPage
{
    public ComingSoonPage(ComingSoonViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
