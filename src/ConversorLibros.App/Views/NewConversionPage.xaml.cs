using ConversorLibros.App.ViewModels;

namespace ConversorLibros.App.Views;

public partial class NewConversionPage : ContentPage
{
    public NewConversionPage(NewConversionViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
