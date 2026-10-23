# StripWolf.PanelBench

A developer tool to measure the panel detection (guided reading) against real comics. The comics and their expected
panels are kept outside of the repository, they are usually not ours to share.

## Usage

```
dotnet run -c Release --project src/StripWolf/StripWolf.PanelBench -- "C:\path\to\Test-Comics" --stability
```

Instead of passing the folder, `STRIPWOLF_PANEL_TESTDATA` can be set. The OpenCV native runtime is only referenced on
Windows, like for StripWolf.Desktop.

For every `.cbz` / `.cbr` in the folder the tool:

1. Runs the detection on all pages.
2. Writes `_panelbench/<comic>/labels.html`, a label editor showing the detection (red, dashed) and the expected panels
   (green). Pages without labels are pre-filled with the detection, so mostly the boxes only need correcting.
3. Compares the detection with the expected panels of the reviewed pages in `<comic>.panels.json` (next to the comic).
4. Writes `_panelbench/report-<detector>.md` and appends a line to `_panelbench/history.csv`, so runs can be compared.

## Labelling

Open `labels.html` in Edge or Chrome, correct the panels of a page, put them in reading order and press Enter to mark
the page as reviewed. Only reviewed pages are scored. "Save labels" writes `<comic>.panels.json`, save it next to the
comic (preferred, it survives deleting `_panelbench`) or next to `labels.html`. The editor keeps a draft in the browser in case the page is closed before saving.

A page without panels (a cover, a splash page) is one panel covering the whole page (S).

## Metrics

- **Pages correct**: every expected panel is found (IoU of at least 0.5) and nothing extra is detected. The number and
  order of the panels matter, a few pixels more or less around a panel don't.
- **In reading order**: correct, and the panels come in the expected order.
- **Recall / precision**: per panel, a detected panel counts as found with an IoU of at least 0.5.
- **Stability** (`--stability`, needs no labels): the page is detected again scaled to 75% and 50% and re-compressed
  as JPEG, the result should practically be the same (same number of panels, IoU of at least 0.85).

## Comparing detectors

`--detector <name>` selects the implementation, see `Detectors.cs`:

- `service` (default): the current `PanelDetectionService` as used by the app.
- `gutter`: `GutterPanelDetector`, splits the page along its gutters (recursive XY-cut).

Each detector writes its own `report-<detector>.md`, `history.csv` has a line per run.
