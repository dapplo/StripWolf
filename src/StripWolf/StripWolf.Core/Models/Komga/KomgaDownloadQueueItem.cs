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

