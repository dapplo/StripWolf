using System;
using System.IO;
using System.Linq;
using System.Threading;
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

    public static readonly System.Collections.Generic.HashSet<string> AllowedFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "cbz", "cbr", "cbt", "cb7", "epub", "pdf"
    };

    /// <summary>
    /// Page turns are counted in memory and written in one go after this delay,
    /// so reading doesn't cause a database write for every page.
    /// </summary>
    private static readonly TimeSpan PagesReadFlushDelay = TimeSpan.FromSeconds(5);

    private readonly System.Threading.SemaphoreSlim _settingsSemaphore = new(1, 1);
    private int _pendingPagesRead;
    private int _isPagesReadFlushScheduled;

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

    // Note: the usage counters feed the "personal reading statistics" in the settings, which are shown in every
    // build (not only in the Play Store trial), so they are always recorded. They are aggregated counters now,
    // the database no longer grows with every event.
    private async void OnLocalComicImported(object? sender, string filePath)
    {
        await IncrementUsageAsync("LocalImport");
    }

    private async void OnKomgaBookDownloaded(object? sender, string bookId)
    {
        await IncrementUsageAsync("KomgaDownload");
    }

    private async void OnPageRead(object? sender, EventArgs e)
    {
        Interlocked.Increment(ref _pendingPagesRead);
        if (Interlocked.Exchange(ref _isPagesReadFlushScheduled, 1) == 1)
        {
            // A flush is already pending, it will include this page
            return;
        }

        await Task.Delay(PagesReadFlushDelay);

        // Reset the flag before taking the count: a page read in between schedules a new flush (which might write 0)
        Volatile.Write(ref _isPagesReadFlushScheduled, 0);
        var pagesRead = Interlocked.Exchange(ref _pendingPagesRead, 0);
        await IncrementUsageAsync("PagesRead", pagesRead);
    }

    /// <summary>
    /// The key under which an opened Komga book is remembered for the trial view limit.
    /// Komga book ids are alphanumeric strings (e.g. "0F99E2NAQ5A4R") and two servers can use the same id,
    /// so the id is qualified with the id of the server.
    /// </summary>
    public static string GetKomgaViewKey(int? serverId, string bookId) => $"{serverId ?? 0}:{bookId}";

    private async void OnComicOpened(object? sender, ComicOpenedEventArgs e)
    {
        try
        {
            await IncrementUsageAsync("ComicOpen");

            if (!IsUnlimitedUnlocked)
            {
                await _settingsSemaphore.WaitAsync();
                try
                {
                    if (e.Source == ComicSource.Komga)
                    {
                        // For Komga the identifier is the key from GetKomgaViewKey (the reader passes it)
                        var viewKey = e.Identifier;
                        if (!string.IsNullOrEmpty(viewKey))
                        {
                            await _settingsService.UpdateSettingsAsync(s =>
                            {
                                if (!s.PermanentViewedKomgaBooks.Contains(viewKey))
                                {
                                    s.PermanentViewedKomgaBooks.Add(viewKey);
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
                finally
                {
                    _settingsSemaphore.Release();
                }
            }
        }
        catch
        {
            // Gracefully ignore settings update/I/O exceptions from async void event handler
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

        if (!AllowedFormats.Contains(ext)) return true;

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
    /// <param name="viewKey">Key of the book, see GetKomgaViewKey</param>
    public Task<bool> CanOpenKomgaAsync(string viewKey)
    {
        if (IsUnlimitedUnlocked) return Task.FromResult(true);

        var settings = _settingsService.LoadSettings();

        // Already viewed in trial - free to open again
        if (settings.PermanentViewedKomgaBooks.Contains(viewKey))
        {
            return Task.FromResult(true);
        }

        return Task.FromResult(settings.PermanentViewedKomgaBooks.Count < MaxTrialLimit);
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
    /// Increments a usage statistics counter in the database.
    /// </summary>
    public async Task IncrementUsageAsync(string metric, long amount = 1)
    {
        await _databaseService.IncrementUsageAsync(metric, amount);
    }
}
