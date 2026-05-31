// StripWolf - an open source comic book reader
// Copyright (C) 2026 Dapplo - Robin Krom
//
// For more information see: https://github.com/dapplo/StripWolf
// The StripWolf project is hosted on GitHub https://github.com/dapplo/StripWolf
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StripWolf.Core.Models;
using StripWolf.Core.Services;
using StripWolf.Core.Resources;

namespace StripWolf.Core.ViewModels;

/// <summary>
/// View model for the library page
/// </summary>
public partial class LibraryViewModel : ViewModelBase
{
    private readonly LibraryService _libraryService;
    private readonly ComicReaderService _comicReaderService;
    private readonly ImportQueueService _importQueueService;
    private readonly SettingsService _settingsService;
    private readonly KomgaApiServiceFactory _komgaApiServiceFactory;
    private readonly KomgaSyncService _komgaSyncService;
    private CancellationTokenSource? _deleteCountdownLoopCancellation;
    private Task? _deleteCountdownLoopTask;
    private bool _hasLoadedComics;
    private bool _isApplyingSectionLayout;

    [ObservableProperty]
    private ObservableCollection<Comic> _newComics = [];

    [ObservableProperty]
    private ObservableCollection<Comic> _inProgressComics = [];

    [ObservableProperty]
    private ObservableCollection<Comic> _completedComics = [];

    [ObservableProperty]
    private ObservableCollection<Comic> _favoriteComics = [];

    [ObservableProperty]
    private ObservableCollection<ComicSeriesGroup> _seriesGroups = [];

    [ObservableProperty]
    private Comic? _selectedComic;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<Comic> _searchResults = [];

    [ObservableProperty]
    private bool _isSearching;

    public SectionLayoutItemViewModel ContinueReadingSection { get; } = new(LibrarySectionKeys.ContinueReading);
    public SectionLayoutItemViewModel NewComicsSection { get; } = new(LibrarySectionKeys.NewComics);
    public SectionLayoutItemViewModel FavoritesSection { get; } = new(LibrarySectionKeys.Favorites);
    public SectionLayoutItemViewModel SeriesSection { get; } = new(LibrarySectionKeys.Series);
    public SectionLayoutItemViewModel ReadSection { get; } = new(LibrarySectionKeys.Read);

    private const int DeleteUndoTimeoutSeconds = 5;

    /// <summary>
    /// Whether the app is running on desktop (not mobile)
    /// </summary>
    public bool IsDesktop => !OperatingSystem.IsAndroid() && !OperatingSystem.IsIOS();

    /// <summary>
    /// Event raised when a comic should be opened in the reader
    /// </summary>
    public event EventHandler<int>? ComicOpenRequested;

    /// <summary>
    /// Event raised when a request is made to view a specific Komga series
    /// </summary>
    public event EventHandler<KomgaSeriesNavigationRequest>? ViewKomgaSeriesRequested;

    [ObservableProperty]
    private Comic? _selectedInfoComic;

    [ObservableProperty]
    private bool _isEditingMetadata;

    [ObservableProperty]
    private ComicInfo? _editingComicInfo;

    [RelayCommand]
    private async Task EditMetadataAsync()
    {
        if (SelectedInfoComic is null) return;
        
        IsEditingMetadata = true;
        try
        {
            EditingComicInfo = await _libraryService.GetComicInfoAsync(SelectedInfoComic.FilePath) ?? new ComicInfo
            {
                Title = SelectedInfoComic.Title,
                Series = SelectedInfoComic.SeriesName,
                Number = SelectedInfoComic.Number?.ToString()
            };
        }
        catch
        {
            EditingComicInfo = new ComicInfo
            {
                Title = SelectedInfoComic.Title,
                Series = SelectedInfoComic.SeriesName,
                Number = SelectedInfoComic.Number?.ToString()
            };
        }
    }

    [RelayCommand]
    private void CancelMetadataEdit()
    {
        IsEditingMetadata = false;
        EditingComicInfo = null;
    }

    [RelayCommand]
    private async Task SaveMetadataAsync()
    {
        if (SelectedInfoComic is null || EditingComicInfo is null) return;

        await ExecuteAsync(async () =>
        {
            await _libraryService.UpdateComicMetadataAsync(SelectedInfoComic, EditingComicInfo);
            IsEditingMetadata = false;
            EditingComicInfo = null;
            
            // Refresh the UI to show updated values
            OnPropertyChanged(nameof(SelectedInfoComic));
        });
    }

