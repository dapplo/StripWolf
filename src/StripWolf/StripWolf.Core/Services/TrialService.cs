using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using StripWolf.Core.Data;
using StripWolf.Core.Models;

namespace StripWolf.Core.Services;

/// <summary>
/// Service to handle trial limitations and license unlock logic.
/// </summary>
public class TrialService
{
    public const int MaxTrialLimit = 5;

    private readonly SettingsService _settingsService;
    private readonly DatabaseService _databaseService;

    public event EventHandler? PremiumUnlockRequested;

    public void RequestPremiumUnlock()
    {
        PremiumUnlockRequested?.Invoke(this, EventArgs.Empty);
    }

    public TrialService(
        SettingsService settingsService, 
        DatabaseService databaseService, 
        IAppEventsService appEventsService, 
        IBillingService? billingService = null)
    {
        _settingsService = settingsService;
        _databaseService = databaseService;

        appEventsService.LocalComicImported += OnLocalComicImported;
        appEventsService.KomgaBookDownloaded += OnKomgaBookDownloaded;
        appEventsService.ComicOpened += OnComicOpened;
        appEventsService.PageRead += OnPageRead;

        if (billingService is not null)
        {
            InitializeBillingBackground(billingService);
        }
    }

    private void InitializeBillingBackground(IBillingService billingService)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                // Wait briefly during startup to avoid competing with UI render thread
                await Task.Delay(2000);

                var hasPremium = await billingService.QueryPremiumPurchaseAsync(System.Threading.CancellationToken.None);

                if (hasPremium != IsUnlimitedUnlocked)
                {
                    await _settingsService.UpdateSettingsAsync(s =>
                    {
                        s.IsUnlimitedUnlocked = hasPremium;
                    });
                }
            }
            catch
            {
                // Fail silently (keep using cached value if offline or Play Store is unreachable)
            }
        });
    }

    private async void OnLocalComicImported(object? sender, string filePath)
    {
        await LogUsageAsync("LocalImport", filePath);
    }

    private async void OnKomgaBookDownloaded(object? sender, string bookId)
    {
        await LogUsageAsync("KomgaDownload", bookId);
    }

    private async void OnPageRead(object? sender, EventArgs e)
    {
        await LogUsageAsync("PagesRead");
    }

    private async void OnComicOpened(object? sender, ComicOpenedEventArgs e)
    {
        await LogUsageAsync("ComicOpen", e.ComicId);

        if (!IsUnlimitedUnlocked)
        {
            if (e.Source == ComicSource.Komga)
            {
                if (int.TryParse(e.Identifier, out var bookId))
                {
                    await _settingsService.UpdateSettingsAsync(s =>
                    {
                        if (!s.PermanentViewedKomgaBookIds.Contains(bookId))
                        {
                            s.PermanentViewedKomgaBookIds.Add(bookId);
                        }
                    });
                }
            }
            else
            {
                var filename = Path.GetFileName(e.Identifier);
                await _settingsService.UpdateSettingsAsync(s =>
                {
                    if (!s.PermanentViewedLocalPaths.Contains(filename))
                    {
                        s.PermanentViewedLocalPaths.Add(filename);
                    }
                });
            }
        }
    }

    public bool IsUnlimitedUnlocked
    {
        get
        {
#if PLAY_STORE_BUILD
            return _settingsService.LoadSettings().IsUnlimitedUnlocked;
#else
            return true;
#endif
        }
    }

    /// <summary>
    /// Checks if a local import of a specific file type is allowed (capacity limit: max MaxTrialLimit).
    /// </summary>
    public async Task<bool> CanImportLocalAsync(string filePath)
    {
        if (IsUnlimitedUnlocked) return true;

        var ext = Path.GetExtension(filePath)?.TrimStart('.')?.ToLowerInvariant();
        if (string.IsNullOrEmpty(ext)) return true;

        var allowedFormats = new[] { "cbz", "cbr", "cbt", "cb7", "epub", "pdf" };
        if (!allowedFormats.Contains(ext)) return true;

        // Count how many local comics of this extension currently exist in the database library
        var comics = await _databaseService.GetComicsAsync();
        var currentCount = comics.Count(c => 
        {
            if (c.Source == ComicSource.Komga) return false;
            var cExt = Path.GetExtension(c.FilePath)?.TrimStart('.')?.ToLowerInvariant();
            return cExt == ext;
        });

        return currentCount < MaxTrialLimit;
    }

    /// <summary>
    /// Checks if a Komga download is allowed (capacity limit: max MaxTrialLimit).
    /// </summary>
    public async Task<bool> CanDownloadKomgaAsync()
    {
        if (IsUnlimitedUnlocked) return true;

        // Count how many Komga-sourced comics currently exist in the library
        var comics = await _databaseService.GetComicsAsync();
        var currentCount = comics.Count(c => c.Source == ComicSource.Komga);

        return currentCount < MaxTrialLimit;
    }

    /// <summary>
    /// Checks if opening a local comic is allowed (permanent view limit: max MaxTrialLimit unique files per format).
    /// </summary>
    public async Task<bool> CanOpenLocalAsync(string filePath)
    {
        if (IsUnlimitedUnlocked) return true;

        var ext = Path.GetExtension(filePath)?.TrimStart('.')?.ToLowerInvariant();
        if (string.IsNullOrEmpty(ext)) return true;

        var filename = Path.GetFileName(filePath);
        var settings = _settingsService.LoadSettings();

        // Already viewed in trial - free to open again
        if (settings.PermanentViewedLocalPaths.Contains(filename) || settings.PermanentViewedLocalPaths.Contains(filePath))
        {
            return true;
        }

        // Count how many unique viewed local files of this extension have been opened permanently
        var currentViewedCount = settings.PermanentViewedLocalPaths
            .Count(p => Path.GetExtension(p)?.TrimStart('.')?.ToLowerInvariant() == ext);

        return currentViewedCount < MaxTrialLimit;
    }

    /// <summary>
    /// Checks if opening a Komga comic is allowed (permanent view limit: max MaxTrialLimit unique files).
    /// </summary>
    public async Task<bool> CanOpenKomgaAsync(int bookId)
    {
        if (IsUnlimitedUnlocked) return true;

        var settings = _settingsService.LoadSettings();

        // Already viewed in trial - free to open again
        if (settings.PermanentViewedKomgaBookIds.Contains(bookId))
        {
            return true;
        }

        return settings.PermanentViewedKomgaBookIds.Count < MaxTrialLimit;
    }

    /// <summary>
    /// Simulates/performs premium unlock.
    /// </summary>
    public async Task UnlockPremiumAsync()
    {
        await _settingsService.UpdateSettingsAsync(s =>
        {
            s.IsUnlimitedUnlocked = true;
        });
    }

    /// <summary>
    /// Logs usage statistics to the database.
    /// </summary>
    public async Task LogUsageAsync(string metric, object? metadata = null)
    {
        await _databaseService.LogUsageAsync(metric, metadata?.ToString());
    }
}
