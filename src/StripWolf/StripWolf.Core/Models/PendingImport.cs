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

using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StripWolf.Core.Models;

/// <summary>
/// Represents a queued or active local import/conversion task.
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
    /// The storage file object if importing from cloud/external folders
    /// </summary>
    public IStorageFile? StorageFile { get; set; }

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