    public bool ShowContinueReadingSection => ContinueReadingSection.IsVisible && InProgressComics.Count > 0;
    public bool ShowNewComicsSection => NewComicsSection.IsVisible;
    public bool ShowFavoritesSection => FavoritesSection.IsVisible && FavoriteComics.Count > 0;
    public bool ShowSeriesSection => SeriesSection.IsVisible && SeriesGroups.Count > 0;
    public bool ShowReadSection => ReadSection.IsVisible && CompletedComics.Count > 0;
    public ObservableCollection<PendingImport> PendingImports => _importQueueService.PendingImports;

    public LibraryViewModel(
        LibraryService libraryService, 
        ComicReaderService comicReaderService, 
        ImportQueueService importQueueService, 
        SettingsService settingsService,
        KomgaApiServiceFactory komgaApiServiceFactory,
        KomgaSyncService komgaSyncService)
    {
        _libraryService = libraryService;
        _comicReaderService = comicReaderService;
        _importQueueService = importQueueService;
        _settingsService = settingsService;
        _komgaApiServiceFactory = komgaApiServiceFactory;
        _komgaSyncService = komgaSyncService;
        Title = Loc.Instance.Library;

        RegisterSectionLayoutState(ContinueReadingSection);
        RegisterSectionLayoutState(NewComicsSection);
        RegisterSectionLayoutState(FavoritesSection);
        RegisterSectionLayoutState(SeriesSection);
        RegisterSectionLayoutState(ReadSection);

        ApplySectionLayout(_settingsService.LoadSettings());
        _settingsService.SettingsChanged += (_, settings) =>
        {
            Dispatcher.UIThread.Post(() => 
            {
                ApplySectionLayout(settings);
                RefreshLocalization();
            });
        };
        
        // Refresh when library changes
        _libraryService.LibraryChanged += (s, e) => _ = RefreshAsync();
    }

    private void RefreshLocalization()
    {
        Title = Loc.Instance.Library;
        OnPropertyChanged(nameof(Title));
        
        foreach (var comic in GetComicCollections().SelectMany(c => c))
        {
            comic.RefreshLocalization();
        }

        foreach (var group in SeriesGroups)
        {
            group.RefreshLocalization();
            foreach (var comic in group.Comics)
            {
                comic.RefreshLocalization();
            }
        }

        RefreshSectionVisibilityState();
        
        // Refresh properties bound to Loc.Instance
        OnPropertyChanged(nameof(Loc.Instance.Library));
        OnPropertyChanged(nameof(Loc.Instance.SearchPlaceholder));
        OnPropertyChanged(nameof(Loc.Instance.SectionContinueReading));
        OnPropertyChanged(nameof(Loc.Instance.SectionNewComics));
        OnPropertyChanged(nameof(Loc.Instance.SectionFavorites));
        OnPropertyChanged(nameof(Loc.Instance.SectionSeries));
        OnPropertyChanged(nameof(Loc.Instance.SectionRead));
    }

    public Task EnsureComicsLoadedAsync()
    {
        return _hasLoadedComics ? Task.CompletedTask : LoadComicsAsync();
    }

    [RelayCommand]
    private void ShowComicInfo(Comic comic)
    {
        SelectedInfoComic = comic;
    }

    [RelayCommand]
    private void CloseComicInfo()
    {
        SelectedInfoComic = null;
    }

