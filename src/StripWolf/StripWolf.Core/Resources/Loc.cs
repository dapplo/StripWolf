using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;

namespace StripWolf.Core.Resources;

/// <summary>
/// Provides localized strings for the UI. Bind to this class in XAML.
/// </summary>
public class Loc : INotifyPropertyChanged
{
    private static readonly ResourceManager ResourceManager = new("StripWolf.Core.Resources.Strings", typeof(Loc).Assembly);
    private static readonly Loc _instance = new();

    public static Loc Instance => _instance;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Call this when the culture changes to refresh all bindings
    /// </summary>
    public void RefreshLocalization()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
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

    // Main Titles
    public string Settings => GetString();
    public string Library => GetString();
    public string Komga => GetString();
    public string Activity => GetString();
    public string Reader => GetString();
    public string Refresh => GetString();

    // Navigation
    public string Back => GetString();
    public string Home => GetString();
    public string WelcomeExperienceBack => GetString();
    public string WelcomeExperienceNext => GetString();
    public string WelcomeExperienceSkip => GetString();
    public string WelcomeExperienceFinish => GetString();

    // Settings Sections
    public string LanguageSettings => GetString();
    public string UiSettings => GetString();
    public string LibrarySettings => GetString();
    public string KomgaSettings => GetString();
    public string ReadingSettings => GetString();
    public string SectionLayout => GetString();
    public string ServerConfiguration => GetString();
    public string AuthenticationTitle => GetString();
    public string ApiKeyTitle => GetString();
    public string Or => GetString();
    public string UsernamePasswordTitle => GetString();
    public string About => GetString();
    public string KomgaServers => GetString();
    public string SupportMe => GetString();

    // Setting Labels
    public string Language => GetString();
    public string StartupBehavior => GetString();
    public string DefaultReadingMode => GetString();
    public string DefaultReadingDirection => GetString();
    public string Handedness => GetString();
    public string CompactOverview => GetString();
    public string UseFullScreenWhenReading => GetString();
    public string AppTheme => GetString();
    public string EpubConversionTheme => GetString();
    public string EpubOutputResolution => GetString();
    public string UnsupportedFormatHandling => GetString();
    public string KomgaParallelDownloads => GetString();
    public string KomgaSeriesPageSize => GetString();
    public string KomgaSearchLimit => GetString();
    public string KomgaSmartListSize => GetString();
    public string AllowMeteredKomgaDownloads => GetString();
    public string SkipExternalDeleteConfirmation => GetString();
    public string Name => GetString();
    public string ServerUrl => GetString();
    public string Username => GetString();
    public string Password => GetString();
    public string CustomHttpHeaders => GetString();

    // Setting Values / Options
    public string ContinueWhereLeftOff => GetString();
    public string LibraryView => GetString();
    public string ReadingModeNormal => GetString();
    public string ReadingModeZoomed => GetString();
    public string ReadingModeGuided => GetString();
    public string ReadingDirectionAutomatic => GetString();
    public string ReadingDirectionLeftToRight => GetString();
    public string ReadingDirectionRightToLeft => GetString();
    public string ReadingDirectionLeftToRightReversedPages => GetString();
    public string ReadingDirectionRightToLeftReversedPages => GetString();
    public string HandednessRight => GetString();
    public string HandednessLeft => GetString();
    public string ThemeSystem => GetString();
    public string ThemeLight => GetString();
    public string ThemeDark => GetString();
    public string ResolutionLow => GetString();
    public string ResolutionMedium => GetString();
    public string ResolutionHigh => GetString();
    public string UnsupportedFormatHandlingConvertOnImport => GetString();
    public string UnsupportedFormatHandlingConvertWhileReading => GetString();
    public string CompactOverviewEnabled => GetString();
    public string CompactOverviewDisabled => GetString();
    public string UseFullScreenWhenReadingEnabled => GetString();
    public string UseFullScreenWhenReadingDisabled => GetString();
    public string SkipExternalDeleteConfirmationEnabled => GetString();
    public string SkipExternalDeleteConfirmationDisabled => GetString();
    public string AllowMeteredKomgaDownloadsEnabled => GetString();
    public string AllowMeteredKomgaDownloadsDisabled => GetString();
    public string Shown => GetString();
    public string Hidden => GetString();
    public string Expanded => GetString();
    public string Collapsed => GetString();
    public string Browsing => GetString();
    public string Edit => GetString();
    public string Delete => GetString();
    public string Cancel => GetString();

