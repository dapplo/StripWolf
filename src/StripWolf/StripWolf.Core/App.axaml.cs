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
using Avalonia.Markup.Xaml;
using StripWolf.Core.Data;
using StripWolf.Core.Models;
using StripWolf.Core.Services;
using StripWolf.Core.ViewModels;
using StripWolf.Core.Views;
using Microsoft.Extensions.DependencyInjection;
using Avalonia.Platform.Storage;
using System.Threading;

namespace StripWolf;

public partial class App : Application
{
    public static IServiceProvider? Services { get; private set; }

    /// <summary>
    /// Gets the current TopLevel instance.
    /// This is used to access platform services like ILauncher.
    /// </summary>
    public static Avalonia.Controls.TopLevel? TopLevel { get; set; }
    
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

    /// <summary>
    /// Action to register platform-specific network connection inspection.
    /// </summary>
    public static Action<IServiceCollection>? RegisterNetworkConnectionService { get; set; }

    /// <summary>
    /// Action to register the platform-specific fullscreen service.
    /// Set this before Initialize() is called if you need a custom implementation (e.g., on Android).
    /// </summary>
    public static Action<IServiceCollection>? RegisterFullScreenService { get; set; }

    /// <summary>
    /// Action to register the platform-specific billing service.
    /// Set this before Initialize() is called if you need a custom implementation (e.g., on Android).
    /// </summary>
    public static Action<IServiceCollection>? RegisterBillingService { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private Task _initializationTask = Task.CompletedTask;

    public override void OnFrameworkInitializationCompleted()
    {
        // Set up dependency injection
        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        // Only the first instance cleans up (a second desktop instance already exited in Program.Main, see SingleInstance).
        // This must finish before a comic can be opened, otherwise it could delete the temporary directory of an EPUB
        // reading session.
        EpubToCbzConverterService.CleanupTemporaryDirectories();
        
        // Apply saved language settings before creating any UI
        ApplyLanguageSettings();
        ApplyThemeSettings();

        var mainViewModel = Services.GetRequiredService<MainViewModel>();

        if (SingleInstance.IsPrimary)
        {
            var activationManager = Services.GetRequiredService<ActivationManager>();
            activationManager.PathReceived += (path) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
                {
                    BringMainWindowToFront();
                    try
                    {
                        await _initializationTask;
                        await mainViewModel.OpenFileAsync(path);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"App: Failed to open forwarded '{path}': {ex.Message}");
                    }
                });
            };
            activationManager.ShowRequested += () => Avalonia.Threading.Dispatcher.UIThread.Post(BringMainWindowToFront);
            activationManager.StartServer();
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = mainViewModel
            };
            App.TopLevel = desktop.MainWindow;
            
            // Handle shutdown to delete pending comics.
            // The handler is async void: without cancelling the shutdown the process ended at the first await,
            // so pending deletes were cut off (and the deleted comics came back on the next start).
            var shutdownCleanupDone = false;
            desktop.ShutdownRequested += async (sender, args) =>
            {
                if (shutdownCleanupDone)
                {
                    return;
                }

                args.Cancel = true;
                try
                {
                    var cleanupTask = mainViewModel.OnShutdownAsync();
                    // Never hang on exit
                    await Task.WhenAny(cleanupTask, Task.Delay(TimeSpan.FromSeconds(5)));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"App: Shutdown cleanup failed: {ex.Message}");
                }
                finally
                {
                    if (SingleInstance.IsPrimary)
                    {
                        Services?.GetService<ActivationManager>()?.StopServer();
                        SingleInstance.Release();
                    }

                    shutdownCleanupDone = true;
                    desktop.Shutdown();
                }
            };

            // Handle command line arguments for opening files at startup
            if (desktop.Args is { Length: > 0 })
            {
                var filePath = desktop.Args[0];
                if (!string.IsNullOrWhiteSpace(filePath))
                {
                    // Run on the UI thread (OpenFileAsync changes view model state which is bound to the UI,
                    // from a Task.Run this failed with an invalid thread exception which was swallowed),
                    // after the main view model finished initializing.
                    Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
                    {
                        try
                        {
                            await _initializationTask;
                            await Task.Delay(500);
                            await mainViewModel.OpenFileAsync(filePath);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"App: Failed to open '{filePath}': {ex.Message}");
                        }
                    });
                }
            }
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

        if (ApplicationLifetime is IActivatableLifetime activatable)
        {
            activatable.Activated += async (s, e) =>
            {
                if (e.Kind == ActivationKind.Background)
                {
                    // async void handler: an exception must not crash the app when it is resumed
                    try
                    {
                        await mainViewModel.OnAppResumedAsync();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"App: Resume handling failed: {ex.Message}");
                    }
                }
                else if (e is FileActivatedEventArgs fileArgs)
                {
                    if (fileArgs.Files is { Count: > 0 })
                    {
                        var firstFile = fileArgs.Files.OfType<IStorageFile>().FirstOrDefault();
                        if (firstFile is not null)
                        {
                            // See above: must run on the UI thread, after initialization
                            Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
                            {
                                try
                                {
                                    await _initializationTask;
                                    await Task.Delay(500);
                                    await mainViewModel.OpenStorageFileAsync(firstFile);
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"App: Failed to open activated file: {ex.Message}");
                                }
                            });
                        }
                    }
                }
            };
        }

        _initializationTask = mainViewModel.InitializeAsync();

        base.OnFrameworkInitializationCompleted();
    }
    
    /// <summary>
    /// Shows the main window in front of the other windows, when a file was opened while StripWolf was already running.
    /// </summary>
    private void BringMainWindowToFront()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } mainWindow })
        {
            return;
        }

        if (mainWindow.WindowState == Avalonia.Controls.WindowState.Minimized)
        {
            mainWindow.WindowState = Avalonia.Controls.WindowState.Normal;
        }
        mainWindow.Activate();
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
            
            var localizationService = Services!.GetRequiredService<LocalizationService>();
            localizationService.SetLanguage(settings.UseSystemLanguage ? null : settings.LanguageCode);
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
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            RequestedThemeVariant = theme switch
            {
                AppThemePreference.Light => ThemeVariant.Light,
                AppThemePreference.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default
            };
        });
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Register services
        services.AddSingleton<DatabaseService>();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<ICloudLibraryService, CloudLibraryService>();
        services.AddSingleton<LocalizationService>();
        services.AddSingleton<ComicReaderService>();
        services.AddSingleton<PanelDetectionService>();
        services.AddSingleton<KomgaApiServiceFactory>();
        services.AddSingleton<ComicConverterService>();
        services.AddSingleton<IExternalLinkService, ExternalLinkService>();
        services.AddSingleton<UpdateService>();
        services.AddSingleton<IAppEventsService, AppEventsService>();
        services.AddSingleton<TrialService>();
        services.AddSingleton<ActivationManager>();

        if (RegisterBillingService != null)
        {
            RegisterBillingService(services);
        }

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
        if (RegisterNetworkConnectionService is not null)
        {
            RegisterNetworkConnectionService(services);
        }
        else
        {
            services.AddSingleton<INetworkConnectionService, DefaultNetworkConnectionService>();
        }
        if (RegisterFullScreenService is not null)
        {
            RegisterFullScreenService(services);
        }
        else
        {
            services.AddSingleton<IFullScreenService, DefaultFullScreenService>();
        }
        services.AddSingleton<PdfToCbzConverterService>();
        services.AddSingleton<EpubToCbzConverterService>();
        services.AddSingleton<EpubShadowConversionService>();
        services.AddSingleton<LibraryService>();
        services.AddSingleton<ImportQueueService>();
        services.AddSingleton<KomgaSyncService>();

        // Register view models
        services.AddSingleton<LibraryViewModel>();
        services.AddSingleton<ReaderViewModel>();
        services.AddSingleton<KomgaViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<ActivityViewModel>(sp => new ActivityViewModel(
            sp.GetRequiredService<LibraryViewModel>(),
            sp.GetRequiredService<KomgaViewModel>(),
            sp.GetRequiredService<EpubShadowConversionService>(),
            sp.GetRequiredService<SettingsService>()));
        services.AddSingleton<MainViewModel>();
    }
}
