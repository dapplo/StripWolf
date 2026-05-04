using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StripWolf.Models;

public static class LibrarySectionKeys
{
    public const string ContinueReading = "continue-reading";
    public const string NewComics = "new-comics";
    public const string Favorites = "favorites";
    public const string Series = "series";
    public const string Read = "read";
}

public static class KomgaSectionKeys
{
    public const string KeepReading = "keep-reading";
    public const string OnDeck = "on-deck";
    public const string RecentlyAddedBooks = "recently-added-books";
    public const string RecentlyAddedSeries = "recently-added-series";
    public const string Libraries = "libraries";
    public const string ReadLists = "read-lists";
}

public partial class SectionLayoutPreference : ObservableObject
{
    [ObservableProperty]
    private string _key = string.Empty;

    [ObservableProperty]
    private int _order;

    [ObservableProperty]
    private bool _isVisible = true;

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    [JsonIgnore]
    private string _label = string.Empty;

    public SectionLayoutPreference Clone()
    {
        return new SectionLayoutPreference
        {
            Key = Key,
            Order = Order,
            IsVisible = IsVisible,
            IsExpanded = IsExpanded,
            Label = Label
        };
    }

    public static List<SectionLayoutPreference> CreateDefaultLibrarySections()
    {
        return
        [
            new SectionLayoutPreference { Key = LibrarySectionKeys.ContinueReading, Order = 0, IsVisible = true, IsExpanded = true },
            new SectionLayoutPreference { Key = LibrarySectionKeys.NewComics, Order = 1, IsVisible = true, IsExpanded = true },
            new SectionLayoutPreference { Key = LibrarySectionKeys.Favorites, Order = 2, IsVisible = true, IsExpanded = true },
            new SectionLayoutPreference { Key = LibrarySectionKeys.Series, Order = 3, IsVisible = true, IsExpanded = true },
            new SectionLayoutPreference { Key = LibrarySectionKeys.Read, Order = 4, IsVisible = true, IsExpanded = true }
        ];
    }

    public static List<SectionLayoutPreference> CreateDefaultKomgaSections()
    {
        return
        [
            new SectionLayoutPreference { Key = KomgaSectionKeys.KeepReading, Order = 0, IsVisible = true, IsExpanded = true },
            new SectionLayoutPreference { Key = KomgaSectionKeys.OnDeck, Order = 1, IsVisible = true, IsExpanded = true },
            new SectionLayoutPreference { Key = KomgaSectionKeys.RecentlyAddedBooks, Order = 2, IsVisible = true, IsExpanded = true },
            new SectionLayoutPreference { Key = KomgaSectionKeys.RecentlyAddedSeries, Order = 3, IsVisible = true, IsExpanded = true },
            new SectionLayoutPreference { Key = KomgaSectionKeys.Libraries, Order = 4, IsVisible = true, IsExpanded = true },
            new SectionLayoutPreference { Key = KomgaSectionKeys.ReadLists, Order = 5, IsVisible = true, IsExpanded = true }
        ];
    }

    public static List<SectionLayoutPreference> MergeWithDefaults(
        IEnumerable<SectionLayoutPreference>? current,
        IReadOnlyList<SectionLayoutPreference> defaults)
    {
        var currentByKey = (current ?? [])
            .GroupBy(preference => preference.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var merged = new List<SectionLayoutPreference>(defaults.Count);

        foreach (var defaultPreference in defaults)
        {
            if (currentByKey.TryGetValue(defaultPreference.Key, out var existing))
            {
                merged.Add(new SectionLayoutPreference
                {
                    Key = defaultPreference.Key,
                    Order = existing.Order,
                    IsVisible = existing.IsVisible,
                    IsExpanded = existing.IsExpanded
                });
            }
            else
            {
                merged.Add(defaultPreference.Clone());
            }
        }

        var normalized = merged
            .OrderBy(preference => preference.Order)
            .ThenBy(preference => defaults.ToList().FindIndex(defaultPreference => string.Equals(defaultPreference.Key, preference.Key, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        for (var index = 0; index < normalized.Count; index++)
        {
            normalized[index].Order = index;
        }

        return normalized;
    }
}
