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
using StripWolf.Core.Models;

namespace StripWolf.Core.ViewModels;

public partial class SectionLayoutItemViewModel : ObservableObject
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
    private string _label = string.Empty;

    public SectionLayoutItemViewModel()
    {
    }

    public SectionLayoutItemViewModel(string key)
    {
        Key = key;
        RefreshLocalization();
    }

    public void Apply(SectionLayoutSettings settings)
    {
        Key = settings.Key;
        Order = settings.Order;
        IsVisible = settings.IsVisible;
        IsExpanded = settings.IsExpanded;
        RefreshLocalization();
    }

    public void RefreshLocalization()
    {
        Label = SectionLayoutSettings.GetSectionLabel(Key);
    }

    public SectionLayoutSettings ToSettings()
    {
        return new SectionLayoutSettings
        {
            Key = Key,
            Order = Order,
            IsVisible = IsVisible,
            IsExpanded = IsExpanded
        };
    }
}
