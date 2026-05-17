// StripWolf - an open source comic book reader
// Copyright (C) 2026 Dapplo - Robin Krom
//
// For more information see: https://github.com/dapplo/StripWolf
// The StripWolf project is hosted on GitHub https://github.com/dapplo/StripWolf
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
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

        EpubToCbzConverterService.CleanupTemporaryDirectories();
        
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
        // Use the custom registration action if set (e.g., for Android), otherwise default to nothing
        if (RegisterPdfRenderer != null)
        {
            RegisterPdfRenderer(services);
        }

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
        services.AddSingleton<EpubShadowConversionService>();
        services.AddSingleton<LibraryService>();
        services.AddSingleton<ImportQueueService>();

        // Register view models
        services.AddSingleton<LibraryViewModel>();
        services.AddSingleton<ReaderViewModel>();
        services.AddSingleton<KomgaViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<ActivityViewModel>();
        services.AddSingleton<MainViewModel>();
    }
}