    // Setting Hints
    public string LanguageDescription => GetString();
    public string StartupBehaviorHint => GetString();
    public string ReadingModeNormalDescription => GetString();
    public string ReadingModeZoomedDescription => GetString();
    public string ReadingModeGuidedDescription => GetString();
    public string ReadingDirectionDescription => GetString();
    public string HandednessDescription => GetString();
    public string CompactOverviewDescription => GetString();
    public string UseFullScreenWhenReadingDescription => GetString();
    public string AppThemeDescription => GetString();
    public string EpubConversionThemeDescription => GetString();
    public string EpubOutputResolutionDescription => GetString();
    public string UnsupportedFormatHandlingDescription => GetString();
    public string KomgaParallelDownloadsDescription => GetString();
    public string KomgaSeriesPageSizeDescription => GetString();
    public string KomgaSearchLimitDescription => GetString();
    public string KomgaSmartListSizeDescription => GetString();
    public string AllowMeteredKomgaDownloadsDescription => GetString();
    public string SkipExternalDeleteConfirmationDescription => GetString();
    public string SectionLayoutDescription => GetString();
    public string AppDescription => GetString();
    public string ProjectOnGitHub => GetString();
    public string DonateWithPayPal => GetString();
    public string DonateWithKoFi => GetString();
    public string AddServerHint => GetString();
    public string ApiKeyHint => GetString();
    public string CustomHeadersHint => GetString();

    // Library View
    public string SearchPlaceholder => GetString();
    public string SearchResults => GetString();
    public string SeriesResults => GetString();
    public string BookResults => GetString();
    public string NoResults => GetString();
    public string NoComicsFound => GetString();
    public string Sort => GetString();
    public string SyncReadProgress => GetString();
    public string SyncReadProgressEnabled => GetString();
    public string SyncReadProgressDisabled => GetString();
    public string SyncReadProgressDescription => GetString();
    public string KomgaSyncStatus => GetString();
    public string SetAsBrowsingServer => GetString();
    public string ConfirmDeleteServer => GetString();
    public string ConfirmDeleteServerMessage => GetString();
    public string ImportComic => GetString();
    public string ImportFolder => GetString();
    public string OpenFolder => GetString();
    public string Importing => GetString();
    public string NoNewComics => GetString();
    public string NoNewComicsHint => GetString();
    public string ConfirmExternalDelete => GetString();
    public string ConfirmExternalDeleteMessage => GetString();
    public string RemoveFromLibraryKeepFile => GetString();
    public string DeletePermanently => GetString();

    // Section Keys
    public string SectionContinueReading => GetString();
    public string SectionNewComics => GetString();
    public string SectionFavorites => GetString();
    public string SectionSeries => GetString();
    public string SectionRead => GetString();
    public string SectionKeepReading => GetString();
    public string SectionOnDeck => GetString();
    public string SectionRecentlyAddedBooks => GetString();
    public string SectionRecentlyAddedSeries => GetString();
    public string SectionLibraries => GetString();
    public string SectionReadLists => GetString();
    public string SeriesDescription => GetString();
    public string KomgaHome => GetString();

    // Comic Info & Metadata
    public string ComicInformation => GetString();
    public string Title => GetString();
    public string Series => GetString();
    public string Number => GetString();
    public string Pages => GetString();
    public string Format => GetString();
    public string FileSize => GetString();
    public string Summary => GetString();
    public string SummaryPlaceholder => GetString();
    public string Writer => GetString();
    public string Creators => GetString();
    public string Publisher => GetString();
    public string Genre => GetString();
    public string Tags => GetString();
    public string NotConvertedYet => GetString();
    public string PagesDisplay => GetString();
    public string TotalPagesDisplay => GetString();
    public string ComicCountDisplay => GetString();
    public string ReadCountDisplay => GetString();
    public string LoadMoreSeries => GetString();
    public string LoadMoreBooks => GetString();

    // Actions
    public string ReadNow => GetString();
    public string ConvertNow => GetString();
    public string Converting => GetString();
    public string Favorite => GetString();
    public string MarkRead => GetString();
    public string EditMetadata => GetString();
    public string DeleteComic => GetString();
    public string SaveChanges => GetString();
    public string Undo => GetString();
    public string Deleting => GetString();
    public string DeleteStatusSingle => GetString();
    public string DeleteStatusPlural => GetString();
    public string DeleteActionUndo => GetString();
    public string DeleteActionUndoMultiple => GetString();
    public string ViewSeriesOnKomga => GetString();
    public string OpenBookOnline => GetString();
    public string OpenSeriesOnline => GetString();
    public string ReadNowAction => GetString();
    public string MarkReadAction => GetString();
    public string DeleteComicAction => GetString();
    public string ContinueReadingAction => GetString();
    public string HandednessAction => GetString();
    public string ReadingDirectionAction => GetString();
    public string ShowComicInfo => GetString();
    public string AddServer => GetString();
    public string ShowHidePassword => GetString();
    public string HeaderName => GetString();
    public string Value => GetString();
    public string AddHeader => GetString();
    public string Save => GetString();
    public string TestConnection => GetString();
    public string ConfigureServer => GetString();
    public string NotConnected => GetString();
    public string AddToReadList => GetString();
    public string Download => GetString();
    public string DownloadSeries => GetString();

