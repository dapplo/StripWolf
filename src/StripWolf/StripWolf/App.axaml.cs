using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Styling;
using System.Globalization;
using Avalonia.Markup.Xaml;
using StripWolf.Data;
using StripWolf.Models;
using StripWolf.Services;
using StripWolf.ViewModels;
using StripWolf.Views;
using Microsoft.Extensions.DependencyInjection;

namespace StripWolf;

public partial class App : Application
{
    public static IServiceProvider? Services { get; private set; }
    
    /// <summary>
    /// Action to register the platform-specific PDF renderer.
    /// Set this before Initialize() is called if you need a custom renderer (e.g., on Android).
    /// </summary>
    public static Action<IServiceCollection>? RegisterPdfRenderer { get; set; }

    /// <summary>
    /// Action to register the platform-specific off-screen WebView snapshot service.
    /// Set this before Initialize() is called if you need a custom renderer for EPUB pagination.
    /// </summary>
    public static Action<IServiceCollection>? RegisterWebViewSnapshotService { get; set; }

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
        ApplyThemeSettings();

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

    private void ApplyThemeSettings()
    {
        try
        {
            var settingsService = Services!.GetRequiredService<SettingsService>();
            ApplyTheme(settingsService.LoadSettings().AppTheme);
            settingsService.SettingsChanged += (_, settings) => ApplyTheme(settings.AppTheme);
        }
        catch
        {
            // If settings fail to load, use system default
        }
    }

    private void ApplyTheme(AppThemePreference theme)
    {
        RequestedThemeVariant = theme switch
        {
            AppThemePreference.Light => ThemeVariant.Light,
            AppThemePreference.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
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

        if (RegisterWebViewSnapshotService != null)
        {
            RegisterWebViewSnapshotService(services);
        }
        else
        {
            services.AddSingleton<IWebViewPaginationService, UnsupportedWebViewSnapshotService>();
        }

        services.AddSingleton<IWebViewSnapshotService>(serviceProvider =>
            serviceProvider.GetRequiredService<IWebViewPaginationService>());
        services.AddSingleton<PdfToCbzConverterService>();
        services.AddSingleton<EpubToCbzConverterService>();
        services.AddSingleton<LibraryService>();

        // Register view models
        services.AddTransient<LibraryViewModel>();
        services.AddTransient<ReaderViewModel>();
        services.AddTransient<KomgaViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<ActivityViewModel>();
        services.AddSingleton<MainViewModel>();
    }
}
