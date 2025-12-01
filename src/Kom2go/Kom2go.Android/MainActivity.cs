using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;
using Kom2go.Android.Services;
using Kom2go.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Kom2go.Android;

[Activity(
    Label = "Kom2go.Android",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity<App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        // Register Android-specific PDF renderer before the app initializes
        App.RegisterPdfRenderer = services => 
            services.AddSingleton<IPdfRenderer, AndroidPdfRenderer>();
        
        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }
}