    // Reader View
    public string ReadingModeNormalTitle => GetString();
    public string ReadingModeZoomedTitle => GetString();
    public string ReadingModeGuidedTitle => GetString();
    public string FitMode => GetString();
    public string FullScreen => GetString();
    public string EndOfComic => GetString();
    public string EndOfComicQuestion => GetString();
    public string KomgaSyncPromptTitle => GetString();
    public string KomgaSyncPromptMessage => GetString();
    public string KomgaSyncPromptAccept => GetString();
    public string KomgaSyncPromptDecline => GetString();
    public string OpenNextInSeries => GetString();
    public string NoNextComic => GetString();
    public string BackToLibrary => GetString();
    public string TwoPageMode => GetString();
    public string SaveOriginalPage => GetString();

    // Activity View
    public string Downloads => GetString();
    public string PauseAll => GetString();
    public string ResumeAll => GetString();
    public string RetryFailed => GetString();
    public string CancelAll => GetString();
    public string Downloading => GetString();
    public string Queued => GetString();
    public string Failed => GetString();
    public string Imports => GetString();
    public string EpubConversions => GetString();
    public string PagesReady => GetString();
    public string Waiting => GetString();
    public string NoServersConfigured => GetString();

    // Errors
    public string Error => GetString();
    public string FailedToLoad => GetString();
    public string FailedToDownload => GetString();
    public string FailedToConnect => GetString();

    // Placeholders
    public string ApiKeyPlaceholder => GetString();
    public string UsernamePlaceholder => GetString();
    public string PasswordPlaceholder => GetString();

    public string BypassSslValidation => GetString();
    public string BypassSslValidationHint => GetString();

    // Update Checker
    public string NewerVersionAvailable => GetString();
    public string CheckForUpdates => GetString();
    public string UpdateAvailable => GetString();
    public string UpdatePopupMessage => GetString();
    public string BringMeThere => GetString();
    public string Ok => GetString();

    // Welcome Experience
    public string WelcomeExperience => GetString();
    public string WelcomeExperienceProgress => GetString();
    public string WelcomeExperienceIntroTitle => GetString();
    public string WelcomeExperienceIntroDescription => GetString();
    public string WelcomeExperienceQuickSetup => GetString();
    public string WelcomeExperienceQuickSetupHint => GetString();
    public string WelcomeExperienceLicenseRequired => GetString();
    public string WelcomeExperienceReviewLicense => GetString();
    public string WelcomeExperienceLicenseAccepted => GetString();
    public string WelcomeExperienceLicenseTitle => GetString();
    public string WelcomeExperienceLicenseSummary => GetString();
    public string WelcomeExperienceLicenseAsIs => GetString();
    public string WelcomeExperienceOpenGplLink => GetString();
    public string WelcomeExperienceDecline => GetString();
    public string WelcomeExperienceAcceptLicense => GetString();
    public string WelcomeExperienceLibraryTitle => GetString();
    public string WelcomeExperienceLibraryDescription => GetString();
    public string WelcomeExperienceImportTitle => GetString();
    public string WelcomeExperienceImportDescription => GetString();
    public string WelcomeExperienceKomgaTitle => GetString();
    public string WelcomeExperienceKomgaDescription => GetString();
    public string WelcomeExperienceActivityTitle => GetString();
    public string WelcomeExperienceActivityDescription => GetString();
    public string WelcomeExperienceSettingsTitle => GetString();
    public string WelcomeExperienceSettingsDescription => GetString();
    public string WelcomeExperienceSupportTitle => GetString();
    public string WelcomeExperienceSupportDescription => GetString();
    public string WelcomeExperienceSupportCta => GetString();
    public string WelcomeExperienceLibraryHint => GetString();
    public string WelcomeExperienceImportHint => GetString();
    public string WelcomeExperienceImportPointer => GetString();
    public string WelcomeExperienceKomgaHint => GetString();
    public string WelcomeExperienceActivityHint => GetString();
    public string WelcomeExperienceSettingsHint => GetString();
    public string WelcomeExperienceSupportHint => GetString();
}
