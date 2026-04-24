using System;
using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using StripWolf.Android.Services;
using StripWolf.Services;
using Microsoft.Extensions.DependencyInjection;

namespace StripWolf.Android;

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
            
        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }
}