    [RelayCommand]
    private void ViewSeriesOnKomga(Comic comic)
    {
        if (!string.IsNullOrEmpty(comic.KomgaSeriesId))
        {
            ViewKomgaSeriesRequested?.Invoke(this, new KomgaSeriesNavigationRequest
            {
                SeriesId = comic.KomgaSeriesId,
                ServerId = comic.KomgaServerId
            });
            SelectedInfoComic = null;
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        // Trigger search when text changes
        _ = SearchAsync();
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            IsSearching = false;
            SearchResults = new ObservableCollection<Comic>();
            return;
        }

        IsSearching = true;
        try
        {
            var results = await _libraryService.SearchComicsAsync(SearchText);
            ApplyPendingDeletionState(results);
            SearchResults = new ObservableCollection<Comic>(results);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Search failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
        IsSearching = false;
        SearchResults.Clear();
    }

    [ObservableProperty]
    private bool _showDeleteConfirmation;

    [ObservableProperty]
    private string _deleteConfirmationPath = string.Empty;

    [ObservableProperty]
    private Comic? _comicPendingDeletion;

    [RelayCommand]
    private async Task LoadComicsAsync()
    {
        await ExecuteAsync(async () =>
        {
            // Ensure Komga API is configured if possible
            var settings = _settingsService.LoadSettings();
            var browsingServer = settings.Servers.FirstOrDefault(s => s.Id == settings.ActiveServerId);
            var browsingKomgaApiService = browsingServer is not null
                ? _komgaApiServiceFactory.GetForServer(browsingServer)
                : null;

            var favorites = await _libraryService.GetFavoriteComicsAsync();
            FavoriteComics = ApplyComics(FavoriteComics, favorites);
             
            var newComicsData = await _libraryService.GetNewComicsAsync();
            NewComics = ApplyComics(NewComics, newComicsData);

            var inProgress = await _libraryService.GetInProgressComicsAsync();
            InProgressComics = ApplyComics(InProgressComics, inProgress);

            var completed = await _libraryService.GetCompletedComicsAsync();
            CompletedComics = ApplyComics(CompletedComics, completed);

            RefreshSeriesGroups();
            RefreshSectionVisibilityState();

            // Defer cleanup to background after initial load is done
            _ = Task.Run(async () => 
            {
                try { await _libraryService.CleanupMissingFilesAsync(); } catch { }
            });

            // Trigger Komga sync in background
            if (browsingKomgaApiService is not null)
            {
                _ = _komgaSyncService.SyncAllComicsAsync();
            }

            _hasLoadedComics = true;
        });
    }

    private ObservableCollection<Comic> ApplyComics(ObservableCollection<Comic> targetCollection, List<Comic> comics)
    {
        ApplyPendingDeletionState(comics);

        if (!_hasLoadedComics && _deleteCancellationTokens.Count == 0)
        {
            return new ObservableCollection<Comic>(comics);
        }

        MergeComics(targetCollection, comics);
        return targetCollection;
    }

    private void ApplyPendingDeletionState(IEnumerable<Comic> comics)
    {
        foreach (var comic in comics)
        {
            if (_deleteCancellationTokens.TryGetValue(comic.Id, out var entry))
            {
                comic.IsDeleting = true;
                comic.DeletionSecondsRemaining = entry.Comic.DeletionSecondsRemaining;
            }
        }
    }

    private void MergeComics(ObservableCollection<Comic> currentList, List<Comic> newList)
    {
        // Remove items no longer in the list (unless they are being deleted)
        var toRemove = currentList.Where(c => !c.IsDeleting && newList.All(n => n.Id != c.Id)).ToList();
        foreach (var item in toRemove) currentList.Remove(item);

        // Add or update items
        for (int i = 0; i < newList.Count; i++)
        {
            var newItem = newList[i];
            var existing = currentList.FirstOrDefault(c => c.Id == newItem.Id);

            if (existing == null)
            {
                // New item, check if it's already in the global deleting dictionary
                if (_deleteCancellationTokens.TryGetValue(newItem.Id, out var entry))
                {
                    newItem.IsDeleting = true;
                    newItem.DeletionSecondsRemaining = entry.Comic.DeletionSecondsRemaining;
                }
                currentList.Insert(i, newItem);
            }
            else
            {
                // Update properties of existing item but preserve deletion state
                if (!existing.IsDeleting)
                {
                    existing.Title = newItem.Title;
                    existing.SeriesName = newItem.SeriesName;
                    existing.Number = newItem.Number;
                    existing.Authors = newItem.Authors;
                    existing.CoverPath = newItem.CoverPath;
                    existing.FilePath = newItem.FilePath;
                    existing.Format = newItem.Format;
                    existing.FileSize = newItem.FileSize;
                    existing.PageCount = newItem.PageCount;
                    existing.CurrentPage = newItem.CurrentPage;
                    existing.IsCompleted = newItem.IsCompleted;
                    existing.IsFavorite = newItem.IsFavorite;
                    existing.LastReadDate = newItem.LastReadDate;
                    existing.ReadProgressLastModified = newItem.ReadProgressLastModified;
                    existing.KomgaSyncStatus = newItem.KomgaSyncStatus;
                    existing.AddedDate = newItem.AddedDate;
                    existing.Source = newItem.Source;
                    existing.KomgaId = newItem.KomgaId;
                    existing.KomgaSeriesId = newItem.KomgaSeriesId;
                    existing.KomgaServerId = newItem.KomgaServerId;
                }
                
                // Move to correct position if needed
                int currentIndex = currentList.IndexOf(existing);
                if (currentIndex != i)
                {
                    currentList.Move(currentIndex, i);
                }
            }
        }
    }

    private void RefreshSeriesGroups()
    {
        var expansionStates = SeriesGroups.ToDictionary(
            group => NormalizeSeriesName(group.Name),
            group => group.IsExpanded,
            StringComparer.OrdinalIgnoreCase);

        var groups = NewComics
            .Concat(InProgressComics)
            .Concat(CompletedComics)
            .GroupBy(comic => comic.Id)
            .Select(group => group.First())
            .Where(comic => !string.IsNullOrWhiteSpace(comic.SeriesName))
            .GroupBy(comic => NormalizeSeriesName(comic.SeriesName!), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var orderedComics = group
                    .OrderBy(comic => comic.Number ?? float.MaxValue)
                    .ThenBy(comic => comic.Title, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                return new ComicSeriesGroup
                {
                    Name = orderedComics.First().SeriesName ?? group.Key,
                    Comics = new ObservableCollection<Comic>(orderedComics),
                    RepresentativeComic = orderedComics.First(),
                    IsExpanded = expansionStates.TryGetValue(group.Key, out var isExpanded) && isExpanded
                };
            })
            .Where(group => group.ComicCount > 1)
            .OrderBy(group => group.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        SeriesGroups = new ObservableCollection<ComicSeriesGroup>(groups);

        RefreshSectionVisibilityState();
    }

    private void RegisterSectionLayoutState(SectionLayoutItemViewModel section)
    {
        section.PropertyChanged += OnSectionLayoutChanged;
    }

    private void OnSectionLayoutChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SectionLayoutItemViewModel.Label))
        {
            return;
        }

