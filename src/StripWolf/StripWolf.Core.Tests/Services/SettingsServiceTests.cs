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

using System.Text;
using StripWolf.Core.Models;
using StripWolf.Core.Services;
using Xunit;

namespace StripWolf.Core.Tests;

public sealed class SettingsServiceTests : IDisposable
{
    private const string Password = "s3cret-Pa55word!";
    private const string ApiKey = "komga-api-key-0123456789";

    private readonly TempDirectory _temp = new("settings");

    private string SettingsDirectory => _temp.DirectoryPath;

    private string SettingsFile => _temp.Combine("settings.json");

    private string CredentialsFile => _temp.Combine("credentials.dat");

    public void Dispose()
    {
        _temp.Dispose();
    }

    private static KomgaServer CreateServer(int id, string? password = Password, string? apiKey = null)
    {
        return new KomgaServer
        {
            Id = id,
            Name = $"Server {id}",
            BaseUrl = $"https://komga{id}.example.com",
            Username = "reader",
            Password = password ?? string.Empty,
            ApiKey = apiKey ?? string.Empty,
            CustomHeaders = [new KomgaHeader { Name = "X-Test", Value = "1" }],
            BypassSslValidation = true
        };
    }

    private static bool Contains(byte[] haystack, string needle)
    {
        ReadOnlySpan<byte> content = haystack;
        ReadOnlySpan<byte> pattern = Encoding.UTF8.GetBytes(needle);
        return content.IndexOf(pattern) >= 0;
    }

    [Fact]
    public void LoadSettings_WithoutFile_ReturnsDefaults()
    {
        var service = new SettingsService(SettingsDirectory);

        var settings = service.LoadSettings();

        Assert.Empty(settings.Servers);
        Assert.Null(settings.ActiveServerId);
        Assert.True(settings.SyncReadProgress);
        Assert.True(settings.UseSystemLanguage);
        Assert.Equal(StartupBehavior.ContinueWhereLeftOff, settings.StartupBehavior);
        Assert.Equal(UnsupportedFormatHandlingMode.ConvertOnImport, settings.UnsupportedFormatHandlingMode);
        Assert.Equal(EpubConversionTheme.System, settings.EpubConversionTheme);
        Assert.Equal(20, settings.KomgaSeriesPageSize);
        Assert.Equal(10, settings.KomgaSearchLimit);
        Assert.Equal(0.3, settings.DefaultZoomRegionSize);
        Assert.NotEmpty(settings.LibrarySections);
        Assert.NotEmpty(settings.KomgaSections);
        Assert.False(File.Exists(SettingsFile));
    }

    [Fact]
    public void LoadSettings_ReturnsACopy()
    {
        var service = new SettingsService(SettingsDirectory);

        var first = service.LoadSettings();
        first.Servers.Add(CreateServer(1));
        first.CloudFolderBookmarks.Add("changed");

        var second = service.LoadSettings();
        Assert.Empty(second.Servers);
        Assert.Empty(second.CloudFolderBookmarks);
    }

