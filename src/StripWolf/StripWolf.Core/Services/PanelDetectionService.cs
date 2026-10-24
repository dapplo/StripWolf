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

using StripWolf.Core.Models;
using OpenCvSharp;

namespace StripWolf.Core.Services;

/// <summary>
/// Service for detecting comic panels (scenes) on comic pages, see <see cref="GutterPanelDetector"/>.
/// The results are cached per comic and page.
/// </summary>
public class PanelDetectionService
{
    private readonly Dictionary<string, Dictionary<(int PageIndex, bool IsManga), Task<PagePanelInfo>>> _cache = new();
    private readonly object _cacheLock = new();

    /// <summary>
    /// Guided reading needs the native OpenCV library. It is referenced by default, also on platforms where no
    /// native runtime is shipped (e.g. iOS), so check once whether it can actually be loaded instead of
    /// always returning true (guided mode then silently found no panels on those platforms).
    /// </summary>
    public bool IsAvailable => NativeOpenCvAvailable.Value;

    private static readonly Lazy<bool> NativeOpenCvAvailable = new(() =>
    {
        try
        {
            using var probe = new Mat(1, 1, MatType.CV_8UC1);
            return !probe.Empty();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PanelDetectionService: OpenCV is not available: {ex.Message}");
            return false;
        }
    });

    public Task<PagePanelInfo> DetectPanelsAsync(string comicFilePath, int pageIndex, byte[] pageData, bool isManga = false)
    {
        lock (_cacheLock)
        {
            if (!_cache.TryGetValue(comicFilePath, out var pageCache))
            {
                pageCache = new Dictionary<(int PageIndex, bool IsManga), Task<PagePanelInfo>>();
                _cache[comicFilePath] = pageCache;
            }

            // The task is cached, so the current page and the pre-detection of the next page never detect the same page twice
            if (!pageCache.TryGetValue((pageIndex, isManga), out var detection))
            {
                detection = Task.Run(() => DetectPanelsInternal(pageIndex, pageData, isManga));
                pageCache[(pageIndex, isManga)] = detection;
            }

            return detection;
        }
    }

    public async Task PreDetectPagesAsync(string comicFilePath, IEnumerable<(int pageIndex, byte[] pageData)> pages, bool isManga = false)
    {
        var tasks = pages.Select(p => DetectPanelsAsync(comicFilePath, p.pageIndex, p.pageData, isManga));
        await Task.WhenAll(tasks);
    }

    public void ClearCache(string comicFilePath)
    {
        lock (_cacheLock) { _cache.Remove(comicFilePath); }
    }

    public void ClearAllCache()
    {
        lock (_cacheLock) { _cache.Clear(); }
    }

    public bool IsCached(string comicFilePath, int pageIndex)
    {
        lock (_cacheLock)
        {
            return _cache.TryGetValue(comicFilePath, out var pageCache) &&
                   pageCache.Keys.Any(key => key.PageIndex == pageIndex);
        }
    }

    private static PagePanelInfo DetectPanelsInternal(int pageIndex, byte[] pageData, bool isManga)
    {
        try
        {
            var panels = GutterPanelDetector.Detect(pageIndex, pageData, isManga, findInsets: true);
            if (panels.Count == 0)
            {
                return CreateSplashPageResult(pageIndex);
            }

            return new PagePanelInfo
            {
                PageIndex = pageIndex,
                Panels = panels,
                DetectionSuccessful = true,
                IsSplashPage = false
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PanelDetectionService: detection of page {pageIndex} failed: {ex.Message}");
            return CreateSplashPageResult(pageIndex, 0.5);
        }
    }

    private static PagePanelInfo CreateSplashPageResult(int pageIndex, double confidence = 1.0)
    {
        return new PagePanelInfo
        {
            PageIndex = pageIndex,
            DetectionSuccessful = true,
            IsSplashPage = true,
            Panels = new List<ComicPanel>
            {
                new ComicPanel
                {
                    PageIndex = pageIndex, PanelIndex = 0,
                    X = 0, Y = 0, Width = 1, Height = 1,
                    Confidence = confidence
                }
            }
        };
    }
}
