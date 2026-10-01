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
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StripWolf.Core.Models.Komga;

/// <summary>
/// Base class for Komga display models with a lazily loaded cover thumbnail.
/// Thumbnails are only decoded while a card for the item is realized by a virtualizing ItemsRepeater,
/// and released again when the card is recycled or the item is removed. All members must be used on the UI thread,
/// except <see cref="IsThumbnailLoadCurrent"/>.
/// </summary>
public abstract partial class KomgaThumbnailDisplay : ObservableObject
{
    [ObservableProperty]
    private Bitmap? _thumbnail;

    [ObservableProperty]
    private bool _isThumbnailResolved;

    // Incremented whenever the thumbnail is released, so a load that is still in flight for the old request is discarded
    private int _thumbnailVersion;
    private bool _isThumbnailLoading;
    // Set when the server has no (decodable) thumbnail, so recycled cards don't hit the server again and again
    private bool _isThumbnailUnavailable;

    /// <summary>
    /// Number of realized ItemsRepeater elements currently showing this item
    /// </summary>
    internal int RealizedElementCount { get; set; }

    /// <summary>
    /// True when the item was removed from its collection, it will never be shown again so it must not get a new thumbnail
    /// </summary>
    internal bool IsDetached { get; private set; }

    /// <summary>
    /// Marks the start of a thumbnail load, returns false if no load is needed (already loaded, loading or unavailable)
    /// </summary>
    internal bool TryBeginThumbnailLoad(out int version)
    {
        version = _thumbnailVersion;
        if (IsDetached || Thumbnail is not null || _isThumbnailLoading || _isThumbnailUnavailable)
        {
            return false;
        }

        _isThumbnailLoading = true;
        return true;
    }

    /// <summary>
    /// Can be called from any thread, used to skip work for a request that was released in the meantime
    /// </summary>
    internal bool IsThumbnailLoadCurrent(int version) => Volatile.Read(ref _thumbnailVersion) == version;

    /// <summary>
    /// Applies the result of a load started with <see cref="TryBeginThumbnailLoad"/>.
    /// A null bitmap with <paramref name="isUnavailable"/> false (e.g. a network error) allows a retry when the card is realized again.
    /// </summary>
    internal void CompleteThumbnailLoad(int version, Bitmap? bitmap, bool isUnavailable = true)
    {
        if (version != _thumbnailVersion || IsDetached)
        {
            // Released while loading: the bitmap was never bound to anything, so it can go right away
            bitmap?.Dispose();
            return;
        }

        _isThumbnailLoading = false;
        if (bitmap is null)
        {
            _isThumbnailUnavailable = isUnavailable;
        }
        else
        {
            Thumbnail = bitmap;
        }
        IsThumbnailResolved = true;
    }

    /// <summary>
    /// Unbinds and disposes the thumbnail, a later <see cref="TryBeginThumbnailLoad"/> loads it again (from the disk cache).
    /// </summary>
    internal void ReleaseThumbnail()
    {
        Interlocked.Increment(ref _thumbnailVersion);
        _isThumbnailLoading = false;

        var bitmap = Thumbnail;
        if (bitmap is null)
        {
            return;
        }

        // Clearing the property first makes the bound Image drop the bitmap synchronously.
        // The dispose is deferred until after the next layout/render pass, disposing a bitmap that is still
        // referenced by a visual throws an ObjectDisposedException while rendering.
        Thumbnail = null;
        IsThumbnailResolved = false;
        Dispatcher.UIThread.Post(bitmap.Dispose, DispatcherPriority.Background);
    }

    /// <summary>
    /// Called when the item was removed from its collection: releases the thumbnail and prevents new loads.
    /// </summary>
    internal void Detach()
    {
        IsDetached = true;
        ReleaseThumbnail();
    }
}
