using System.Collections.ObjectModel;

namespace StripWolf.Models;

/// <summary>
/// Represents a source-agnostic comic series grouping in the local library.
/// </summary>
public class ComicSeriesGroup
{
    public required string Name { get; init; }

    public required ObservableCollection<Comic> Comics { get; init; }

    public int ComicCount => Comics.Count;
}
