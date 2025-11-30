using CommunityToolkit.Mvvm.ComponentModel;

namespace Kom2go.Models;

/// <summary>
/// Represents a comic that has been marked for deletion but can still be restored
/// </summary>
public partial class DeletedComic : ObservableObject
{
    /// <summary>
    /// The comic that was deleted
    /// </summary>
    public Comic Comic { get; set; } = null!;

    /// <summary>
    /// When the comic was deleted
    /// </summary>
    public DateTime DeletedAt { get; set; }

    /// <summary>
    /// Time remaining before permanent deletion (in seconds)
    /// </summary>
    [ObservableProperty]
    private int _secondsRemaining;

    /// <summary>
    /// Timer for updating the countdown
    /// </summary>
    public CancellationTokenSource? CancellationToken { get; set; }
}
