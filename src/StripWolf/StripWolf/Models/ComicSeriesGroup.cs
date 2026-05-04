using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StripWolf.Models;

/// <summary>
/// Represents a source-agnostic comic series grouping in the local library.
/// </summary>
public partial class ComicSeriesGroup : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private ObservableCollection<Comic> _comics = [];

    [ObservableProperty]
    private Comic? _representativeComic;

    [ObservableProperty]
    private bool _isExpanded;

    public int ComicCount => Comics.Count;

    public int ReadCount => Comics.Count(comic => comic.IsCompleted);
}