        RefreshSectionVisibilityState();

        if (_isApplyingSectionLayout ||
            sender is not SectionLayoutItemViewModel ||
            e.PropertyName != nameof(SectionLayoutItemViewModel.IsExpanded))
        {
            return;
        }

        _ = PersistSectionLayoutAsync();
    }

    private Task PersistSectionLayoutAsync()
    {
        return _settingsService.UpdateSettingsAsync(settings =>
        {
            settings.LibrarySections = SectionLayoutSettings.MergeWithDefaults(
                GetSectionLayoutStates().Select(section => section.ToSettings()),
                SectionLayoutSettings.CreateDefaultLibrarySections());
        });
    }

    private void ApplySectionLayout(AppSettings settings)
    {
        _isApplyingSectionLayout = true;
        try
        {
            ApplyPreference(settings.LibrarySections, ContinueReadingSection);
            ApplyPreference(settings.LibrarySections, NewComicsSection);
            ApplyPreference(settings.LibrarySections, FavoritesSection);
            ApplyPreference(settings.LibrarySections, SeriesSection);
            ApplyPreference(settings.LibrarySections, ReadSection);
            RefreshSectionVisibilityState();
        }
        finally
        {
            _isApplyingSectionLayout = false;
        }
    }

    private IEnumerable<SectionLayoutItemViewModel> GetSectionLayoutStates()
    {
        yield return ContinueReadingSection;
        yield return NewComicsSection;
        yield return FavoritesSection;
        yield return SeriesSection;
        yield return ReadSection;
    }

    private static void ApplyPreference(
        IEnumerable<SectionLayoutSettings> preferences,
        SectionLayoutItemViewModel section)
    {
        var preference = preferences.FirstOrDefault(item => string.Equals(item.Key, section.Key, StringComparison.OrdinalIgnoreCase));
        if (preference is null)
        {
            return;
        }

        section.Apply(preference);
    }

    private void RefreshSectionVisibilityState()
    {
        OnPropertyChanged(nameof(ShowContinueReadingSection));
        OnPropertyChanged(nameof(ShowNewComicsSection));
        OnPropertyChanged(nameof(ShowFavoritesSection));
        OnPropertyChanged(nameof(ShowSeriesSection));
        OnPropertyChanged(nameof(ShowReadSection));
    }

    private static string NormalizeSeriesName(string seriesName)
    {
        return string.Join(' ', seriesName
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
    }

    private IEnumerable<ObservableCollection<Comic>> GetComicCollections()
    {
        yield return NewComics;
        yield return InProgressComics;
        yield return CompletedComics;
        yield return FavoriteComics;
        yield return SearchResults;
    }

    private void UpdateTrackedComics(int comicId, Action<Comic> update)
    {
        foreach (var comic in GetComicCollections().SelectMany(collection => collection.Where(item => item.Id == comicId)))
        {
            update(comic);
        }
    }

    private static bool ContainsComic(ObservableCollection<Comic> collection, int comicId)
    {
        return collection.Any(comic => comic.Id == comicId);
    }

    private static void RemoveComic(ObservableCollection<Comic> collection, int comicId)
    {
        var existing = collection.FirstOrDefault(comic => comic.Id == comicId);
        if (existing is not null)
        {
            collection.Remove(existing);
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        await LoadComicsAsync();
        IsRefreshing = false;
    }

    [RelayCommand]
    private void OpenComicsDirectory()
    {
        try
        {
            var path = _libraryService.ComicsDirectory;
            if (OperatingSystem.IsWindows())
            {
                Process.Start("explorer.exe", path);
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", path);
            }
            else if (OperatingSystem.IsLinux())
            {
                Process.Start("xdg-open", path);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to open directory: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ImportFileAsync(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return;
        }

        await ImportFilesCoreAsync([filePath], null);
    }

    [RelayCommand]
    private async Task ImportFilesAsync(IList<string>? filePaths)
    {
        if (filePaths is null || filePaths.Count == 0)
        {
            return;
        }

        await ImportFilesCoreAsync(filePaths, null);
    }

    [RelayCommand]
    private async Task ImportDirectoryAsync(string? directoryPath)
    {
        await ImportDirectoryWithOptionsAsync(directoryPath, null, suppressAutomaticDirectoryFallback: false);
    }

    public async Task ImportDirectoryWithOptionsAsync(
        string? directoryPath,
        string? seriesNameFallback,
        bool suppressAutomaticDirectoryFallback)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return;
        }

        var filePaths = _libraryService.GetSupportedComicFilesInDirectory(directoryPath);
        if (filePaths.Count == 0)
        {
            ErrorMessage = "No supported comic files were found in the selected folder.";
            return;
        }

        await ImportFilesCoreAsync(filePaths, directoryPath, seriesNameFallback, suppressAutomaticDirectoryFallback);
    }

    private async Task ImportFilesCoreAsync(
        IList<string> filePaths,
        string? importRootDirectory,
        string? seriesNameFallbackOverride = null,
        bool suppressAutomaticDirectoryFallback = false)
    {
        ErrorMessage = null;

        // Create pending import items for each file
        var pendingItems = new List<PendingImport>();
        foreach (var filePath in filePaths)
        {
            var pending = new PendingImport
            {
                FilePath = filePath,
                FileName = GetImportDisplayName(filePath, importRootDirectory),
                Status = "Waiting..."
            };
            pendingItems.Add(pending);
            await _importQueueService.EnqueueAsync(pending);
        }

        using var deferredLibraryChanged = _libraryService.DeferLibraryChanged();

        // Process files sequentially
        foreach (var pending in pendingItems)
        {
            pending.IsProcessing = true;
            var format = ComicReaderService.GetComicFormat(pending.FilePath);
            var useLazyUnsupportedFormats = _settingsService.LoadSettings().UnsupportedFormatHandlingMode == UnsupportedFormatHandlingMode.ConvertWhileReading;
            var isLazyEpubImport = useLazyUnsupportedFormats && format == ComicFormat.Epub;
            pending.Status = format switch
            {
                ComicFormat.Epub when isLazyEpubImport => "Analyzing EPUB...",
                ComicFormat.Pdf => "Converting PDF...",
                ComicFormat.Epub => "Converting EPUB...",
                _ => "Importing..."
            };

            try
            {
                var progress = UiProgressThrottle.Create(p =>
                {
                    pending.Progress = p;
                    pending.Status = isLazyEpubImport
                        ? p switch
                        {
                            < 0.7 => $"Preparing EPUB... {p:P0}",
                            < 0.95 => $"Copying EPUB... {p:P0}",
                            _ => $"Finalizing EPUB... {p:P0}"
                        }
                        : $"Converting... {p:P0}";
                });

                var comic = await _libraryService.ImportLocalComicAsync(
                    pending.FilePath,
                    progress,
                    suppressAutomaticDirectoryFallback
                        ? seriesNameFallbackOverride
                        : seriesNameFallbackOverride ?? LibraryService.GetDirectorySeriesNameFallback(pending.FilePath, importRootDirectory ?? string.Empty));
                
                pending.IsProcessing = false;
                pending.IsCompleted = true;
                pending.Status = "Completed";
                pending.Progress = 1.0;

                if (!NewComics.Any(c => c.Id == comic.Id))
                {
                    NewComics.Insert(0, comic);
                }

                RefreshSeriesGroups();

                ScheduleCompletedImportRemoval(pending);
            }
            catch (Exception ex)
            {
                pending.IsProcessing = false;
                pending.IsFailed = true;
                pending.Status = "Failed";
                pending.ErrorMessage = ex.Message;
                ErrorMessage = $"Failed to import '{pending.FileName}': {ex.Message}";
            }
        }
    }

    private static string GetImportDisplayName(string filePath, string? importRootDirectory)
    {
        if (string.IsNullOrWhiteSpace(importRootDirectory))
        {
            return Path.GetFileName(filePath);
        }

        var relativePath = Path.GetRelativePath(importRootDirectory, filePath);
        if (string.IsNullOrWhiteSpace(relativePath) ||
            relativePath == "." ||
            relativePath.StartsWith("..", StringComparison.Ordinal))
        {
            return Path.GetFileName(filePath);
        }

        return relativePath;
    }

    [RelayCommand]
    private void OpenComic(Comic? comic)
    {
        if (comic is null || !comic.CanOpen)
        {
            return;
        }

        SelectedComic = comic;
        // Raise event to request opening the reader
        ComicOpenRequested?.Invoke(this, comic.Id);
    }

    [RelayCommand]
    private async Task ConvertComicNowAsync(Comic? comic)
    {
        if (comic is null || !comic.CanConvertNow)
        {
            return;
        }

        var pending = new PendingImport
        {
            FilePath = comic.FilePath,
            FileName = comic.Title,
            Status = "Waiting to convert EPUB..."
        };

        try
        {
            comic.IsConverting = true;
            ErrorMessage = null;
            await _importQueueService.EnqueueAsync(pending);

            pending.IsProcessing = true;
            pending.Status = "Converting EPUB...";
            var progress = UiProgressThrottle.Create(value =>
            {
                pending.Progress = value;
                pending.Status = $"Converting EPUB... {value:P0}";
            });

            await _libraryService.ConvertPendingEpubAsync(comic, progress);
            pending.IsProcessing = false;
            pending.IsCompleted = true;
            pending.Progress = 1.0;
            pending.Status = "Completed";
            ScheduleCompletedImportRemoval(pending);
        }
        catch (Exception ex)
        {
            pending.IsProcessing = false;
            pending.IsFailed = true;
            pending.Status = "Failed";
            pending.ErrorMessage = ex.Message;
            ErrorMessage = $"Failed to convert '{comic.Title}': {ex.Message}";
        }
        finally
        {
            comic.IsConverting = false;
        }
    }

    private readonly Dictionary<int, PendingComicDeletion> _deleteCancellationTokens = new();

    [RelayCommand]
    private async Task DeleteSeriesAsync(ComicSeriesGroup? seriesGroup)
    {
        if (seriesGroup is null)
        {
            return;
        }

        if (seriesGroup.HasDeletingComics)
        {
            foreach (var comic in seriesGroup.Comics.Where(static comic => comic.IsDeleting).ToList())
            {
                UndoComicDelete(comic);
            }

            return;
        }

        foreach (var comic in seriesGroup.Comics.Where(static comic => !comic.IsDeleting).ToList())
        {
            StartDeletionProcess(comic, ShouldDeleteComicFile(comic));
        }
    }

    [RelayCommand]
    private async Task DeleteComicAsync(Comic? comic)
    {
        if (comic is null || comic.IsDeleting)
        {
            return;
        }

        if (!ShouldDeleteComicFile(comic) && _settingsService.LoadSettings().SkipExternalDeleteConfirmation)
        {
            StartDeletionProcess(comic, false);
            return;
        }

        if (!ShouldDeleteComicFile(comic))
        {
            ComicPendingDeletion = comic;
            DeleteConfirmationPath = comic.FilePath;
            ShowDeleteConfirmation = true;
            return;
        }

        StartDeletionProcess(comic, true); // Internal files are always deleted physically
    }

    [RelayCommand]
    private async Task ConfirmDeleteAsync()
    {
        if (ComicPendingDeletion != null)
        {
            var comic = ComicPendingDeletion;
            ShowDeleteConfirmation = false;
            ComicPendingDeletion = null;
            StartDeletionProcess(comic, true); // Permanent delete
        }
    }

    [RelayCommand]
    private async Task RemoveFromLibraryAsync()
    {
        if (ComicPendingDeletion != null)
        {
            var comic = ComicPendingDeletion;
            ShowDeleteConfirmation = false;
            ComicPendingDeletion = null;
            StartDeletionProcess(comic, false); // Only remove from library
        }
    }

    [RelayCommand]
    private void CancelDelete()
    {
        ShowDeleteConfirmation = false;
        ComicPendingDeletion = null;
    }

    private void StartDeletionProcess(Comic comic, bool deleteFile)
    {
        // Mark as deleting in UI
        comic.IsDeleting = true;
        comic.DeletionSecondsRemaining = DeleteUndoTimeoutSeconds;

        _deleteCancellationTokens[comic.Id] = new PendingComicDeletion(comic, deleteFile, DateTime.UtcNow.AddSeconds(DeleteUndoTimeoutSeconds));
        EnsureDeleteCountdownTimer();
    }

    private void EnsureDeleteCountdownTimer()
    {
        if (_deleteCancellationTokens.Count > 0)
        {
            if (_deleteCountdownLoopTask is null || _deleteCountdownLoopTask.IsCompleted)
            {
                _deleteCountdownLoopCancellation?.Dispose();
                _deleteCountdownLoopCancellation = new CancellationTokenSource();
                _deleteCountdownLoopTask = RunDeleteCountdownLoopAsync(_deleteCountdownLoopCancellation.Token);
            }
        }
        else
        {
            _deleteCountdownLoopCancellation?.Cancel();
        }
    }

    private async Task RunDeleteCountdownLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await Dispatcher.UIThread.InvokeAsync(UpdateDeleteCountdowns);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void UpdateDeleteCountdowns()
    {
        if (_deleteCancellationTokens.Count == 0)
        {
            _deleteCountdownLoopCancellation?.Cancel();
            return;
        }

        var now = DateTime.UtcNow;
        var expiredDeletes = new List<PendingComicDeletion>();

        foreach (var entry in _deleteCancellationTokens.Values.ToList())
        {
            var secondsRemaining = Math.Max(0, (int)Math.Ceiling((entry.ExpiresAtUtc - now).TotalSeconds));
            UpdateTrackedComics(entry.Comic.Id, trackedComic =>
            {
                trackedComic.IsDeleting = true;
                trackedComic.DeletionSecondsRemaining = secondsRemaining;
            });

            entry.Comic.IsDeleting = true;
            entry.Comic.DeletionSecondsRemaining = secondsRemaining;

            if (secondsRemaining == 0)
            {
                expiredDeletes.Add(entry);
            }
        }

        foreach (var entry in expiredDeletes)
        {
            _deleteCancellationTokens.Remove(entry.Comic.Id);
            _ = PerformPermanentComicActionAsync(entry.Comic, entry.DeleteFile);
        }

        EnsureDeleteCountdownTimer();
    }

    private async Task PerformPermanentComicActionAsync(Comic comic, bool deleteFile)
    {
        try
        {
            if (deleteFile)
            {
                await _libraryService.DeleteComicAsync(comic);
            }
            else
            {
                // Just remove from database
                await _libraryService.RemoveComicFromLibraryAsync(comic);
            }
            
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                RemoveComic(NewComics, comic.Id);
                RemoveComic(InProgressComics, comic.Id);
                RemoveComic(CompletedComics, comic.Id);
                RemoveComic(FavoriteComics, comic.Id);
                RemoveComic(SearchResults, comic.Id);
                RefreshSeriesGroups();
            });
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to perform action: {ex.Message}";
            comic.IsDeleting = false;
        }
    }

    [RelayCommand]
    private void UndoComicDelete(Comic? comic)
    {
        if (comic is null || !comic.IsDeleting)
        {
            return;
        }

        if (_deleteCancellationTokens.Remove(comic.Id))
        {
            EnsureDeleteCountdownTimer();
        }

        comic.IsDeleting = false;
        comic.DeletionSecondsRemaining = 0;
        UpdateTrackedComics(comic.Id, trackedComic =>
        {
            trackedComic.IsDeleting = false;
            trackedComic.DeletionSecondsRemaining = 0;
        });
    }

    [RelayCommand]
    private async Task ToggleReadStatusAsync(Comic? comic)
    {
        if (comic is null)
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            // Store old state for UI update
            var wasCompleted = comic.IsCompleted;
            var newIsCompleted = !wasCompleted;
             
            // Save to database first (this updates the comic state in the database)
            await _libraryService.ToggleReadStatusAsync(comic.Id);

            UpdateTrackedComics(comic.Id, trackedComic =>
            {
                trackedComic.IsCompleted = newIsCompleted;
                if (!newIsCompleted)
                {
                    trackedComic.CurrentPage = 0;
                    trackedComic.LastReadDate = null;
                }
            });
             
            // Move comic between collections for immediate UI feedback
            if (wasCompleted)
            {
                // Was completed, now unread -> move to New Comics
                RemoveComic(CompletedComics, comic.Id);
                if (!ContainsComic(NewComics, comic.Id))
                {
                    NewComics.Insert(0, comic);
                }
            }
            else
            {
                // Was not completed, now completed -> move to Read
                RemoveComic(NewComics, comic.Id);
                RemoveComic(InProgressComics, comic.Id);
                if (!ContainsComic(CompletedComics, comic.Id))
                {
                    CompletedComics.Insert(0, comic);
                }
            }

            RefreshSeriesGroups();
        }, "Failed to update read status");
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync(Comic? comic)
    {
        if (comic is null)
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            // Store old state for UI update
            var wasFavorite = comic.IsFavorite;
            var newIsFavorite = !wasFavorite;
             
            // Save to database first
            await _libraryService.ToggleFavoriteAsync(comic.Id);
             
            UpdateTrackedComics(comic.Id, trackedComic => trackedComic.IsFavorite = newIsFavorite);
             
            // Update the favorites collection
            if (newIsFavorite)
            {
                if (!ContainsComic(FavoriteComics, comic.Id))
                {
                    FavoriteComics.Insert(0, comic);
                }
            }
            else
            {
                RemoveComic(FavoriteComics, comic.Id);
            }
        }, "Failed to update favorite status");
    }

    [RelayCommand]
    private void RemovePendingImport(PendingImport? pending)
    {
        if (pending is not null)
        {
            PendingImports.Remove(pending);
        }
    }

    private void ScheduleCompletedImportRemoval(PendingImport pending)
    {
        _ = RemoveCompletedImportAfterDelayAsync(pending);
    }

    private async Task RemoveCompletedImportAfterDelayAsync(PendingImport pending)
    {
        await Task.Delay(500);
        await _importQueueService.RemoveAsync(pending);
    }

    /// <summary>
    /// Performs permanent deletion of all comics in the pending delete queue.
    /// Should be called when the application is closing.
    /// </summary>
    public async Task DeleteAllPendingComicsAsync()
    {
        _deleteCountdownLoopCancellation?.Cancel();

        var tokens = _deleteCancellationTokens.ToList();
        
        foreach (var entry in tokens)
        {
            try
            {
                if (entry.Value.DeleteFile)
                {
                    await _libraryService.DeleteComicAsync(entry.Value.Comic);
                }
                else
                {
                    await _libraryService.RemoveComicFromLibraryAsync(entry.Value.Comic);
                }
            }
            catch
            {
                // Ignore errors during app shutdown
            }
        }
        
        _deleteCancellationTokens.Clear();
    }

    private sealed class PendingComicDeletion(Comic comic, bool deleteFile, DateTime expiresAtUtc)
    {
        public Comic Comic { get; } = comic;
        public bool DeleteFile { get; } = deleteFile;
        public DateTime ExpiresAtUtc { get; } = expiresAtUtc;
    }

    private bool ShouldDeleteComicFile(Comic comic)
    {
        return comic.Source == ComicSource.Komga || IsManagedComicFile(comic.FilePath);
    }

    private bool IsManagedComicFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var managedDirectory = EnsureTrailingDirectorySeparator(Path.GetFullPath(_libraryService.ComicsDirectory));
        var normalizedFilePath = Path.GetFullPath(filePath);
        return normalizedFilePath.StartsWith(managedDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureTrailingDirectorySeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }
}
