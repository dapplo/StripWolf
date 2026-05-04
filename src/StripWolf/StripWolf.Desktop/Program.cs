using System;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using StripWolf.Desktop.Services;
using StripWolf.Services;

namespace StripWolf.Desktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        App.RegisterWebViewSnapshotService = services =>
        {
            if (OperatingSystem.IsWindows())
            {
                services.AddSingleton<IWebViewPaginationService, WindowsWebView2SnapshotService>();
            }
            else
            {
                services.AddSingleton<IWebViewPaginationService, UnsupportedWebViewSnapshotService>();
            }
        };

        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}
