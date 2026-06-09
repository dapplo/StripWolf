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
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using StripWolf.Core.Resources;

namespace StripWolf.Core.Models;

/// <summary>
/// Represents a source-agnostic comic series group
/// </summary>
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicFields)]
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

    public bool HasDeletingComics => Comics.Any(comic => comic.IsDeleting);

    public int DeletingComicCount => Comics.Count(comic => comic.IsDeleting);

    public int DeleteCountdownSeconds => Comics
        .Where(comic => comic.IsDeleting)
        .Select(comic => comic.DeletionSecondsRemaining)
        .DefaultIfEmpty(0)
        .Max();

    public string DeleteActionLabel => !HasDeletingComics
        ? Loc.Instance.DeleteComic
        : DeletingComicCount >= ComicCount
            ? string.Format(Loc.Instance.DeleteActionUndo, DeleteCountdownSeconds)
            : string.Format(Loc.Instance.DeleteActionUndoMultiple, DeletingComicCount, DeleteCountdownSeconds);

    public string DeleteStatusText => !HasDeletingComics
        ? string.Empty
        : DeletingComicCount == 1 
            ? string.Format(Loc.Instance.DeleteStatusSingle, DeleteCountdownSeconds)
            : string.Format(Loc.Instance.DeleteStatusPlural, DeletingComicCount, DeleteCountdownSeconds);

    public string ComicCountDisplay => string.Format(Loc.Instance.ComicCountDisplay, ComicCount);
    public string ReadCountDisplay => string.Format(Loc.Instance.ReadCountDisplay, ReadCount);

    public string? CoverPath1 => RepresentativeComic?.CoverPath ?? (Comics.Count > 0 ? Comics[0].CoverPath : null);
    public string? CoverPath2 => Comics.Count > 1 ? Comics[1].CoverPath : null;
    public string? CoverPath3 => Comics.Count > 2 ? Comics[2].CoverPath : null;

    public bool HasSecondCover => Comics.Count > 1;
    public bool HasThirdCover => Comics.Count > 2;

    partial void OnComicsChanged(ObservableCollection<Comic>? oldValue, ObservableCollection<Comic> newValue)
    {
        if (oldValue is not null)
        {
            oldValue.CollectionChanged -= OnComicsCollectionChanged;
            foreach (var comic in oldValue)
            {
                comic.PropertyChanged -= OnComicPropertyChanged;
            }
        }

        newValue.CollectionChanged += OnComicsCollectionChanged;
        foreach (var comic in newValue)
        {
            comic.PropertyChanged += OnComicPropertyChanged;
        }

        RaiseComputedPropertiesChanged();
    }

    private void OnComicsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (var comic in e.OldItems.OfType<Comic>())
            {
                comic.PropertyChanged -= OnComicPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var comic in e.NewItems.OfType<Comic>())
            {
                comic.PropertyChanged += OnComicPropertyChanged;
            }
        }

        RaiseComputedPropertiesChanged();
    }

    private void OnComicPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Comic.IsDeleting) or nameof(Comic.DeletionSecondsRemaining) or nameof(Comic.IsCompleted))
        {
            RaiseComputedPropertiesChanged();
        }
    }

    private void RaiseComputedPropertiesChanged()
    {
        OnPropertyChanged(nameof(ComicCount));
        OnPropertyChanged(nameof(ReadCount));
        OnPropertyChanged(nameof(HasDeletingComics));
        OnPropertyChanged(nameof(DeletingComicCount));
        OnPropertyChanged(nameof(DeleteCountdownSeconds));
        OnPropertyChanged(nameof(DeleteActionLabel));
        OnPropertyChanged(nameof(DeleteStatusText));
        OnPropertyChanged(nameof(ComicCountDisplay));
        OnPropertyChanged(nameof(ReadCountDisplay));
        OnPropertyChanged(nameof(CoverPath1));
        OnPropertyChanged(nameof(CoverPath2));
        OnPropertyChanged(nameof(CoverPath3));
        OnPropertyChanged(nameof(HasSecondCover));
        OnPropertyChanged(nameof(HasThirdCover));
    }

    public void RefreshLocalization()
    {
        RaiseComputedPropertiesChanged();
    }
}

