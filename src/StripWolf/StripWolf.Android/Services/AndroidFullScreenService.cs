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

using Android.OS;
using Android.Views;
using StripWolf.Core.Services;

namespace StripWolf.Core.Android.Services;

/// <summary>
/// Android implementation of <see cref="IFullScreenService"/>.
/// On API 30+ uses <see cref="IWindowInsetsController"/> to hide/show the
/// status bar and navigation bar.  On older APIs falls back to the deprecated
/// <see cref="View.SystemUiVisibility"/> immersive-sticky flags.
/// </summary>
public class AndroidFullScreenService : IFullScreenService
{
    public void SetFullScreen(bool fullScreen)
    {
        var activity = MainActivity.Current;
        if (activity?.Window is not { } window) return;

        activity.RunOnUiThread(() =>
        {
            if (System.OperatingSystem.IsAndroidVersionAtLeast(30))
            {
                SetFullScreenApi30(window, fullScreen);
            }
            else
            {
                SetFullScreenLegacy(window, fullScreen);
            }
        });
    }

    [System.Runtime.Versioning.SupportedOSPlatform("android30.0")]
    private static void SetFullScreenApi30(global::Android.Views.Window window, bool fullScreen)
    {
        var controller = window.InsetsController;
        if (controller is null) return;

        if (fullScreen)
        {
            controller.Hide(WindowInsets.Type.StatusBars() | WindowInsets.Type.NavigationBars());
            controller.SystemBarsBehavior = (int)WindowInsetsControllerBehavior.ShowTransientBarsBySwipe;
        }
        else
        {
            controller.Show(WindowInsets.Type.StatusBars() | WindowInsets.Type.NavigationBars());
        }
    }

#pragma warning disable CA1416, CS0618 // Validate platform compatibility – guarded by version check above
    private static void SetFullScreenLegacy(global::Android.Views.Window window, bool fullScreen)
    {
        var decorView = window.DecorView;
        if (decorView is null) return;

        decorView.SystemUiVisibility = fullScreen
            ? (StatusBarVisibility)(
                (int)SystemUiFlags.Fullscreen |
                (int)SystemUiFlags.HideNavigation |
                (int)SystemUiFlags.ImmersiveSticky |
                (int)SystemUiFlags.LayoutStable |
                (int)SystemUiFlags.LayoutFullscreen |
                (int)SystemUiFlags.LayoutHideNavigation)
            : StatusBarVisibility.Visible;
    }
#pragma warning restore CA1416, CS0618
}
