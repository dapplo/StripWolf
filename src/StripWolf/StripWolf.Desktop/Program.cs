using System;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
#if Windows
using StripWolf.Desktop.Services.Windows;
#endif
#if Linux
using StripWolf.Desktop.Services.Linux;
#endif
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
        App.RegisterPdfRenderer = services =>
        {
            services.AddSingleton<IPdfRenderer, PdfiumPdfRenderer>();
        };

        App.RegisterWebViewSnapshotService = services =>
        {
#if Windows
            if (OperatingSystem.IsWindows())
            {
                services.AddSingleton<IWebViewPaginationService, WindowsWebView2SnapshotService>();
            }
            else
#endif
#if Linux
            if (OperatingSystem.IsLinux())
            {
                services.AddSingleton<IWebViewPaginationService, LinuxWpeWebViewSnapshotService>();
            }
            else
#endif
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
