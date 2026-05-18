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

using System;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
#if Windows && !DISABLE_EPUB_WEBVIEW
using StripWolf.Desktop.Services.Windows;
#endif
#if Linux && !DISABLE_EPUB_WEBVIEW
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
#if Windows && !DISABLE_EPUB_WEBVIEW
            if (OperatingSystem.IsWindows())
            {
                services.AddSingleton<IWebViewPaginationService, WindowsWebView2SnapshotService>();
            }
            else
#endif
#if Linux && !DISABLE_EPUB_WEBVIEW
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

