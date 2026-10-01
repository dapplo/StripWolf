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

using StripWolf.Core.Models;
using StripWolf.Core.Services;
using Xunit;

namespace StripWolf.Core.Tests;

/// <summary>
/// The tests build StripWolf.Core without PLAY_STORE_BUILD: the trial limits only apply to the Play Store build,
/// every other build is unlimited.
/// </summary>
public sealed class TrialServiceTests
{
    [Fact]
    public async Task NonPlayStoreBuild_IsUnlimited()
    {
        await using var host = await TestServiceHost.CreateAsync(settings => settings.IsUnlimitedUnlocked = false);
        // More local comics and Komga books than the trial allows
        for (var index = 0; index < TrialService.MaxTrialLimit + 2; index++)
        {
            await host.Database.SaveComicAsync(new Comic { Title = $"Local {index}", FilePath = $"/comics/local{index}.cbz", Source = ComicSource.Local });
            await host.Database.SaveComicAsync(new Comic { Title = $"Komga {index}", FilePath = $"/comics/komga{index}.cbz", Source = ComicSource.Komga, KomgaId = $"0BOOK{index}" });
        }

        Assert.True(host.Trial.IsUnlimitedUnlocked);
        Assert.True(await host.Trial.CanImportLocalAsync("/comics/another.cbz"));
        Assert.True(await host.Trial.CanDownloadKomgaAsync());
        Assert.True(await host.Trial.CanOpenLocalAsync("/comics/another.cbz"));
        Assert.True(await host.Trial.CanOpenKomgaAsync(TrialService.GetKomgaViewKey(1, "0F99E2NAQ5A4R")));
    }

    [Theory]
    [InlineData(1, "0F99E2NAQ5A4R", "1:0F99E2NAQ5A4R")]
    [InlineData(12, "0BOOK1", "12:0BOOK1")]
    [InlineData(null, "0BOOK1", "0:0BOOK1")]
    public void GetKomgaViewKey_QualifiesTheBookIdWithTheServer(int? serverId, string bookId, string expected)
    {
        Assert.Equal(expected, TrialService.GetKomgaViewKey(serverId, bookId));
    }

    [Fact]
    public void GetKomgaViewKey_SameBookIdOnTwoServers_AreDifferentKeys()
    {
        Assert.NotEqual(TrialService.GetKomgaViewKey(1, "0BOOK1"), TrialService.GetKomgaViewKey(2, "0BOOK1"));
    }

    /// <summary>
    /// Komga book ids are alphanumeric strings: the old int list (PermanentViewedKomgaBookIds) could never be filled.
    /// The list got a new name, an old settings.json with the int array must still load.
    /// </summary>
    [Fact]
    public async Task Settings_WithTheOldKomgaViewList_StillLoad()
    {
        using var temp = new TempDirectory("trial");
        await File.WriteAllTextAsync(temp.Combine("settings.json"),
            """{ "permanentViewedKomgaBookIds": [1, 2, 3], "permanentViewedLocalPaths": ["a.cbz"], "lastTabIndex": 2 }""",
            TestContext.Current.CancellationToken);

        var settings = new SettingsService(temp.DirectoryPath).LoadSettings();

        Assert.Empty(settings.PermanentViewedKomgaBooks);
        Assert.Equal(new[] { "a.cbz" }, settings.PermanentViewedLocalPaths);
        Assert.Equal(2, settings.LastTabIndex);
    }

    [Fact]
    public async Task PermanentViewedKomgaBooks_RoundTrip()
    {
        using var temp = new TempDirectory("trial");
        var service = new SettingsService(temp.DirectoryPath);
        var key = TrialService.GetKomgaViewKey(3, "0F99E2NAQ5A4R");

        await service.UpdateSettingsAsync(settings => settings.PermanentViewedKomgaBooks.Add(key));

        Assert.Equal(new[] { key }, new SettingsService(temp.DirectoryPath).LoadSettings().PermanentViewedKomgaBooks);
    }

    [Fact]
    public async Task Events_AreCountedInTheUsageStatistics()
    {
        await using var host = await TestServiceHost.CreateAsync();

        host.AppEvents.RaiseLocalComicImported("/comics/a.cbz");
        host.AppEvents.RaiseKomgaBookDownloaded("0BOOK1");
        host.AppEvents.RaiseComicOpened(1, ComicSource.Komga, TrialService.GetKomgaViewKey(1, "0BOOK1"));
        host.AppEvents.RaiseComicOpened(2, ComicSource.Local, "/comics/a.cbz");

        // The handlers are async void
        await WaitForUsageAsync(host, "LocalImport", 1);
        await WaitForUsageAsync(host, "KomgaDownload", 1);
        await WaitForUsageAsync(host, "ComicOpen", 2);

        // Unlimited (non Play Store) build: no trial bookkeeping
        var settings = host.Settings.LoadSettings();
        Assert.Empty(settings.PermanentViewedKomgaBooks);
        Assert.Empty(settings.PermanentViewedLocalPaths);
    }

    /// <summary>
    /// Page turns are counted in memory and written in one go (after 5 seconds) instead of one database write per page
    /// </summary>
    [Fact]
    public async Task PageRead_IsBatched()
    {
        await using var host = await TestServiceHost.CreateAsync();

        for (var index = 0; index < 7; index++)
        {
            host.AppEvents.RaisePageRead();
        }

        await Task.Delay(500, TestContext.Current.CancellationToken);
        Assert.Equal(0, await host.Database.GetUsageCountAsync("PagesRead"));

        await WaitForUsageAsync(host, "PagesRead", 7, TimeSpan.FromSeconds(20));

        // The next page starts a new batch
        host.AppEvents.RaisePageRead();
        await WaitForUsageAsync(host, "PagesRead", 8, TimeSpan.FromSeconds(20));
    }

    private static async Task WaitForUsageAsync(TestServiceHost host, string metric, int expected, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        int actual;
        while ((actual = await host.Database.GetUsageCountAsync(metric)) != expected && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task RequestPremiumUnlock_RaisesTheEvent()
    {
        await using var host = await TestServiceHost.CreateAsync();
        var raised = 0;
        host.Trial.PremiumUnlockRequested += (_, _) => raised++;

        host.Trial.RequestPremiumUnlock();

        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task UnlockPremiumAsync_StoresTheUnlockInTheSettings()
    {
        await using var host = await TestServiceHost.CreateAsync(settings => settings.IsUnlimitedUnlocked = false);

        await host.Trial.UnlockPremiumAsync();

        Assert.True(host.Settings.LoadSettings().IsUnlimitedUnlocked);
    }

    [Theory]
    [InlineData("cbz", true)]
    [InlineData("CBR", true)]
    [InlineData("cb7", true)]
    [InlineData("cbt", true)]
    [InlineData("epub", true)]
    [InlineData("pdf", true)]
    [InlineData("zip", false)]
    public void AllowedFormats_AreTheComicFormats(string extension, bool expected)
    {
        Assert.Equal(expected, TrialService.AllowedFormats.Contains(extension));
    }
}
