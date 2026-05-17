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

using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace StripWolf.Services;

public static class EpubSnapshotHtmlHelper
{
    private static readonly Regex BaseHrefRegex = new(
        "<base\\s+[^>]*href\\s*=\\s*\"(?<href>[^\"]+)\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static Uri? TryExtractBaseUri(string htmlContent)
    {
        var match = BaseHrefRegex.Match(htmlContent);
        if (!match.Success)
        {
            return null;
        }

        var decodedHref = WebUtility.HtmlDecode(match.Groups["href"].Value);
        return Uri.TryCreate(decodedHref, UriKind.Absolute, out var baseUri) ? baseUri : null;
    }

    public static string? CreateTemporaryHtmlFile(string htmlContent)
    {
        var baseUri = TryExtractBaseUri(htmlContent);
        if (baseUri is null || !baseUri.IsFile)
        {
            return null;
        }

        var baseDirectory = Path.GetFullPath(baseUri.LocalPath);
        if (!Directory.Exists(baseDirectory))
        {
            return null;
        }

        var temporaryHtmlPath = Path.Combine(baseDirectory, $"__stripwolf_capture_{Guid.NewGuid():N}.html");
        File.WriteAllText(temporaryHtmlPath, htmlContent, new UTF8Encoding(false));
        return temporaryHtmlPath;
    }
}

