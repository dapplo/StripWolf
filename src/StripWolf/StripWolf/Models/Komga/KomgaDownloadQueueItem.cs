using CommunityToolkit.Mvvm.ComponentModel;

namespace StripWolf.Models.Komga;

/// <summary>
/// Display model for a queued or active Komga download.
/// </summary>
public partial class KomgaDownloadQueueItem : ObservableObject
{
    public KomgaBookDisplay BookDisplay { get; init; } = new();
    public int? ServerId { get; init; }

    public string Id => BookDisplay.Id;
    public string Name => BookDisplay.Name;
    public string SeriesTitle => BookDisplay.SeriesTitle;

    [ObservableProperty]
    private bool _isQueued = true;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private bool _isCancelling;

    [ObservableProperty]
    private bool _isFailed;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string? _errorMessage;
}
