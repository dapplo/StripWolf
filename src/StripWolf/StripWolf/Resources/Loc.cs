using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;

namespace StripWolf.Resources;

/// <summary>
/// Provides localized strings for the UI. Bind to this class in XAML.
/// </summary>
public class Loc
{
    private static readonly ResourceManager ResourceManager = new("StripWolf.Resources.Strings", typeof(Loc).Assembly);
    private static Loc _instance = new();

    public static Loc Instance => _instance;

    /// <summary>
    /// Call this when the culture changes to refresh all bindings
    /// </summary>
    public static void RefreshInstance()
    {
        _instance = new Loc();
    }
    
    private string GetString([CallerMemberName] string? key = null)
    {
        if (key == null) return string.Empty;
        try
        {
            return ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
        }
        catch
        {
            return key;
        }
    }

    // Common UI Strings
    public string AppName => GetString();
    public string Settings => GetString();
    public string Library => GetString();
    public string Komga => GetString();
    public string Reader => GetString();
    
    // Navigation
    public string Back => GetString();
    public string Home => GetString();
    public string Refresh => GetString();
    
    // Library View
    public string AllComics => GetString();
    public string ReadingNow => GetString();
    public string Completed => GetString();
    public string Favorites => GetString();
    public string ImportComic => GetString();
    public string ImportFolder => GetString();
    public string NoComicsFound => GetString();
    public string NoComicsInLibrary => GetString();
    public string Pages => GetString();
    
    // Komga View
    public string NotConnected => GetString();
    public string ConfigureServer => GetString();
    public string SearchOnKomga => GetString();
    public string KeepReading => GetString();
    public string OnDeck => GetString();
    public string RecentlyAddedBooks => GetString();
    public string RecentlyAddedSeries => GetString();
    public string Libraries => GetString();
    public string ReadLists => GetString();
    public string Series => GetString();
    public string Books => GetString();
    public string Download => GetString();
    public string Downloading => GetString();
    public string LoadMore => GetString();
    public string LoadMoreSeries => GetString();
    public string LoadMoreBooks => GetString();
    public string LoadMoreReadLists => GetString();
    public string NoResults => GetString();
    public string SeriesResults => GetString();
    public string BookResults => GetString();
    
    // Settings View
    public string KomgaServers => GetString();
    public string AddServer => GetString();
    public string Active => GetString();
    public string NoServersConfigured => GetString();
    public string AddServerHint => GetString();
    public string ServerConfiguration => GetString();
    public string Name => GetString();
    public string ServerUrl => GetString();
    public string Username => GetString();
    public string Password => GetString();
    public string TestConnection => GetString();
    public string Save => GetString();
    public string Cancel => GetString();
    public string ConnectionSuccessful => GetString();
    public string ConnectionFailed => GetString();
    public string TestingConnection => GetString();
    
    // Custom HTTP Headers
    public string CustomHttpHeaders => GetString();
    public string CustomHeadersHint => GetString();
    public string HeaderName => GetString();
    public string Value => GetString();
    public string AddHeader => GetString();
    
    // Language Settings
    public string LanguageSettings => GetString();
    public string Language => GetString();
    public string UseSystemLanguage => GetString();
    public string SystemDefault => GetString();
    
    // About Section
    public string About => GetString();
    public string AppDescription => GetString();
    public string Version => GetString();
    public string ViewReleaseNotes => GetString();
    public string ReleaseNotes => GetString();
    
    // Reader View
    public string Page => GetString();
    public string PageDisplay => GetString();
    public string TwoPagePageDisplay => GetString();
    public string ShowComicInfo => GetString();
    public string ToggleTwoPageMode => GetString();
    public string ChangeFitMode => GetString();
    public string ToggleFullscreen => GetString();
    public string ResetZoom => GetString();
    
    // Comic Info Panel
    public string ComicInformation => GetString();
    public string Title => GetString();
    public string IssueNumber => GetString();
    public string Authors => GetString();
    public string Publisher => GetString();
    public string ReleaseDate => GetString();
    public string Summary => GetString();
    public string ExtendedMetadata => GetString();
    public string Volume => GetString();
    public string Genre => GetString();
    public string Characters => GetString();
    public string Teams => GetString();
    public string StoryArc => GetString();
    public string Notes => GetString();
    public string Web => GetString();
    public string NoExtendedMetadata => GetString();
    
    // Error Messages
    public string Error => GetString();
    public string FailedToLoad => GetString();
    public string FailedToDownload => GetString();
    public string FailedToConnect => GetString();
}
