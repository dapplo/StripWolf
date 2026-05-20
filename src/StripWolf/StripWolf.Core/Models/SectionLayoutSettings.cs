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

namespace StripWolf.Core.Models;

public class SectionLayoutSettings
{
    public string Key { get; set; } = string.Empty;

    public int Order { get; set; }

    public bool IsVisible { get; set; } = true;

    public bool IsExpanded { get; set; } = true;

    public SectionLayoutSettings Clone()
    {
        return new SectionLayoutSettings
        {
            Key = Key,
            Order = Order,
            IsVisible = IsVisible,
            IsExpanded = IsExpanded
        };
    }

    public static string GetSectionLabel(string key)
    {
        return key switch
        {
            LibrarySectionKeys.ContinueReading => Resources.Loc.Instance.SectionContinueReading,
            LibrarySectionKeys.NewComics => Resources.Loc.Instance.SectionNewComics,
            LibrarySectionKeys.Favorites => Resources.Loc.Instance.SectionFavorites,
            LibrarySectionKeys.Series => Resources.Loc.Instance.SectionSeries,
            LibrarySectionKeys.Read => Resources.Loc.Instance.SectionRead,
            KomgaSectionKeys.KeepReading => Resources.Loc.Instance.SectionKeepReading,
            KomgaSectionKeys.OnDeck => Resources.Loc.Instance.SectionOnDeck,
            KomgaSectionKeys.RecentlyAddedBooks => Resources.Loc.Instance.SectionRecentlyAddedBooks,
            KomgaSectionKeys.RecentlyAddedSeries => Resources.Loc.Instance.SectionRecentlyAddedSeries,
            KomgaSectionKeys.Libraries => Resources.Loc.Instance.SectionLibraries,
            KomgaSectionKeys.ReadLists => Resources.Loc.Instance.SectionReadLists,
            _ => key
        };
    }

    public static List<SectionLayoutSettings> CreateDefaultLibrarySections()
    {
        return
        [
            new SectionLayoutSettings { Key = LibrarySectionKeys.ContinueReading, Order = 0, IsVisible = true, IsExpanded = true },
            new SectionLayoutSettings { Key = LibrarySectionKeys.NewComics, Order = 1, IsVisible = true, IsExpanded = true },
            new SectionLayoutSettings { Key = LibrarySectionKeys.Favorites, Order = 2, IsVisible = true, IsExpanded = true },
            new SectionLayoutSettings { Key = LibrarySectionKeys.Series, Order = 3, IsVisible = true, IsExpanded = true },
            new SectionLayoutSettings { Key = LibrarySectionKeys.Read, Order = 4, IsVisible = true, IsExpanded = true }
        ];
    }

    public static List<SectionLayoutSettings> CreateDefaultKomgaSections()
    {
        return
        [
            new SectionLayoutSettings { Key = KomgaSectionKeys.KeepReading, Order = 0, IsVisible = true, IsExpanded = true },
            new SectionLayoutSettings { Key = KomgaSectionKeys.OnDeck, Order = 1, IsVisible = true, IsExpanded = true },
            new SectionLayoutSettings { Key = KomgaSectionKeys.RecentlyAddedBooks, Order = 2, IsVisible = true, IsExpanded = true },
            new SectionLayoutSettings { Key = KomgaSectionKeys.RecentlyAddedSeries, Order = 3, IsVisible = true, IsExpanded = true },
            new SectionLayoutSettings { Key = KomgaSectionKeys.Libraries, Order = 4, IsVisible = true, IsExpanded = true },
            new SectionLayoutSettings { Key = KomgaSectionKeys.ReadLists, Order = 5, IsVisible = true, IsExpanded = true }
        ];
    }

    public static List<SectionLayoutSettings> MergeWithDefaults(
        IEnumerable<SectionLayoutSettings>? current,
        IReadOnlyList<SectionLayoutSettings> defaults)
    {
        var currentByKey = (current ?? [])
            .GroupBy(preference => preference.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var merged = new List<SectionLayoutSettings>(defaults.Count);

        foreach (var defaultPreference in defaults)
        {
            if (currentByKey.TryGetValue(defaultPreference.Key, out var existing))
            {
                merged.Add(new SectionLayoutSettings
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
