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

using StripWolf.Core.Services;

namespace StripWolf.PanelBench;

/// <summary>
/// A panel detection implementation which can be measured, so a new implementation can be compared with the current one.
/// </summary>
public interface IPanelDetector
{
    string Name { get; }

    /// <summary>
    /// Detects the panels of a page in reading order, normalized to 0-1. A splash page is one panel covering the page.
    /// </summary>
    /// <param name="log">Optional, receives the decisions of detectors which support it</param>
    Task<List<Box>> DetectAsync(byte[] pageData, bool isManga, Action<string>? log = null);
}

/// <summary>
/// The PanelDetectionService as used by the app.
/// </summary>
public sealed class ServicePanelDetector : IPanelDetector
{
    private readonly PanelDetectionService _service = new();

    public string Name => "service";

    public async Task<List<Box>> DetectAsync(byte[] pageData, bool isManga, Action<string>? log = null)
    {
        // A unique key, the service caches per comic and page
        var result = await _service.DetectPanelsAsync(Guid.NewGuid().ToString("N"), 0, pageData, isManga);
        _service.ClearAllCache();
        return result.Panels
            .Select(panel => new Box(panel.X, panel.Y, panel.Width, panel.Height))
            .ToList();
    }
}

/// <summary>
/// The gutter split (XY-cut) detection, a splash page when it finds no panel structure.
/// </summary>
/// <param name="findInsets">Also search panels drawn over a background panel, as the app does</param>
public sealed class GutterPanelDetectorAdapter(bool findInsets = false) : IPanelDetector
{
    public string Name => findInsets ? "gutter-insets" : "gutter";

    public Task<List<Box>> DetectAsync(byte[] pageData, bool isManga, Action<string>? log = null) => Task.Run(() =>
    {
        var panels = GutterPanelDetector.Detect(0, pageData, isManga, log, findInsets);
        return panels.Count == 0
            ? [new Box(0, 0, 1, 1)]
            : panels.Select(panel => new Box(panel.X, panel.Y, panel.Width, panel.Height)).ToList();
    });
}

public static class Detectors
{
    public static readonly string[] Names = ["service", "gutter", "gutter-insets"];

    public static IPanelDetector Create(string name) => name.ToLowerInvariant() switch
    {
        "service" => new ServicePanelDetector(),
        "gutter" => new GutterPanelDetectorAdapter(),
        "gutter-insets" => new GutterPanelDetectorAdapter(findInsets: true),
        _ => throw new ArgumentException($"Unknown detector '{name}', available: {string.Join(", ", Names)}")
    };
}
