using Kom2go.Views;

namespace Kom2go;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        
        // Register routes for navigation
        Routing.RegisterRoute("reader", typeof(ReaderPage));
        Routing.RegisterRoute("settings", typeof(SettingsPage));
    }
}
