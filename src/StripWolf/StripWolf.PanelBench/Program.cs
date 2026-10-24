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

using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using OpenCvSharp;
using StripWolf.PanelBench;

const string TestDataVariable = "STRIPWOLF_PANEL_TESTDATA";

string? folder = null;
string? outDir = null;
string detectorName = "service";
bool runStability = false;
bool isManga = false;
bool debug = false;
int maxPages = int.MaxValue;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--out": outDir = args[++i]; break;
        case "--detector": detectorName = args[++i]; break;
        case "--stability": runStability = true; break;
        case "--manga": isManga = true; break;
        case "--debug": debug = true; break;
        case "--max-pages": maxPages = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "-h" or "--help": PrintUsage(); return 0;
        default: folder = args[i]; break;
    }
}

folder ??= Environment.GetEnvironmentVariable(TestDataVariable);
if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
{
    Console.Error.WriteLine($"No test comics folder, pass it as argument or set {TestDataVariable}.");
    PrintUsage();
    return 1;
}

outDir ??= Path.Combine(folder, "_panelbench");
Directory.CreateDirectory(outDir);

var detector = Detectors.Create(detectorName);
var comics = Directory.EnumerateFiles(folder)
    .Where(path => ComicArchive.SupportedExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
    .ToList();

Console.WriteLine($"Detector '{detector.Name}', {comics.Count} comic(s) in {folder}");

var scores = new List<PageScore>();
var stability = new List<(string Comic, string Page, string Variant, bool Stable)>();
var detectionTimes = new List<double>();
int totalPages = 0;
int splashPages = 0;
int totalPanels = 0;

foreach (var comicPath in comics)
{
    var comicName = Path.GetFileName(comicPath);
    Console.WriteLine($"- {comicName}");
    var pages = ComicArchive.ReadPages(comicPath).Take(maxPages).ToList();
    var comicOut = Path.Combine(outDir, SafeName(Path.GetFileNameWithoutExtension(comicPath)));
    var labels = LabelFile.TryLoad(comicPath, comicOut);
    var labelsByName = labels?.Pages.ToDictionary(page => page.Name) ?? new Dictionary<string, PageLabel>();

    var pagesOut = Path.Combine(comicOut, "pages");
    Directory.CreateDirectory(pagesOut);

    var debugLog = debug ? new StringBuilder() : null;
    var editorPages = new List<object>();
    var detectedPages = new List<PageLabel>();
    for (int pageIndex = 0; pageIndex < pages.Count; pageIndex++)
    {
        var page = pages[pageIndex];
        debugLog?.AppendLine(CultureInfo.InvariantCulture, $"## {page.Name}");
        var stopwatch = Stopwatch.StartNew();
        var detected = await detector.DetectAsync(page.Data, isManga, debugLog is null ? null : line => debugLog.AppendLine(line));
        detectionTimes.Add(stopwatch.Elapsed.TotalMilliseconds);

        totalPages++;
        totalPanels += detected.Count;
        if (detected.Count == 1 && detected[0].Area >= 0.95)
        {
            splashPages++;
        }

        detectedPages.Add(new PageLabel { Name = page.Name, Panels = detected });

        labelsByName.TryGetValue(page.Name, out var label);
        PageScore? score = null;
        if (label is { Reviewed: true })
        {
            score = Scoring.ScorePage(comicName, page.Name, label.Panels, detected);
            scores.Add(score);
        }

        if (runStability)
        {
            foreach (var (variant, data) in CreateVariants(page.Data))
            {
                var variantResult = await detector.DetectAsync(data, isManga);
                stability.Add((comicName, page.Name, variant, Scoring.IsSameResult(detected, variantResult)));
            }
        }

        var imageFile = $"{pageIndex + 1:0000}{Path.GetExtension(page.Name).ToLowerInvariant()}";
        await File.WriteAllBytesAsync(Path.Combine(pagesOut, imageFile), page.Data);
        editorPages.Add(new
        {
            name = page.Name,
            image = $"pages/{imageFile}",
            detected,
            label,
            score = score is null ? null : new { score.IsCorrect, score.IsCorrectInOrder, score.Expected, score.Detected, score.Found, score.MeanIou }
        });
    }

    var detectedFile = new LabelFile { Comic = comicName, Pages = detectedPages };
    await File.WriteAllTextAsync(Path.Combine(comicOut, $"detected-{detector.Name}.json"),
        JsonSerializer.Serialize(detectedFile, LabelFile.JsonOptions));
    await WriteEditorAsync(Path.Combine(comicOut, "labels.html"), comicName, detector.Name, editorPages);
    if (debugLog is not null)
    {
        await File.WriteAllTextAsync(Path.Combine(comicOut, $"debug-{detector.Name}.log"), debugLog.ToString());
    }
    Console.WriteLine($"  {pages.Count} pages, {labelsByName.Values.Count(label => label.Reviewed)} reviewed labels, editor: {Path.Combine(comicOut, "labels.html")}");
}

var report = BuildReport();
Console.WriteLine();
Console.WriteLine(report);
await File.WriteAllTextAsync(Path.Combine(outDir, $"report-{detector.Name}.md"), report);
await AppendHistoryAsync(Path.Combine(outDir, "history.csv"));
return 0;

string BuildReport()
{
    var builder = new StringBuilder();
    builder.AppendLine(CultureInfo.InvariantCulture, $"# Panel detection: {detector.Name} ({DateTime.Now:yyyy-MM-dd HH:mm})");
    builder.AppendLine();
    builder.AppendLine(CultureInfo.InvariantCulture, $"Pages: {totalPages}, panels per page: {Ratio(totalPanels, totalPages):0.00}, splash pages: {splashPages}, ms per page: {Average(detectionTimes):0} (max {(detectionTimes.Count > 0 ? detectionTimes.Max() : 0):0})");
    builder.AppendLine();

    if (scores.Count > 0)
    {
        int expected = scores.Sum(score => score.Expected);
        int detected = scores.Sum(score => score.Detected);
        int found = scores.Sum(score => score.Found);
        builder.AppendLine(CultureInfo.InvariantCulture, $"## Accuracy on {scores.Count} reviewed pages");
        builder.AppendLine();
        builder.AppendLine("| Metric | Value |");
        builder.AppendLine("|---|---|");
        builder.AppendLine(CultureInfo.InvariantCulture, $"| Pages correct (right number of panels, all found) | {Percent(scores.Count(score => score.IsCorrect), scores.Count)} |");
        builder.AppendLine(CultureInfo.InvariantCulture, $"| Pages correct and in reading order | {Percent(scores.Count(score => score.IsCorrectInOrder), scores.Count)} |");
        builder.AppendLine(CultureInfo.InvariantCulture, $"| Panel recall (IoU >= {PageScore.MatchIou}) | {Percent(found, expected)} |");
        builder.AppendLine(CultureInfo.InvariantCulture, $"| Panel precision | {Percent(found, detected)} |");
        builder.AppendLine(CultureInfo.InvariantCulture, $"| Mean IoU of found panels | {Average(scores.SelectMany(score => score.Matches).Select(match => match.Iou).ToList()):0.000} |");
        builder.AppendLine();

        var failures = scores.Where(score => !score.IsCorrectInOrder).ToList();
        if (failures.Count > 0)
        {
            builder.AppendLine("### Pages which are not correct");
            builder.AppendLine();
            builder.AppendLine("| Comic | Page | Expected | Detected | Found | Mean IoU | Problem |");
            builder.AppendLine("|---|---|---|---|---|---|---|");
            foreach (var score in failures)
            {
                string problem = score.IsCorrect ? "order" :
                    score.Detected > score.Expected ? "too many" :
                    score.Detected < score.Expected ? "too few" : "wrong boxes";
                builder.AppendLine(CultureInfo.InvariantCulture, $"| {score.Comic} | {score.Page} | {score.Expected} | {score.Detected} | {score.Found} | {score.MeanIou:0.00} | {problem} |");
            }
            builder.AppendLine();
        }
    }
    else
    {
        builder.AppendLine("No reviewed labels yet, open the labels.html of a comic to create them.");
        builder.AppendLine();
    }

    if (stability.Count > 0)
    {
        builder.AppendLine("## Stability (same result for a slightly changed page)");
        builder.AppendLine();
        builder.AppendLine("| Variant | Stable pages |");
        builder.AppendLine("|---|---|");
        foreach (var group in stability.GroupBy(entry => entry.Variant))
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"| {group.Key} | {Percent(group.Count(entry => entry.Stable), group.Count())} |");
        }
        builder.AppendLine(CultureInfo.InvariantCulture, $"| all variants | {Percent(stability.GroupBy(entry => (entry.Comic, entry.Page)).Count(page => page.All(entry => entry.Stable)), stability.Select(entry => (entry.Comic, entry.Page)).Distinct().Count())} |");
        builder.AppendLine();
    }

    return builder.ToString();
}

