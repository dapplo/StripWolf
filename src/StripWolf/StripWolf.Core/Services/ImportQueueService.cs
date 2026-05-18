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
using Avalonia.Threading;
using StripWolf.Models;

namespace StripWolf.Services;

public class ImportQueueService
{
    public ObservableCollection<PendingImport> PendingImports { get; } = [];

    public Task EnqueueAsync(PendingImport pendingImport)
    {
        ArgumentNullException.ThrowIfNull(pendingImport);

        if (Dispatcher.UIThread.CheckAccess())
        {
            PendingImports.Add(pendingImport);
            return Task.CompletedTask;
        }

        return Dispatcher.UIThread.InvokeAsync(() => PendingImports.Add(pendingImport)).GetTask();
    }

    public Task RemoveAsync(PendingImport pendingImport)
    {
        ArgumentNullException.ThrowIfNull(pendingImport);

        if (Dispatcher.UIThread.CheckAccess())
        {
            PendingImports.Remove(pendingImport);
            return Task.CompletedTask;
        }

        return Dispatcher.UIThread.InvokeAsync(() => PendingImports.Remove(pendingImport)).GetTask();
    }
}

