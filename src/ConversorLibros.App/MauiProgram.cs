using ConversorLibros.App.Services;
using ConversorLibros.App.ViewModels;
using ConversorLibros.App.Views;

namespace ConversorLibros.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        builder.Services.AddSingleton<AppSettings>();
        builder.Services.AddSingleton<ConversionService>();
        builder.Services.AddSingleton<IFolderPicker, WindowsFolderPicker>();
        builder.Services.AddSingleton<AppShell>();

        builder.Services.AddTransient<LibraryViewModel>();
        builder.Services.AddTransient<NewConversionViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();

        builder.Services.AddTransient<LibraryPage>();
        builder.Services.AddTransient<NewConversionPage>();
        builder.Services.AddTransient<SettingsPage>();

        return builder.Build();
    }
}
