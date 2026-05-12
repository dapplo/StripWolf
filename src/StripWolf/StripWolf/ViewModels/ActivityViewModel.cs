using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using StripWolf.Models;
using StripWolf.Models.Komga;
using StripWolf.Services;

namespace StripWolf.ViewModels;

public partial class ActivityViewModel : ViewModelBase
{
    private readonly LibraryViewModel _libraryViewModel;
    private readonly KomgaViewModel _komgaViewModel;
    private readonly EpubShadowConversionService _epubShadowConversionService;
    private bool _activityRefreshPending;

    [ObservableProperty]
    private int _activeItemsCount;

    [ObservableProperty]
    private double _overallProgress;

    [ObservableProperty]
    private ObservableCollection<EpubConversionState> _epubConversions = [];

    public ObservableCollection<PendingImport> PendingImports => _libraryViewModel.PendingImports;
    public ObservableCollection<KomgaDownloadQueueItem> DownloadQueueItems => _komgaViewModel.DownloadQueueItems;
    public LibraryViewModel Library => _libraryViewModel;
    public KomgaViewModel Komga => _komgaViewModel;

    public bool HasActiveItems => ActiveItemsCount > 0;

    public ActivityViewModel(
        LibraryViewModel libraryViewModel,
        KomgaViewModel komgaViewModel,
        EpubShadowConversionService epubShadowConversionService)
    {
        _libraryViewModel = libraryViewModel;
        _komgaViewModel = komgaViewModel;
        _epubShadowConversionService = epubShadowConversionService;
        Title = "Activity";

        PendingImports.CollectionChanged += OnPendingImportsChanged;
        DownloadQueueItems.CollectionChanged += OnDownloadQueueChanged;
        foreach (var pendingImport in PendingImports)
        {
            pendingImport.PropertyChanged += OnActivityItemPropertyChanged;
        }

        foreach (var queueItem in DownloadQueueItems)
        {
            queueItem.PropertyChanged += OnActivityItemPropertyChanged;
        }

        _epubShadowConversionService.ConversionStateChanged += OnEpubConversionStateChanged;
        _ = RefreshEpubConversionsAsync();
        RefreshActivityState();
    }

    partial void OnActiveItemsCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasActiveItems));
    }

    private void OnPendingImportsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (var pendingImport in e.NewItems.OfType<PendingImport>())
            {
                pendingImport.PropertyChanged += OnActivityItemPropertyChanged;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (var pendingImport in e.OldItems.OfType<PendingImport>())
            {
                pendingImport.PropertyChanged -= OnActivityItemPropertyChanged;
            }
        }

        ScheduleRefreshActivityState();
    }

    private void OnDownloadQueueChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (var queueItem in e.NewItems.OfType<KomgaDownloadQueueItem>())
            {
                queueItem.PropertyChanged += OnActivityItemPropertyChanged;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (var queueItem in e.OldItems.OfType<KomgaDownloadQueueItem>())
            {
                queueItem.PropertyChanged -= OnActivityItemPropertyChanged;
            }
        }

        ScheduleRefreshActivityState();
    }

    private void OnActivityItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PendingImport.Progress) or nameof(PendingImport.IsProcessing) or nameof(PendingImport.IsCompleted) or nameof(PendingImport.IsFailed)
            or nameof(KomgaDownloadQueueItem.Progress) or nameof(KomgaDownloadQueueItem.IsDownloading) or nameof(KomgaDownloadQueueItem.IsQueued) or nameof(KomgaDownloadQueueItem.IsFailed))
        {
            ScheduleRefreshActivityState();
        }
    }

    private void ScheduleRefreshActivityState()
    {
        if (_activityRefreshPending)
        {
            return;
        }

        _activityRefreshPending = true;
        void Refresh()
        {
            _activityRefreshPending = false;
            RefreshActivityState();
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Refresh();
        }
        else
        {
            Dispatcher.UIThread.Post(Refresh, DispatcherPriority.Background);
        }
    }

    private void RefreshActivityState()
    {
        ActiveItemsCount = PendingImports.Count + DownloadQueueItems.Count + EpubConversions.Count;

        var inFlightImports = PendingImports.Where(item => item.IsProcessing).Select(item => item.Progress);
        var inFlightDownloads = DownloadQueueItems.Where(item => item.IsDownloading).Select(item => item.Progress);
        var progressValues = inFlightImports.Concat(inFlightDownloads).ToList();
        OverallProgress = progressValues.Count == 0 ? 0 : progressValues.Average();
    }

    private void OnEpubConversionStateChanged(object? sender, int comicId)
    {
        _ = RefreshEpubConversionsAsync();
    }

    private async Task RefreshEpubConversionsAsync()
    {
        var states = await _epubShadowConversionService.GetActiveConversionsAsync();
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            EpubConversions = new ObservableCollection<EpubConversionState>(
                states.OrderBy(state => state.UpdatedAtUtc)
                    .ThenBy(state => state.ComicId));
            ScheduleRefreshActivityState();
        });
    }
}
