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

using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Avalonia.Android;
using Microsoft.Extensions.DependencyInjection;
using StripWolf.Core.ViewModels;

namespace StripWolf.Core.Android;

[Activity(
    Label = "StripWolf",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTask,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataSchemes = new[] { "file", "content" },
    DataHost = "*",
    DataMimeType = "application/pdf")]
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataSchemes = new[] { "file", "content" },
    DataHost = "*",
    DataMimeType = "application/epub+zip")]
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataSchemes = new[] { "file", "content" },
    DataHost = "*",
    DataMimeType = "application/x-cbz")]
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataSchemes = new[] { "file", "content" },
    DataHost = "*",
    DataMimeType = "application/x-cbr")]
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataSchemes = new[] { "file", "content" },
    DataHost = "*",
    DataMimeType = "application/octet-stream",
    DataPathPatterns = new[] { ".*\\.cbz", ".*\\.cbr", ".*\\.cb7", ".*\\.cbt", ".*\\.pdf", ".*\\.epub" })]
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataSchemes = new[] { "file", "content" },
    DataHost = "*",
    DataMimeType = "*/*",
    DataPathPatterns = new[] { ".*\\.cbz", ".*\\.cbr", ".*\\.cb7", ".*\\.cbt", ".*\\.pdf", ".*\\.epub" })]
public class MainActivity : AvaloniaMainActivity
{
    /// <summary>
    /// The currently active <see cref="MainActivity"/> instance.
    /// Used by platform services (e.g. <see cref="Services.AndroidFullScreenService"/>) that need
    /// access to the Android Window.
    /// </summary>
    public static MainActivity? Current { get; private set; }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        Current = this;

        // Handle splash screen transition
        AndroidX.Core.SplashScreen.SplashScreen.InstallSplashScreen(this);
        
        base.OnCreate(savedInstanceState);
    }

    protected override void OnDestroy()
    {
        if (Current == this)
        {
            Current = null;
        }
        base.OnDestroy();
    }
}

