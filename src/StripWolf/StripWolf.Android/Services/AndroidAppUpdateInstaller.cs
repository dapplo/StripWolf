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

#if !PLAY_STORE_BUILD
// The Play Store doesn't allow apps to install APKs, only the build from the GitHub releases updates itself
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Android.App;
using Android.Content;
using StripWolf.Core.Models;
using StripWolf.Core.Services;

[assembly: UsesPermission("android.permission.REQUEST_INSTALL_PACKAGES")]

namespace StripWolf.Core.Android.Services;

/// <summary>
/// Installs an update APK, downloaded from the GitHub releases, with the Android package installer.
/// Android asks the user to confirm the update, and the first time to allow StripWolf to install apps.
/// The update only installs over the current app when both are signed with the same key.
/// </summary>
public class AndroidAppUpdateInstaller : IAppUpdateInstaller
{
    private const string ApkMimeType = "application/vnd.android.package-archive";

    public bool IsSupported => true;

    /// <summary>
    /// Shared through <see cref="UpdateFileProvider"/>, see Resources/xml/update_file_paths.xml
    /// </summary>
    public string DownloadDirectory => Path.Combine(Application.Context.CacheDir!.AbsolutePath, "updates");

    public GitHubReleaseAsset? SelectAsset(IReadOnlyList<GitHubReleaseAsset> assets) =>
        UpdateService.SelectAndroidApkAsset(assets, RuntimeInformation.ProcessArchitecture);

    public void LaunchInstaller(string filePath)
    {
        var context = (Context?)MainActivity.Current ?? Application.Context;
        var authority = context.PackageName + UpdateFileProvider.AuthoritySuffix;
        var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(context, authority, new Java.IO.File(filePath));

        var intent = new Intent(Intent.ActionView);
        intent.SetDataAndType(uri, ApkMimeType);
        intent.AddFlags(ActivityFlags.GrantReadUriPermission);
        if (context is not Activity)
        {
            intent.AddFlags(ActivityFlags.NewTask);
        }
        context.StartActivity(intent);
    }
}

/// <summary>
/// Gives the package installer read access to the downloaded update.
/// An own subclass, so it can't clash with a FileProvider from a library.
/// </summary>
[ContentProvider(new[] { "com.dapplo.StripWolf" + AuthoritySuffix }, Exported = false, GrantUriPermissions = true)]
[MetaData("android.support.FILE_PROVIDER_PATHS", Resource = "@xml/update_file_paths")]
public class UpdateFileProvider : AndroidX.Core.Content.FileProvider
{
    public const string AuthoritySuffix = ".updates";
}
#endif
