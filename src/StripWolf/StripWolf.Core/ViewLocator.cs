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

using Avalonia.Controls;
using Avalonia.Controls.Templates;
using StripWolf.Core.ViewModels;
using StripWolf.Core.Views;

namespace StripWolf;

/// <summary>
/// Given a view model, returns the corresponding view if possible.
/// This implementation is Native AOT compatible by avoiding reflection.
/// </summary>
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        return param switch
        {
            MainViewModel => new MainView(),
            LibraryViewModel => new LibraryView(),
            ReaderViewModel => new ReaderView(),
            KomgaViewModel => new KomgaView(),
            SettingsViewModel => new SettingsView(),
            ActivityViewModel => new ActivityView(),
            _ => CreateFallback(param)
        };
    }

    private static Control CreateFallback(object param)
    {
        var name = param.GetType().FullName?.Replace("ViewModel", "View", StringComparison.Ordinal) ?? "Unknown";
        return new TextBlock { Text = "Not Found: " + name };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
