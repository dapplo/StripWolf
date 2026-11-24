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

using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using StripWolf.Core.Data;
using StripWolf.Core.Models;
using StripWolf.Core.Services;
using Xunit;

namespace StripWolf.Core.Tests;

public sealed class UpdateServiceTests
{
    private static readonly GitHubReleaseAsset[] ReleaseAssets =
    [
        new("StripWolf-Android-v1.0.79-gc97e8f0.aab", "https://example.invalid/aab", 1),
        new("StripWolf-Android-arm64-v1.0.79-gc97e8f0.apk", "https://example.invalid/arm64", 1),
        new("StripWolf-Android-x86_64-v1.0.79-gc97e8f0.apk", "https://example.invalid/x64", 1),
        new("StripWolf-Windows-x64-v1.0.79-gc97e8f0.zip", "https://example.invalid/zip", 1)
    ];

    [Theory]
    [InlineData(Architecture.Arm64, "StripWolf-Android-arm64-v1.0.79-gc97e8f0.apk")]
    [InlineData(Architecture.X64, "StripWolf-Android-x86_64-v1.0.79-gc97e8f0.apk")]
    [InlineData(Architecture.Arm, null)]
    [InlineData(Architecture.X86, null)]
    public void SelectAndroidApkAsset_PicksTheApkOfTheArchitecture(Architecture architecture, string? expected)
    {
        var asset = UpdateService.SelectAndroidApkAsset(ReleaseAssets, architecture);

        Assert.Equal(expected, asset?.Name);
    }

    [Fact]
    public void GitHubRelease_ReadsTheAssets()
    {
        const string json = """
            {
              "tag_name": "v1.0.79",
              "html_url": "https://github.com/dapplo/StripWolf/releases/tag/v1.0.79",
              "assets": [
                {
                  "name": "StripWolf-Android-arm64-v1.0.79-gc97e8f0.apk",
                  "browser_download_url": "https://github.com/dapplo/StripWolf/releases/download/v1.0.79/StripWolf-Android-arm64-v1.0.79-gc97e8f0.apk",
                  "size": 34402757,
                  "digest": "sha256:1f07736dffaf06ecdd61cdc9b3b48facc4af745b3cb96443f55ede66fc410efa"
                }
              ]
            }
            """;

        var release = JsonSerializer.Deserialize(json, StripWolfJsonContext.Default.GitHubRelease);

        var asset = Assert.Single(release!.Assets!);
        Assert.Equal("StripWolf-Android-arm64-v1.0.79-gc97e8f0.apk", asset.Name);
        Assert.EndsWith("/v1.0.79/StripWolf-Android-arm64-v1.0.79-gc97e8f0.apk", asset.DownloadUrl);
        Assert.Equal(34402757L, asset.Size);
        Assert.StartsWith("sha256:", asset.Digest);
    }

    [Fact]
    public void GitHubRelease_WithoutAssets_IsStillRead()
    {
        var release = JsonSerializer.Deserialize("""{ "tag_name": "v1.0.1", "html_url": "x" }""", StripWolfJsonContext.Default.GitHubRelease);

        Assert.Equal("v1.0.1", release!.TagName);
        Assert.Null(release.Assets);
    }

