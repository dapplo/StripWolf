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
using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using StripWolf.Core.Android.Services;
using StripWolf.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace StripWolf.Core.Android;

[Application]
public class AndroidApp : AvaloniaAndroidApplication<App>
{
    public AndroidApp(IntPtr javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        // Register Android-specific PDF renderer before the app initializes
        App.RegisterPdfRenderer = services => 
            services.AddSingleton<IPdfRenderer, AndroidPdfRenderer>();
        App.RegisterWebViewSnapshotService = services =>
            services.AddSingleton<IWebViewPaginationService, AndroidWebViewSnapshotService>();
             
        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }
}

