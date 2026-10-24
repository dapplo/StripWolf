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

using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace StripWolf.Core.Converters;

/// <summary>
/// Looks up an icon from the application resources (see Resources/Icons.axaml) by its resource key,
/// so a view model can pick an icon without depending on Avalonia resources itself.
/// </summary>
public static class IconConverters
{
    /// <summary>
    /// Converts a resource key such as "Icon.FitPage" to the matching <see cref="IImage"/>.
    /// </summary>
    public static readonly IValueConverter ResourceKeyToImage =
        new FuncValueConverter<string?, IImage?>(key =>
        {
            if (string.IsNullOrEmpty(key) || Application.Current is not { } app)
            {
                return null;
            }

            return app.TryGetResource(key, app.ActualThemeVariant, out var resource) ? resource as IImage : null;
        });
}
