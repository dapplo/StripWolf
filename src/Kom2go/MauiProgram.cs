using Kom2go.Data;
using Kom2go.Services;
using Kom2go.ViewModels;
using Kom2go.Views;
using Microsoft.Extensions.Logging;

namespace Kom2go;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Register services
        builder.Services.AddSingleton<DatabaseService>();
        builder.Services.AddSingleton<ComicReaderService>();
        builder.Services.AddSingleton<KomgaApiService>();
        builder.Services.AddSingleton<LibraryService>();

        // Register view models
        builder.Services.AddTransient<LibraryViewModel>();
        builder.Services.AddTransient<ReaderViewModel>();
        builder.Services.AddTransient<KomgaViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();

        // Register views
        builder.Services.AddTransient<LibraryPage>();
        builder.Services.AddTransient<ReaderPage>();
        builder.Services.AddTransient<KomgaPage>();
        builder.Services.AddTransient<SettingsPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