    [Fact]
    public async Task DownloadAssetAsync_StoresTheFileAndReportsProgress()
    {
        using var temp = new TempDirectory("update");
        var content = new byte[200_000];
        Random.Shared.NextBytes(content);
        var directory = temp.Combine("updates");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "old.apk"), "left over");
        var asset = new GitHubReleaseAsset("StripWolf-Android-arm64-v2.0.0-gabc.apk", "https://example.invalid/apk", content.Length,
            "sha256:" + Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant());
        var progress = new List<int>();

        using var httpClient = new HttpClient(new StaticContentHandler(content));
        var filePath = await UpdateService.DownloadAssetAsync(asset, directory, progress.Add, CancellationToken.None, httpClient);

        Assert.Equal(Path.Combine(directory, asset.Name), filePath);
        Assert.Equal(content, await File.ReadAllBytesAsync(filePath));
        Assert.False(File.Exists(Path.Combine(directory, "old.apk")));
        Assert.Equal(100, progress[^1]);
    }

    [Fact]
    public void RemoveInstalledUpdates_KeepsOnlyNewerDownloads()
    {
        using var temp = new TempDirectory("update");
        var directory = temp.CreateDirectory("updates");
        string[] names =
        [
            "StripWolf-Android-arm64-1.2.3-gabc.apk",   // same as running: installed
            "StripWolf-Android-arm64-1.1.0-gdef.apk",   // older
            "StripWolf-Android-x86_64-1.3.0-g123.apk",  // newer: keep
            "old.apk"                                   // no version: left over
        ];
        foreach (var name in names)
        {
            File.WriteAllText(Path.Combine(directory, name), "x");
        }

        UpdateService.RemoveInstalledUpdates(directory, "1.2.3");

        Assert.Equal(new[] { "StripWolf-Android-x86_64-1.3.0-g123.apk" }, Directory.GetFiles(directory).Select(Path.GetFileName).ToArray());
    }

    [Fact]
    public void RemoveInstalledUpdates_RemovesTheEmptyDirectory()
    {
        using var temp = new TempDirectory("update");
        var directory = temp.CreateDirectory("updates");
        File.WriteAllText(Path.Combine(directory, "StripWolf-Android-arm64-v1.0.0-gabc.apk"), "x");

        UpdateService.RemoveInstalledUpdates(directory, "1.0.0");
        UpdateService.RemoveInstalledUpdates(directory, "1.0.0");
        UpdateService.RemoveInstalledUpdates(null, "1.0.0");

        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task DownloadAssetAsync_WrongChecksum_Throws()
    {
        using var temp = new TempDirectory("update");
        var content = new byte[1000];
        var asset = new GitHubReleaseAsset("StripWolf.apk", "https://example.invalid/apk", content.Length,
            "sha256:" + new string('0', 64));

        using var httpClient = new HttpClient(new StaticContentHandler(content));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            UpdateService.DownloadAssetAsync(asset, temp.DirectoryPath, null, CancellationToken.None, httpClient));
        Assert.Empty(Directory.GetFiles(temp.DirectoryPath));
    }

    [Fact]
    public async Task DownloadAssetAsync_WrongSize_Throws()
    {
        using var temp = new TempDirectory("update");
        var asset = new GitHubReleaseAsset("StripWolf.apk", "https://example.invalid/apk", 2000);

        using var httpClient = new HttpClient(new StaticContentHandler(new byte[1000]));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            UpdateService.DownloadAssetAsync(asset, temp.DirectoryPath, null, CancellationToken.None, httpClient));
    }

    [Fact]
    public async Task DownloadAssetAsync_NameWithPath_StaysInTheDirectory()
    {
        using var temp = new TempDirectory("update");
        var directory = temp.Combine("updates");
        var asset = new GitHubReleaseAsset("../../evil.apk", "https://example.invalid/apk", 10);

        using var httpClient = new HttpClient(new StaticContentHandler(new byte[10]));
        var filePath = await UpdateService.DownloadAssetAsync(asset, directory, null, CancellationToken.None, httpClient);

        Assert.Equal(Path.Combine(directory, "evil.apk"), filePath);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("md5:abc")]
    public async Task MatchesDigestAsync_UnknownDigest_Passes(string? digest)
    {
        using var temp = new TempDirectory("update");
        var filePath = temp.Combine("file.bin");
        await File.WriteAllBytesAsync(filePath, [1, 2, 3]);

        Assert.True(await UpdateService.MatchesDigestAsync(filePath, digest));
    }

    private sealed class StaticContentHandler(byte[] content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) });
        }
    }
}
