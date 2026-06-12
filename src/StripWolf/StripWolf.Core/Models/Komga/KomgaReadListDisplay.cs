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

using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StripWolf.Core.Models.Komga;

/// <summary>
/// Display model for a Komga read list with pre-loaded thumbnail
/// </summary>
public partial class KomgaReadListDisplay : ObservableObject
{
    [ObservableProperty]
    private bool _isLoaded;
    /// <summary>
    /// The underlying Komga read list data
    /// </summary>
    public KomgaReadList ReadList { get; set; } = new();

    [ObservableProperty]
    private Bitmap? _thumbnail;

    [ObservableProperty]
    private bool _isThumbnailResolved;

    // Convenience properties for binding
    public string Id => ReadList.Id;
    public string Name => ReadList.Name;
    public int BookCount => ReadList.BookIds.Count;
}
