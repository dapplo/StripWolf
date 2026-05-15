using Foundation;
using Avalonia;
using Avalonia.iOS;

namespace StripWolf.iOS;

// The UIApplicationDelegate for the application. This class is responsible for launching the
// User Interface of the application, as well as listening (and optionally responding) to application events from iOS.
[Register("AppDelegate")]
public partial class AppDelegate : AvaloniaAppDelegate<App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        // Register iOS-specific services here if needed
        // App.RegisterPdfRenderer = ...
        // App.RegisterWebViewSnapshotService = ...
        
        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }
}