async Task AppendHistoryAsync(string path)
{
    bool exists = File.Exists(path);
    int expected = scores.Sum(score => score.Expected);
    int detected = scores.Sum(score => score.Detected);
    int found = scores.Sum(score => score.Found);
    var line = string.Join(",",
        DateTime.Now.ToString("s", CultureInfo.InvariantCulture),
        detector.Name,
        totalPages.ToString(CultureInfo.InvariantCulture),
        scores.Count.ToString(CultureInfo.InvariantCulture),
        Ratio(scores.Count(score => score.IsCorrect), scores.Count).ToString("0.000", CultureInfo.InvariantCulture),
        Ratio(scores.Count(score => score.IsCorrectInOrder), scores.Count).ToString("0.000", CultureInfo.InvariantCulture),
        Ratio(found, expected).ToString("0.000", CultureInfo.InvariantCulture),
        Ratio(found, detected).ToString("0.000", CultureInfo.InvariantCulture),
        (stability.Count == 0 ? double.NaN : Ratio(stability.Count(entry => entry.Stable), stability.Count)).ToString("0.000", CultureInfo.InvariantCulture),
        Average(detectionTimes).ToString("0", CultureInfo.InvariantCulture));
    await File.AppendAllTextAsync(path,
        (exists ? string.Empty : "time,detector,pages,reviewed,correct,correct_in_order,recall,precision,stable,ms_per_page" + Environment.NewLine) +
        line + Environment.NewLine);
}

