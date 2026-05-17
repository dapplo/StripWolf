using Avalonia.Controls;
using Avalonia.Controls.Templates;
using StripWolf.ViewModels;
using StripWolf.Views;

namespace StripWolf;

/// <summary>
/// Given a view model, returns the corresponding view if possible.
/// This implementation is Native AOT compatible by avoiding reflection.
/// </summary>
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        return param switch
        {
            MainViewModel => new MainView(),
            LibraryViewModel => new LibraryView(),
            ReaderViewModel => new ReaderView(),
            KomgaViewModel => new KomgaView(),
            SettingsViewModel => new SettingsView(),
            ActivityViewModel => new ActivityView(),
            _ => CreateFallback(param)
        };
    }

    private static Control CreateFallback(object param)
    {
        var name = param.GetType().FullName?.Replace("ViewModel", "View", StringComparison.Ordinal) ?? "Unknown";
        return new TextBlock { Text = "Not Found: " + name };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}