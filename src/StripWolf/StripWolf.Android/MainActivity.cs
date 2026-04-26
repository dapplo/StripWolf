using Android.App;
using Android.Content.PM;
using Android.OS;
using Avalonia.Android;

namespace StripWolf.Android;

[Activity(
    Label = "StripWolf",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        // Handle splash screen transition
        AndroidX.Core.SplashScreen.SplashScreen.InstallSplashScreen(this);
        
        base.OnCreate(savedInstanceState);
    }
}
