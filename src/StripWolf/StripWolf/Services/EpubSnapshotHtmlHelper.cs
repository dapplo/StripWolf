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
