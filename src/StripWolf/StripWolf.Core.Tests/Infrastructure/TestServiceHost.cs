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

using StripWolf.Core.Data;
using StripWolf.Core.Models;
using StripWolf.Core.Services;

namespace StripWolf.Core.Tests;

/// <summary>
/// Wires the StripWolf.Core services like the app does, but with a temporary app data directory and fakes for the
/// platform parts (PDF renderer, WebView). Dispose it to close the database and remove the directory.
/// </summary>
internal sealed class TestServiceHost : IAsyncDisposable
{
    private TestServiceHost(TempDirectory temp)
    {
        Temp = temp;
        AppDataDirectory = temp.CreateDirectory("AppData");
        Settings = new SettingsService(AppDataDirectory);
        Database = new DatabaseService(Path.Combine(AppDataDirectory, "StripWolf.db"));
        PdfRenderer = new FakePdfRenderer(TestFiles.FakePdfPageBytes(), new PdfMetadata { Title = "PDF Title", Author = "Pdf Author" });
        WebView = new FakeWebViewPaginationService();
        EpubConverter = new EpubToCbzConverterService(WebView, Settings);
        Reader = new ComicReaderService(PdfRenderer, EpubConverter);
        PdfConverter = new PdfToCbzConverterService(PdfRenderer);
        ComicConverter = new ComicConverterService();
        EpubShadowConversion = new EpubShadowConversionService(Database, EpubConverter, Settings, AppDataDirectory);
        AppEvents = new AppEventsService();
        Trial = new TrialService(Settings, Database, AppEvents);
        KomgaApiServiceFactory = new KomgaApiServiceFactory();
        Library = new LibraryService(
            Database,
            Reader,
            KomgaApiServiceFactory,
            Settings,
            new DefaultNetworkConnectionService(),
            PdfConverter,
            EpubConverter,
            EpubShadowConversion,
            ComicConverter,
            Trial,
            AppEvents,
            AppDataDirectory);
    }

    public TempDirectory Temp { get; }

    public string AppDataDirectory { get; }

    public SettingsService Settings { get; }

    public DatabaseService Database { get; }

    public FakePdfRenderer PdfRenderer { get; }

    public FakeWebViewPaginationService WebView { get; }

    public EpubToCbzConverterService EpubConverter { get; }

    public ComicReaderService Reader { get; }

    public PdfToCbzConverterService PdfConverter { get; }

    public ComicConverterService ComicConverter { get; }

    public EpubShadowConversionService EpubShadowConversion { get; }

    public AppEventsService AppEvents { get; }

    public TrialService Trial { get; }

    public KomgaApiServiceFactory KomgaApiServiceFactory { get; }

    public LibraryService Library { get; }

    /// <summary>
    /// Creates the services. The EPUB conversion theme is set to Light: with "System" the converter asks the Avalonia
    /// dispatcher for the theme variant, which never answers without a running application.
    /// </summary>
    public static async Task<TestServiceHost> CreateAsync(Action<AppSettings>? configureSettings = null)
    {
        var host = new TestServiceHost(new TempDirectory("host"));
        await host.Settings.UpdateSettingsAsync(settings =>
        {
            settings.EpubConversionTheme = EpubConversionTheme.Light;
            settings.IsUnlimitedUnlocked = true;
            configureSettings?.Invoke(settings);
        });
        return host;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Reader.ClearCacheAsync();
            KomgaApiServiceFactory.Dispose();
            await Database.DisposeAsync();
        }
        finally
        {
            Temp.Dispose();
        }
    }
}
