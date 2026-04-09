using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using System.Globalization;
using Avalonia.Markup.Xaml;
using Kom2go.Data;
using Kom2go.Services;
using Kom2go.ViewModels;
using Kom2go.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Kom2go;

public partial class App : Application
{
    public static IServiceProvider? Services { get; private set; }
    
    /// <summary>
    /// Action to register the platform-specific PDF renderer.
    /// Set this before Initialize() is called if you need a custom renderer (e.g., on Android).
    /// </summary>
    public static Action<IServiceCollection>? RegisterPdfRenderer { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Set up dependency injection
        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();
        
        // Apply saved language settings before creating any UI
        ApplyLanguageSettings();

        var mainViewModel = Services.GetRequiredService<MainViewModel>();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = mainViewModel
            };
            
            // Handle shutdown to delete pending comics
            desktop.ShutdownRequested += async (sender, args) =>
            {
                await mainViewModel.OnShutdownAsync();
            };
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime activityLifetime)
        {
            activityLifetime.MainViewFactory = () => new MainView
            {
                DataContext = mainViewModel
            };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainView
            {
                DataContext = mainViewModel
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
    
    /// <summary>
    /// Apply saved language settings before UI creation
    /// </summary>
    private void ApplyLanguageSettings()
    {
        try
        {
            var settingsService = Services!.GetRequiredService<SettingsService>();
            var settings = settingsService.LoadSettings();
            
            if (!settings.UseSystemLanguage && !string.IsNullOrEmpty(settings.LanguageCode))
            {
                var culture = new CultureInfo(settings.LanguageCode);
                CultureInfo.DefaultThreadCurrentUICulture = culture;
                CultureInfo.DefaultThreadCurrentCulture = culture;
                Thread.CurrentThread.CurrentUICulture = culture;
                Thread.CurrentThread.CurrentCulture = culture;
            }
        }
        catch
        {
            // If settings fail to load, use system default
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Register services
        services.AddSingleton<DatabaseService>();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<LocalizationService>();
        services.AddSingleton<ComicReaderService>();
        services.AddSingleton<PanelDetectionService>();
        services.AddSingleton<KomgaApiService>();
        services.AddSingleton<ComicConverterService>();

        // Register platform-specific PDF renderer
        // Use the custom registration action if set (e.g., for Android), otherwise default to PDFium
        if (RegisterPdfRenderer != null)
        {
            RegisterPdfRenderer(services);
        }
    #if !EXCLUDE_PDFIUM
        else
        {
            services.AddSingleton<IPdfRenderer, PdfiumPdfRenderer>();
        }
    #endif

        services.AddSingleton<PdfToCbzConverterService>();
        services.AddSingleton<LibraryService>();

        // Register view models
        services.AddTransient<LibraryViewModel>();
        services.AddTransient<ReaderViewModel>();
        services.AddTransient<KomgaViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddSingleton<MainViewModel>();
    }
    }