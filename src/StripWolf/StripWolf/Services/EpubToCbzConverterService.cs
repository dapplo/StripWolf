using System.IO.Compression;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Serialization;
using Avalonia;
using Avalonia.Styling;
using StripWolf.Models;
using VersOne.Epub;
using VersOne.Epub.Schema;

namespace StripWolf.Services;

/// <summary>
/// Converts EPUB books to CBZ by rendering paginated HTML chapters with a platform-native off-screen WebView.
/// </summary>
public sealed class EpubToCbzConverterService
{
    private const int DefaultViewportWidth = 700;
    private const int DefaultViewportHeight = 1050;
    private const string PaginationCss = "body { height: 100vh; overflow: hidden; }";

    private const string PaginationScriptBody = """
        window.__stripWolfReady = false;
        window.__stripWolfPageCount = 1;
        window.__stripWolfGetPageCount = () => window.__stripWolfPageCount;
        (() => {
            const notifyReady = () => {
                window.__stripWolfReady = true;
                try {
                    if (window.chrome?.webview?.postMessage) {
                        window.chrome.webview.postMessage('stripwolf-ready');
                    }
                } catch {
                }
                try {
                    if (typeof window.StripWolfBridge?.onPaginationReady === 'function') {
                        window.StripWolfBridge.onPaginationReady('stripwolf-ready');
                    }
                } catch {
                }
            };
            const scheduleReady = () => {
                requestAnimationFrame(() => requestAnimationFrame(notifyReady));
            };
            const waitForAssets = async () => {
                const images = Array.from(document.images || []);
                await Promise.all(images.map(image => {
                    if (image.complete) {
                        return Promise.resolve();
                    }
                    return new Promise(resolve => {
                        image.addEventListener('load', resolve, { once: true });
                        image.addEventListener('error', resolve, { once: true });
                    });
                }));
                if (document.fonts && document.fonts.ready) {
                    try {
                        await document.fonts.ready;
                    } catch {
                    }
                }
            };
            const markSingleVisualPage = () => {
                const body = document.body;
                if (!body) {
                    return;
                }

                const directChildren = Array.from(body.children);
                if (directChildren.length === 1) {
                    const onlyChild = directChildren[0];
                    const onlyChildText = (onlyChild.textContent || '').trim();
                    const imageCount = onlyChild.querySelectorAll('img, svg').length;
                    if (imageCount === 1 && onlyChildText.length === 0) {
                        body.classList.add('stripwolf-single-visual-page');
                    }
                }

                if (directChildren.length === 1 && /^(img|svg)$/i.test(directChildren[0].tagName)) {
                    body.classList.add('stripwolf-single-visual-page');
                }

                if (!body.classList.contains('stripwolf-single-visual-page')) {
                    body.classList.add('stripwolf-reading-page');
                }
            };
            const wrapReadingContent = () => {
                const body = document.body;
                if (!body || body.querySelector(':scope > .stripwolf-page-viewport')) {
                    return;
                }

                const pageViewport = document.createElement('div');
                pageViewport.className = 'stripwolf-page-viewport';

                const contentWrapper = document.createElement('div');
                contentWrapper.className = body.classList.contains('stripwolf-reading-page')
                    ? 'stripwolf-reading-content'
                    : 'stripwolf-visual-content';

                while (body.firstChild) {
                    contentWrapper.appendChild(body.firstChild);
                }

                pageViewport.appendChild(contentWrapper);
                body.appendChild(pageViewport);
            };
            const settlePageOffset = (pageScroller, targetOffset, remainingFrames = 12) => {
                const currentOffset = Math.abs(pageScroller.scrollLeft || 0);
                if (Math.abs(currentOffset - targetOffset) <= 1 || remainingFrames <= 0) {
                    scheduleReady();
                    return;
                }

                pageScroller.scrollLeft = targetOffset;
                requestAnimationFrame(() => settlePageOffset(pageScroller, targetOffset, remainingFrames - 1));
            };
            const computePagination = () => {
                const pageViewport = document.querySelector(':scope body > .stripwolf-page-viewport') || document.querySelector('body > .stripwolf-page-viewport');
                const pageContent = pageViewport?.firstElementChild;
                const forcedViewportWidth = Number(window.__stripWolfPaginationViewportWidth) || 0;
                const viewportWidth = forcedViewportWidth > 0
                    ? forcedViewportWidth
                    : Math.max(pageViewport?.clientWidth || 0, window.innerWidth || 0, document.documentElement.clientWidth || 0, 1);
                if (!pageViewport || !pageContent) {
                    return { pageCount: 1, maxScrollLeft: 0, pageScroller: null, viewportWidth };
                }

                const totalScrollWidth = Math.max(
                    pageContent.scrollWidth || 0,
                    viewportWidth);
                const maxScrollLeft = Math.max(0, totalScrollWidth - (pageContent.clientWidth || viewportWidth));
                const trailingOverflowTolerance = 32;
                const pageCount = maxScrollLeft <= trailingOverflowTolerance
                    ? 1
                    : Math.ceil((maxScrollLeft - trailingOverflowTolerance) / viewportWidth) + 1;
                return { pageCount, maxScrollLeft, pageScroller: pageContent, viewportWidth };
            };
            window.__stripWolfSetPage = (requestedPageIndex) => {
                const pagination = computePagination();
                const pageCount = pagination.pageCount || 1;
                window.__stripWolfPageCount = pageCount;
                window.__stripWolfGetPageCount = () => pageCount;
                const safePageIndex = Math.max(0, Math.min(Number(requestedPageIndex) || 0, pageCount - 1));
                if (!pagination.pageScroller) {
                    scheduleReady();
                    return safePageIndex;
                }

                const targetScrollLeft = Math.min(safePageIndex * pagination.viewportWidth, pagination.maxScrollLeft);
                window.__stripWolfReady = false;
                pagination.pageScroller.scrollLeft = targetScrollLeft;
                requestAnimationFrame(() => settlePageOffset(pagination.pageScroller, targetScrollLeft));
                return safePageIndex;
            };
            window.addEventListener('load', async () => {
                await waitForAssets();
                markSingleVisualPage();
                wrapReadingContent();
                requestAnimationFrame(() => requestAnimationFrame(() => window.__stripWolfSetPage(0)));
            }, { once: true });
        })();
        """;

