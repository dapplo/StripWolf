using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using StripWolf.Models;
using StripWolf.Models.Komga;

namespace StripWolf.ViewModels;

public partial class ActivityViewModel : ViewModelBase
{
    private readonly LibraryViewModel _libraryViewModel;
    private readonly KomgaViewModel _komgaViewModel;

    [ObservableProperty]
    private int _activeItemsCount;

    [ObservableProperty]
    private double _overallProgress;

    public ObservableCollection<PendingImport> PendingImports => _libraryViewModel.PendingImports;
    public ObservableCollection<KomgaDownloadQueueItem> DownloadQueueItems => _komgaViewModel.DownloadQueueItems;
    public LibraryViewModel Library => _libraryViewModel;
    public KomgaViewModel Komga => _komgaViewModel;

    public bool HasActiveItems => ActiveItemsCount > 0;

    public ActivityViewModel(LibraryViewModel libraryViewModel, KomgaViewModel komgaViewModel)
    {
        _libraryViewModel = libraryViewModel;
        _komgaViewModel = komgaViewModel;
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

        RefreshActivityState();
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

        RefreshActivityState();
    }

    private void OnActivityItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PendingImport.Progress) or nameof(PendingImport.IsProcessing) or nameof(PendingImport.IsCompleted) or nameof(PendingImport.IsFailed)
            or nameof(KomgaDownloadQueueItem.Progress) or nameof(KomgaDownloadQueueItem.IsDownloading) or nameof(KomgaDownloadQueueItem.IsQueued) or nameof(KomgaDownloadQueueItem.IsFailed))
        {
            RefreshActivityState();
        }
    }

    private void RefreshActivityState()
    {
        ActiveItemsCount = PendingImports.Count + DownloadQueueItems.Count;

        var inFlightImports = PendingImports.Where(item => item.IsProcessing).Select(item => item.Progress);
        var inFlightDownloads = DownloadQueueItems.Where(item => item.IsDownloading).Select(item => item.Progress);
        var progressValues = inFlightImports.Concat(inFlightDownloads).ToList();
        OverallProgress = progressValues.Count == 0 ? 0 : progressValues.Average();
    }
}
