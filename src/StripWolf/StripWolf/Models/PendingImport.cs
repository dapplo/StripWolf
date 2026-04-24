using CommunityToolkit.Mvvm.ComponentModel;

namespace StripWolf.Models;

/// <summary>
/// Represents a file that is being imported (e.g., PDF being converted)
/// </summary>
public partial class PendingImport : ObservableObject
{
    /// <summary>
    /// The file name being imported
    /// </summary>
    [ObservableProperty]
    private string _fileName = string.Empty;

    /// <summary>
    /// The original file path
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Import progress (0-1)
    /// </summary>
    [ObservableProperty]
    private double _progress;

    /// <summary>
    /// Status message
    /// </summary>
    [ObservableProperty]
    private string _status = "Waiting...";

    /// <summary>
    /// Whether the import is currently in progress
    /// </summary>
    [ObservableProperty]
    private bool _isProcessing;

    /// <summary>
    /// Whether the import completed successfully
    /// </summary>
    [ObservableProperty]
    private bool _isCompleted;

    /// <summary>
    /// Whether the import failed
    /// </summary>
    [ObservableProperty]
    private bool _isFailed;

    /// <summary>
    /// Error message if failed
    /// </summary>
    [ObservableProperty]
    private string? _errorMessage;
}
