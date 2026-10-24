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
using Android.Views;
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
// The registered (IANA) types of CBZ and CBR, which most file managers and download providers use
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataSchemes = new[] { "file", "content" },
    DataHost = "*",
    DataMimeType = "application/vnd.comicbook+zip")]
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataSchemes = new[] { "file", "content" },
    DataHost = "*",
    DataMimeType = "application/vnd.comicbook-rar")]
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataSchemes = new[] { "file", "content" },
    DataHost = "*",
    DataMimeType = "application/x-cb7")]
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataSchemes = new[] { "file", "content" },
    DataHost = "*",
    DataMimeType = "application/x-cbt")]
// Files offered with a generic MIME type, recognized by their extension. Android's path pattern matching is not greedy:
// ".*\\.cbz" doesn't match a path with another dot before the extension, the patterns with more dots cover those.
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataSchemes = new[] { "file", "content" },
    DataHost = "*",
    DataMimeType = "application/octet-stream",
    DataPathPatterns = new[]
    {
        ".*\\.cbz", ".*\\.cbr", ".*\\.cb7", ".*\\.cbt", ".*\\.pdf", ".*\\.epub",
        ".*\\..*\\.cbz", ".*\\..*\\.cbr", ".*\\..*\\.cb7", ".*\\..*\\.cbt", ".*\\..*\\.pdf", ".*\\..*\\.epub",
        ".*\\..*\\..*\\.cbz", ".*\\..*\\..*\\.cbr", ".*\\..*\\..*\\.cb7", ".*\\..*\\..*\\.cbt", ".*\\..*\\..*\\.pdf", ".*\\..*\\..*\\.epub"
    })]
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataSchemes = new[] { "file", "content" },
    DataHost = "*",
    DataMimeType = "*/*",
    DataPathPatterns = new[]
    {
        ".*\\.cbz", ".*\\.cbr", ".*\\.cb7", ".*\\.cbt", ".*\\.pdf", ".*\\.epub",
        ".*\\..*\\.cbz", ".*\\..*\\.cbr", ".*\\..*\\.cb7", ".*\\..*\\.cbt", ".*\\..*\\.pdf", ".*\\..*\\.epub",
        ".*\\..*\\..*\\.cbz", ".*\\..*\\..*\\.cbr", ".*\\..*\\..*\\.cb7", ".*\\..*\\..*\\.cbt", ".*\\..*\\..*\\.pdf", ".*\\..*\\..*\\.epub"
    })]
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

    public override bool DispatchTouchEvent(MotionEvent? ev)
    {
        // Android cancels a gesture with a single ACTION_CANCEL carrying every finger that is still down (e.g. when
        // the system takes over the touch). Avalonia only cancels the first of those pointers, the others stay "down"
        // in its TouchDevice forever. From then on no touch is primary anymore, and the reader views took every
        // single-finger touch for the second finger of a pinch: one finger zoomed, taps and swipes did nothing.
        // Cancel the other pointers one by one, so Avalonia forgets all of them.
        if (ev is { ActionMasked: MotionEventActions.Cancel, PointerCount: > 1 } && FindAvaloniaView(Window?.DecorView) is { } avaloniaView)
        {
            for (int index = 1; index < ev.PointerCount; index++)
            {
                using var cancel = ObtainSinglePointerCancel(ev, index);
                avaloniaView.DispatchTouchEvent(cancel);
            }
        }
        return base.DispatchTouchEvent(ev);
    }

    private static MotionEvent ObtainSinglePointerCancel(MotionEvent ev, int index)
    {
        var properties = new MotionEvent.PointerProperties();
        ev.GetPointerProperties(index, properties);
        var coords = new MotionEvent.PointerCoords();
        ev.GetPointerCoords(index, coords);
        return MotionEvent.Obtain(ev.DownTime, ev.EventTime, MotionEventActions.Cancel, 1,
            new[] { properties }, new[] { coords }, ev.MetaState, ev.ButtonState, ev.XPrecision, ev.YPrecision,
            ev.DeviceId, (Edge)ev.EdgeFlags, ev.Source, ev.Flags)!;
    }

    private static AvaloniaView? FindAvaloniaView(View? view)
    {
        if (view is AvaloniaView avaloniaView) return avaloniaView;
        if (view is not ViewGroup group) return null;
        for (int i = 0; i < group.ChildCount; i++)
        {
            if (FindAvaloniaView(group.GetChildAt(i)) is { } found) return found;
        }
        return null;
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