    [Fact]
    public async Task UpdateSettingsAsync_RoundTripsThroughANewInstance()
    {
        var service = new SettingsService(SettingsDirectory);
        await service.UpdateSettingsAsync(settings =>
        {
            settings.Servers.Add(CreateServer(1));
            settings.ActiveServerId = 1;
            settings.ComicsDirectory = "/data/comics";
            settings.LanguageCode = "de";
            settings.UseSystemLanguage = false;
            settings.PreferredReadingMode = ReadingMode.Normal;
            settings.EpubConversionTheme = EpubConversionTheme.Dark;
            settings.EpubOutputResolution = EpubOutputResolution.High;
            settings.UnsupportedFormatHandlingMode = UnsupportedFormatHandlingMode.ConvertWhileReading;
            settings.DefaultZoomRegionSize = 0.55;
            settings.KomgaSeriesPageSize = 42;
            settings.SyncReadProgress = false;
            settings.CloudFolderBookmarks.Add("bookmark");
            settings.PermanentViewedLocalPaths.Add("comic.cbz");
            settings.LastUpdateCheckTime = new DateTime(2025, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        });
        var loaded = new SettingsService(SettingsDirectory).LoadSettings();

        var server = Assert.Single(loaded.Servers);
        Assert.Equal(1, server.Id);
        Assert.Equal("https://komga1.example.com", server.BaseUrl);
        Assert.Equal("reader", server.Username);
        Assert.Equal(Password, server.Password);
        Assert.True(server.BypassSslValidation);
        Assert.Equal("X-Test", Assert.Single(server.CustomHeaders).Name);
        Assert.Equal(1, loaded.ActiveServerId);
        Assert.Equal("/data/comics", loaded.ComicsDirectory);
        Assert.Equal("de", loaded.LanguageCode);
        Assert.False(loaded.UseSystemLanguage);
        Assert.Equal(EpubConversionTheme.Dark, loaded.EpubConversionTheme);
        Assert.Equal(EpubOutputResolution.High, loaded.EpubOutputResolution);
        Assert.Equal(UnsupportedFormatHandlingMode.ConvertWhileReading, loaded.UnsupportedFormatHandlingMode);
        Assert.Equal(0.55, loaded.DefaultZoomRegionSize);
        Assert.Equal(42, loaded.KomgaSeriesPageSize);
        Assert.False(loaded.SyncReadProgress);
        Assert.Equal(new[] { "bookmark" }, loaded.CloudFolderBookmarks);
        Assert.Equal(new[] { "comic.cbz" }, loaded.PermanentViewedLocalPaths);
        Assert.Equal(new DateTime(2025, 3, 4, 5, 6, 7), loaded.LastUpdateCheckTime!.Value.ToUniversalTime());
    }

    [Fact]
    public async Task Credentials_AreStoredEncrypted_AndNotInSettingsJson()
    {
        var service = new SettingsService(SettingsDirectory);
        await service.UpdateSettingsAsync(settings =>
        {
            settings.Servers.Add(CreateServer(1));
            settings.Servers.Add(CreateServer(2, password: null, apiKey: ApiKey));
        });

        var settingsJson = await File.ReadAllBytesAsync(SettingsFile, TestContext.Current.CancellationToken);
        var credentials = await File.ReadAllBytesAsync(CredentialsFile, TestContext.Current.CancellationToken);

        Assert.True(Contains(settingsJson, "https://komga1.example.com"));
        Assert.False(Contains(settingsJson, Password));
        Assert.False(Contains(settingsJson, ApiKey));
        Assert.False(Contains(credentials, Password));
        Assert.False(Contains(credentials, ApiKey));

        var loaded = new SettingsService(SettingsDirectory).LoadSettings();
        Assert.Equal(Password, loaded.Servers.Single(s => s.Id == 1).Password);
        Assert.Equal(string.Empty, loaded.Servers.Single(s => s.Id == 1).ApiKey);
        Assert.Equal(ApiKey, loaded.Servers.Single(s => s.Id == 2).ApiKey);
        Assert.Equal(string.Empty, loaded.Servers.Single(s => s.Id == 2).Password);
    }

    [Fact]
    public async Task Save_LeavesNoTemporaryFilesBehind()
    {
        var service = new SettingsService(SettingsDirectory);

        await service.UpdateSettingsAsync(settings => settings.Servers.Add(CreateServer(1)));
        await service.UpdateSettingsAsync(settings => settings.LastTabIndex = 2);

        Assert.Empty(Directory.GetFiles(SettingsDirectory, "*.tmp"));
        Assert.True(File.Exists(SettingsFile));
        Assert.True(File.Exists(CredentialsFile));
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{ this is not json")]
    [InlineData("{\"servers\": 42}")]
    [InlineData("<settings>not json</settings>")]
    public async Task CorruptSettingsJson_FallsBackToDefaults_AndCanBeSavedAgain(string content)
    {
        await File.WriteAllTextAsync(SettingsFile, content, TestContext.Current.CancellationToken);
        var service = new SettingsService(SettingsDirectory);

        var settings = service.LoadSettings();

        Assert.Empty(settings.Servers);
        Assert.True(settings.SyncReadProgress);

        await service.UpdateSettingsAsync(s => s.LastTabIndex = 3);
        Assert.Equal(3, new SettingsService(SettingsDirectory).LoadSettings().LastTabIndex);
    }

    [Fact]
    public async Task TruncatedSettingsJson_FallsBackToDefaults()
    {
        var service = new SettingsService(SettingsDirectory);
        await service.UpdateSettingsAsync(settings =>
        {
            settings.Servers.Add(CreateServer(1));
            settings.LastTabIndex = 5;
        });
        var json = await File.ReadAllTextAsync(SettingsFile, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(SettingsFile, json[..(json.Length / 2)], TestContext.Current.CancellationToken);

        var settings = new SettingsService(SettingsDirectory).LoadSettings();

        Assert.Empty(settings.Servers);
        Assert.Equal(0, settings.LastTabIndex);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(40)]
    [InlineData(-1)]
    public async Task CorruptCredentials_DoNotPreventLoadingTheSettings(int length)
    {
        var service = new SettingsService(SettingsDirectory);
        await service.UpdateSettingsAsync(settings => settings.Servers.Add(CreateServer(1)));
        var credentials = await File.ReadAllBytesAsync(CredentialsFile, TestContext.Current.CancellationToken);
        // -1: same length, but flipped bits (authentication tag mismatch)
        var corrupted = length < 0 ? credentials.Select(b => (byte)~b).ToArray() : credentials.Take(length).ToArray();
        await File.WriteAllBytesAsync(CredentialsFile, corrupted, TestContext.Current.CancellationToken);

        var settings = new SettingsService(SettingsDirectory).LoadSettings();

        var server = Assert.Single(settings.Servers);
        Assert.Equal("https://komga1.example.com", server.BaseUrl);
        Assert.Equal(string.Empty, server.Password);
    }

    [Fact]
    public async Task LostEncryptionKey_CredentialsAreDropped_SettingsRemain()
    {
        var service = new SettingsService(SettingsDirectory);
        await service.UpdateSettingsAsync(settings => settings.Servers.Add(CreateServer(1)));
        File.Delete(_temp.Combine(".key"));

        var settings = new SettingsService(SettingsDirectory).LoadSettings();

        var server = Assert.Single(settings.Servers);
        Assert.Equal("Server 1", server.Name);
        Assert.Equal(string.Empty, server.Password);
    }

    [Fact]
    public async Task UpdateSettingsAsync_IsVisibleImmediately_AndPersisted()
    {
        var service = new SettingsService(SettingsDirectory);
        AppSettings? changedSettings = null;
        service.SettingsChanged += (_, settings) => changedSettings = settings;

        var save = service.UpdateSettingsAsync(settings => settings.KomgaSmartListSize = 33);
        Assert.Equal(33, service.LoadSettings().KomgaSmartListSize);
        await save;

        Assert.NotNull(changedSettings);
        Assert.Equal(33, changedSettings.KomgaSmartListSize);
        Assert.Equal(33, new SettingsService(SettingsDirectory).LoadSettings().KomgaSmartListSize);
    }

    [Fact]
    public async Task UpdateSettingsAsync_ConcurrentCalls_DoNotLoseUpdates()
    {
        var service = new SettingsService(SettingsDirectory);

        await Task.WhenAll(Enumerable.Range(0, 50).Select(index =>
            Task.Run(() => service.UpdateSettingsAsync(settings => settings.CloudFolderBookmarks.Add($"bookmark-{index}")))));

        var settings = service.LoadSettings();
        Assert.Equal(50, settings.CloudFolderBookmarks.Distinct().Count());

        // After one more (sequential) save the file contains everything
        await service.UpdateSettingsAsync(_ => { });
        Assert.Equal(50, new SettingsService(SettingsDirectory).LoadSettings().CloudFolderBookmarks.Count);
    }

    /// <summary>
    /// UpdateSettingsAsync takes the snapshot under the settings lock, but queues it for saving after releasing the lock
    /// (SettingsService.UpdateSettingsAsync / QueueSettingsSave). Two concurrent updates can queue in the opposite order,
    /// the older snapshot is then written last and the file misses the newer change until the next save.
    /// </summary>
    [Fact]
    public async Task UpdateSettingsAsync_ConcurrentCalls_AllUpdatesArePersisted()
    {
        var service = new SettingsService(SettingsDirectory);

        await Task.WhenAll(Enumerable.Range(0, 50).Select(index =>
            Task.Run(() => service.UpdateSettingsAsync(settings => settings.CloudFolderBookmarks.Add($"bookmark-{index}")))));

        Assert.Equal(50, new SettingsService(SettingsDirectory).LoadSettings().CloudFolderBookmarks.Count);
    }

    [Fact]
    public async Task UpdateSettingsAsync_SequentialSaves_LastOneWins()
    {
        var service = new SettingsService(SettingsDirectory);
        for (var index = 1; index <= 10; index++)
        {
            var value = index;
            await service.UpdateSettingsAsync(settings => settings.LastTabIndex = value);
        }

        Assert.Equal(10, new SettingsService(SettingsDirectory).LoadSettings().LastTabIndex);
    }
}