    private static readonly Regex HeadTagRegex = new("<head\\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HtmlTagRegex = new("<html\\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ScriptTagRegex = new("<script\\b[^>]*>[\\s\\S]*?</script\\s*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DangerousTagRegex = new("<(?:iframe|object|embed|applet|form)\\b[^>]*>[\\s\\S]*?</(?:iframe|object|embed|applet|form)\\s*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SelfClosingDangerousTagRegex = new("<(?:iframe|object|embed|applet|form)\\b[^>]*/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex EventHandlerAttributeRegex = new("\\s+on[\\w:-]+\\s*=\\s*(?:\"[^\"]*\"|'[^']*'|[^\\s>]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex JavascriptUriAttributeRegex = new("(\\s+(?:href|src|xlink:href|action|formaction|poster)\\s*=\\s*)(?<quote>['\"]?)(?<value>[^'\"\\s>]*)(?:\\k<quote>)?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MetaRefreshTagRegex = new("<meta\\b[^>]*http-equiv\\s*=\\s*(['\"]?)refresh\\1[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IWebViewPaginationService _webViewPaginationService;
    private readonly SettingsService _settingsService;

    public EpubToCbzConverterService(IWebViewPaginationService webViewPaginationService, SettingsService settingsService)
    {
        _webViewPaginationService = webViewPaginationService;
        _settingsService = settingsService;
    }

    public async Task<string> ConvertEpubToCbzAsync(
        string epubFilePath,
        string outputDirectory,
        int viewportWidth = DefaultViewportWidth,
        int viewportHeight = DefaultViewportHeight,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(epubFilePath))
        {
            throw new FileNotFoundException("EPUB file not found.", epubFilePath);
        }

        if (!Path.GetExtension(epubFilePath).Equals(".epub", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("Only .epub files can be converted by the EPUB converter.");
        }

        Directory.CreateDirectory(outputDirectory);

        var cbzPath = Path.Combine(outputDirectory, $"{Path.GetFileNameWithoutExtension(epubFilePath)}.cbz");
        if (File.Exists(cbzPath))
        {
            File.Delete(cbzPath);
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), $"StripWolf_EPUB_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var settings = _settingsService.LoadSettings();
            var conversionTheme = ResolveConversionTheme(settings.EpubConversionTheme);
            var renderScale = ResolveRenderScale(settings.EpubOutputResolution);
            var book = await EpubReader.ReadBookAsync(epubFilePath);
            await MaterializeContentAsync(book, tempRoot, cancellationToken);

            var totalPages = 0;

            using var archive = ZipFile.Open(cbzPath, ZipArchiveMode.Create);
            await using var paginationSession = await _webViewPaginationService.CreatePaginationSessionAsync(
                viewportWidth,
                viewportHeight,
                renderScale);

            var renderedPageIndex = 0;
            var totalChapterCount = Math.Max(book.ReadingOrder.Count, 1);
            for (var chapterIndex = 0; chapterIndex < book.ReadingOrder.Count; chapterIndex++)
            {
                var chapter = book.ReadingOrder[chapterIndex];
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(chapter.Content))
                {
                    continue;
                }

                var chapterRelativePath = GetChapterRelativePath(chapter);
                var baseUri = CreateChapterBaseUri(tempRoot, chapterRelativePath);
                await paginationSession.LoadHtmlAsync(BuildPaginatedHtml(chapter.Content, baseUri, conversionTheme));

                var pageCount = await paginationSession.GetPageCountAsync();
                if (pageCount <= 0)
                {
                    continue;
                }

                totalPages += pageCount;

                for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var entry = archive.CreateEntry($"Page_{renderedPageIndex + 1:000}.png", CompressionLevel.NoCompression);
                    await using var entryStream = entry.Open();
                    await paginationSession.CapturePageToStreamAsync(pageIndex, entryStream);

                    renderedPageIndex++;
                    progress?.Report((chapterIndex + ((double)pageIndex + 1) / pageCount) / totalChapterCount);
                }
            }

            var comicInfoXml = CreateComicInfoXml(book, totalPages);
            var comicInfoEntry = archive.CreateEntry("ComicInfo.xml", CompressionLevel.Optimal);
            await using (var comicInfoStream = comicInfoEntry.Open())
            await using (var comicInfoWriter = new StreamWriter(comicInfoStream, new UTF8Encoding(false)))
            {
                await comicInfoWriter.WriteAsync(comicInfoXml);
            }

            progress?.Report(1);
            return cbzPath;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static async Task MaterializeContentAsync(EpubBook book, string rootDirectory, CancellationToken cancellationToken)
    {
        foreach (var contentFile in book.Content.AllFiles.Local)
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (contentFile)
            {
                case EpubLocalTextContentFile textContentFile:
                    await WriteLocalContentFileAsync(rootDirectory, textContentFile.Key, textContentFile.FilePath, async outputPath =>
                        await File.WriteAllTextAsync(outputPath, textContentFile.Content, new UTF8Encoding(false), cancellationToken));
                    break;

                case EpubLocalByteContentFile byteContentFile:
                    await WriteLocalContentFileAsync(rootDirectory, byteContentFile.Key, byteContentFile.FilePath, async outputPath =>
                        await File.WriteAllBytesAsync(outputPath, byteContentFile.Content, cancellationToken));
                    break;
            }
        }
    }

    private static async Task WriteLocalContentFileAsync(
        string rootDirectory,
        string key,
        string? filePath,
        Func<string, Task> writeAsync)
    {
        foreach (var candidatePath in GetCandidatePaths(key, filePath))
        {
            var outputPath = ResolveOutputPath(rootDirectory, candidatePath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await writeAsync(outputPath);
        }
    }

    private static string BuildPaginatedHtml(string htmlContent, Uri baseUri, EpubConversionTheme conversionTheme)
    {
        var sanitizedHtml = SanitizeChapterHtml(htmlContent);
        var injection = BuildHeadInjection(baseUri, conversionTheme);

        if (HeadTagRegex.IsMatch(sanitizedHtml))
        {
            return HeadTagRegex.Replace(sanitizedHtml, match => $"{match.Value}{injection}", 1);
        }

        if (HtmlTagRegex.IsMatch(sanitizedHtml))
        {
            return HtmlTagRegex.Replace(sanitizedHtml, match => $"{match.Value}<head>{injection}</head>", 1);
        }

        return $"""
            <html>
            <head>
            {injection}
            </head>
            <body>
            {sanitizedHtml}
            </body>
            </html>
            """;
    }

    private static string BuildHeadInjection(Uri baseUri, EpubConversionTheme conversionTheme)
    {
        var escapedBaseUri = SecurityElement.Escape(baseUri.AbsoluteUri) ?? baseUri.AbsoluteUri;
        var backgroundColor = conversionTheme == EpubConversionTheme.Dark ? "#000000" : "#ffffff";
        var foregroundColor = conversionTheme == EpubConversionTheme.Dark ? "#f5f5f5" : "#111111";
        var mutedForegroundColor = conversionTheme == EpubConversionTheme.Dark ? "#d0d0d0" : "#333333";
        var scriptNonce = Convert.ToHexString(Guid.NewGuid().ToByteArray());
        var csp = BuildContentSecurityPolicy(scriptNonce);

        return
            $"<base href=\"{escapedBaseUri}\" />{Environment.NewLine}" +
            "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1, maximum-scale=1, user-scalable=no\" />" + Environment.NewLine +
            $"<meta http-equiv=\"Content-Security-Policy\" content=\"{SecurityElement.Escape(csp)}\" />" + Environment.NewLine +
            "<style id=\"stripwolf-pagination-style\">" + Environment.NewLine +
            "* { box-sizing: border-box; }" + Environment.NewLine +
            $"html {{ margin: 0; padding: 0; width: 100vw; height: 100vh; overflow: hidden; background: {backgroundColor}; color: {foregroundColor}; }}" + Environment.NewLine +
            $"body {{ margin: 0; padding: 0; width: 100vw; background: {backgroundColor}; color: {foregroundColor}; }}" + Environment.NewLine +
            PaginationCss + Environment.NewLine +
            $"body.stripwolf-reading-page {{ font-size: 1.256em; line-height: 1.55; color: {foregroundColor}; background: {backgroundColor}; margin: 0 !important; padding: 0 !important; }}" + Environment.NewLine +
            "body > .stripwolf-page-viewport { position: relative; width: 100vw; height: 100vh; overflow: hidden; }" + Environment.NewLine +
            "body.stripwolf-reading-page > .stripwolf-page-viewport > .stripwolf-reading-content { width: 100vw; height: 100vh; padding: 3vh 6vw !important; overflow-x: auto; overflow-y: hidden; scrollbar-width: none; -ms-overflow-style: none; column-width: calc(100vw - 12vw); column-gap: 12vw; column-fill: auto; -webkit-column-width: calc(100vw - 12vw); -webkit-column-gap: 12vw; -webkit-column-fill: auto; }" + Environment.NewLine +
            "body.stripwolf-reading-page > .stripwolf-page-viewport > .stripwolf-reading-content::-webkit-scrollbar { display: none; }" + Environment.NewLine +
            "body.stripwolf-reading-page > .stripwolf-page-viewport > .stripwolf-reading-content > :is(div, section, article, main, aside, header, footer, figure, nav) { margin-left: 0 !important; margin-right: 0 !important; padding-left: 0 !important; padding-right: 0 !important; max-width: 100% !important; }" + Environment.NewLine +
            "body.stripwolf-reading-page > .stripwolf-page-viewport > .stripwolf-reading-content > :is(div, section, article, main, aside, header, footer, figure, nav) > :is(div, section, article, main, aside, header, footer, figure, nav) { margin-left: 0 !important; margin-right: 0 !important; max-width: 100% !important; }" + Environment.NewLine +
            $"body.stripwolf-reading-page p, body.stripwolf-reading-page li, body.stripwolf-reading-page div, body.stripwolf-reading-page blockquote {{ color: {foregroundColor}; }}" + Environment.NewLine +
            $"body.stripwolf-reading-page h1, body.stripwolf-reading-page h2, body.stripwolf-reading-page h3, body.stripwolf-reading-page h4, body.stripwolf-reading-page h5, body.stripwolf-reading-page h6 {{ color: {foregroundColor}; margin-top: 0; }}" + Environment.NewLine +
            $"body.stripwolf-reading-page small, body.stripwolf-reading-page figcaption {{ color: {mutedForegroundColor}; }}" + Environment.NewLine +
            $"body.stripwolf-reading-page img, body.stripwolf-reading-page svg {{ margin: 0.5em auto 0.75em; max-height: calc(100vh - 8vh); }}" + Environment.NewLine +
            "img, svg, video, canvas { display: block; max-width: 100%; max-height: 100vh; height: auto; break-inside: avoid-column; page-break-inside: avoid; }" + Environment.NewLine +
            $"a {{ color: {foregroundColor}; }}" + Environment.NewLine +
            $"body.stripwolf-single-visual-page {{ background: {backgroundColor}; }}" + Environment.NewLine +
            "body.stripwolf-single-visual-page > .stripwolf-page-viewport > .stripwolf-visual-content { display: flex; align-items: center; justify-content: center; width: 100vw; min-height: 100vh; }" + Environment.NewLine +
            "body.stripwolf-single-visual-page img, body.stripwolf-single-visual-page svg { width: 100%; height: 100vh; object-fit: contain; }" + Environment.NewLine +
            "</style>" + Environment.NewLine +
            $"<script nonce=\"{scriptNonce}\">{Environment.NewLine}{PaginationScriptBody}{Environment.NewLine}</script>";
    }

    private static string SanitizeChapterHtml(string htmlContent)
    {
        var sanitizedHtml = ScriptTagRegex.Replace(htmlContent, string.Empty);
        sanitizedHtml = DangerousTagRegex.Replace(sanitizedHtml, string.Empty);
        sanitizedHtml = SelfClosingDangerousTagRegex.Replace(sanitizedHtml, string.Empty);
        sanitizedHtml = MetaRefreshTagRegex.Replace(sanitizedHtml, string.Empty);
        sanitizedHtml = EventHandlerAttributeRegex.Replace(sanitizedHtml, string.Empty);
        sanitizedHtml = JavascriptUriAttributeRegex.Replace(sanitizedHtml, match =>
        {
            var value = match.Groups["value"].Value.TrimStart();
            return value.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : match.Value;
        });

        return sanitizedHtml;
    }

    private static string BuildContentSecurityPolicy(string scriptNonce)
    {
        return
            "default-src 'none'; " +
            "base-uri 'self' file: data: blob:; " +
            "img-src file: data: blob:; " +
            "style-src 'unsafe-inline' file: data: blob:; " +
            "font-src file: data: blob:; " +
            "media-src file: data: blob:; " +
            "object-src 'none'; " +
            "frame-src 'none'; " +
            "child-src 'none'; " +
            "connect-src 'none'; " +
            "manifest-src 'none'; " +
            "worker-src 'none'; " +
            "form-action 'none'; " +
            $"script-src 'nonce-{scriptNonce}'";
    }

    private static EpubConversionTheme ResolveConversionTheme(EpubConversionTheme configuredTheme)
    {
        if (configuredTheme != EpubConversionTheme.System)
        {
            return configuredTheme;
        }

        return Application.Current?.ActualThemeVariant == ThemeVariant.Dark
            ? EpubConversionTheme.Dark
            : EpubConversionTheme.Light;
    }

    private static double ResolveRenderScale(EpubOutputResolution outputResolution)
    {
        return outputResolution switch
        {
            EpubOutputResolution.Medium => 2,
            EpubOutputResolution.High => 3,
            _ => 1
        };
    }

    private static Uri CreateChapterBaseUri(string rootDirectory, string chapterKey)
    {
        var normalizedKey = NormalizeRelativePath(chapterKey);
        var chapterDirectory = Path.GetDirectoryName(normalizedKey);
        var absoluteDirectory = string.IsNullOrEmpty(chapterDirectory)
            ? rootDirectory
            : Path.Combine(rootDirectory, chapterDirectory);

        Directory.CreateDirectory(absoluteDirectory);
        return new Uri(absoluteDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar);
    }

    private static string ResolveOutputPath(string rootDirectory, string relativePath)
    {
        var normalizedRelativePath = NormalizeRelativePath(relativePath);
        var combinedPath = Path.GetFullPath(Path.Combine(rootDirectory, normalizedRelativePath));
        var fullRootPath = Path.GetFullPath(rootDirectory);

        if (!combinedPath.StartsWith(fullRootPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"EPUB content path '{relativePath}' resolves outside of the temporary extraction root.");
        }

        return combinedPath;
    }

    private static string NormalizeRelativePath(string path)
    {
        var normalized = Uri.UnescapeDataString(path)
            .Replace('/', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return normalized;
    }

    private static IEnumerable<string> GetCandidatePaths(string key, string? filePath)
    {
        yield return key;

        if (!string.IsNullOrWhiteSpace(filePath) &&
            !string.Equals(filePath, key, StringComparison.OrdinalIgnoreCase))
        {
            yield return filePath;
        }
    }

    private static string GetChapterRelativePath(EpubLocalTextContentFile chapter)
    {
        return !string.IsNullOrWhiteSpace(chapter.FilePath)
            ? chapter.FilePath
            : chapter.Key;
    }

    private static string CreateComicInfoXml(EpubBook book, int pageCount)
    {
        var metadata = book.Schema.Package.Metadata;
        var seriesName = ExtractSeriesName(metadata);
        var seriesIndex = ExtractSeriesIndex(metadata);
        var authors = book.AuthorList?.Where(author => !string.IsNullOrWhiteSpace(author)).ToList() ?? [];

        var comicInfo = new ComicInfo
        {
            Title = book.Title,
            Series = seriesName,
            Number = seriesIndex,
            Writer = authors.Count > 0 ? string.Join(", ", authors) : book.Author,
            Summary = book.Description,
            PageCount = pageCount > 0 ? pageCount : null,
            Notes = $"Converted from EPUB with off-screen native WebView pagination."
        };

        var serializer = new XmlSerializer(typeof(ComicInfo));
        var namespaces = new XmlSerializerNamespaces();
        namespaces.Add("", "");

        var settings = new XmlWriterSettings
        {
            Indent = true,
            Encoding = new UTF8Encoding(false),
            OmitXmlDeclaration = false
        };

        using var stringWriter = new Utf8StringWriter();
        using var xmlWriter = XmlWriter.Create(stringWriter, settings);
        serializer.Serialize(xmlWriter, comicInfo, namespaces);
        return stringWriter.ToString();
    }

    private static string? ExtractSeriesName(EpubMetadata metadata)
    {
        var explicitSeries = metadata.MetaItems.FirstOrDefault(meta =>
            string.Equals(meta.Name, "calibre:series", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(meta.Name, "series", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(explicitSeries?.Content))
        {
            return explicitSeries.Content;
        }

        var collectionMeta = metadata.MetaItems.FirstOrDefault(meta =>
            string.Equals(meta.Property, "belongs-to-collection", StringComparison.OrdinalIgnoreCase));

        return string.IsNullOrWhiteSpace(collectionMeta?.Content) ? null : collectionMeta.Content;
    }

    private static string? ExtractSeriesIndex(EpubMetadata metadata)
    {
        var explicitSeriesIndex = metadata.MetaItems.FirstOrDefault(meta =>
            string.Equals(meta.Name, "calibre:series_index", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(meta.Name, "series_index", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(explicitSeriesIndex?.Content))
        {
            return explicitSeriesIndex.Content;
        }

        var collectionMeta = metadata.MetaItems.FirstOrDefault(meta =>
            string.Equals(meta.Property, "belongs-to-collection", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(meta.Id));
        if (collectionMeta is null || string.IsNullOrWhiteSpace(collectionMeta.Id))
        {
            return null;
        }

        var refinedIndex = metadata.MetaItems.FirstOrDefault(meta =>
            string.Equals(meta.Refines, $"#{collectionMeta.Id}", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(meta.Property, "group-position", StringComparison.OrdinalIgnoreCase));

        return string.IsNullOrWhiteSpace(refinedIndex?.Content) ? null : refinedIndex.Content;
    }

    private sealed class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding => new UTF8Encoding(false);
    }
}