static IEnumerable<(string Variant, byte[] Data)> CreateVariants(byte[] pageData)
{
    using var source = Cv2.ImDecode(pageData, ImreadModes.Color);
    if (source.Empty())
    {
        yield break;
    }

    var jpeg = new ImageEncodingParam(ImwriteFlags.JpegQuality, 60);
    foreach (var scale in new[] { 0.75, 0.5 })
    {
        using var scaled = new Mat();
        Cv2.Resize(source, scaled, new Size(), scale, scale, InterpolationFlags.Area);
        yield return ($"scaled {scale:0%}", scaled.ImEncode(".png"));
    }

    yield return ("jpeg quality 60", source.ImEncode(".jpg", jpeg));
}

static async Task WriteEditorAsync(string path, string comicName, string detectorName, List<object> pages)
{
    await using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("LabelEditor.html")
        ?? throw new InvalidOperationException("LabelEditor.html resource is missing");
    using var reader = new StreamReader(resource);
    var template = await reader.ReadToEndAsync();
    var data = JsonSerializer.Serialize(new
    {
        comic = comicName,
        labelFile = Path.GetFileName(LabelFile.PathFor(comicName)),
        detector = detectorName,
        pages
    }, LabelFile.JsonOptions);
    // Keep "</script>" in page names from ending the script block
    data = data.Replace("</", "<\\/", StringComparison.Ordinal);
    await File.WriteAllTextAsync(path, template.Replace("/*BENCH_DATA*/null", data, StringComparison.Ordinal));
}

static string SafeName(string name) =>
    string.Concat(name.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));

static double Ratio(int part, int total) => total == 0 ? 0 : (double)part / total;

static string Percent(int part, int total) => total == 0 ? "-" : $"{Ratio(part, total):0.0%} ({part}/{total})";

static double Average(IReadOnlyCollection<double> values) => values.Count == 0 ? 0 : values.Average();

static void PrintUsage()
{
    Console.WriteLine("""
        Usage: StripWolf.PanelBench [folder] [--out <dir>] [--detector service] [--stability] [--manga] [--max-pages <n>] [--debug]

          folder        Folder with .cbz/.cbr files, defaults to %STRIPWOLF_PANEL_TESTDATA%.
                        Expected panels are read from "<comic>.panels.json" next to each comic.
          --out         Output folder, defaults to "<folder>/_panelbench".
          --stability   Also detect slightly changed pages (scaled, re-compressed) and report how often the result changes.
          --debug       Write the decisions of the detector per page to "debug-<detector>.log" (gutter detector).
        """);
}
