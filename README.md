<div align="center">
  <img src="media/StripFlow.png" alt="StripWolf Logo" width="300" />
  <p><i>As comics are called strips in Dutch, and follow a flow, Wolf is the reverse of flow.</i></p>
</div>

A cross-platform comic book reader built with Avalonia UI that supports offline reading and Komga integration.

## Features

- **Local Comic Reading**: Read CBZ, CBR, CB7, CBT, PDF, and EPUB files stored on your device
- **PDF Support**: Import PDF files - they are automatically converted to CBZ format for optimal viewing
- **EPUB Support**: On supported platforms you can import EPUB files either by converting them to CBZ on import or by converting pages on demand while you read
- **Extended Format Support**: Read CB7 (7-Zip) and CBT (TAR) archives natively, while solid RAR archives are automatically converted to CBZ for reliable reading
- **ComicInfo.xml Support**: Automatically extracts and displays metadata from ComicInfo.xml files embedded in comic archives
- **Komga Integration**: Connect to your Komga server to browse and download comics
- **Multiple Reading Modes**:
  - **Normal**: Standard full-page reading
  - **Zoomed**: Split view with page overview and magnified area
  - **Guided**: Automatic panel detection for scene-by-scene navigation (available on limited platforms and still in development)
- **Handedness Options**: Choose between left-handed and right-handed layouts for Zoomed and Guided modes
- **Offline Reading**: Download comics from Komga for offline access
- **Reading Progress**: Automatically tracks your reading progress
- **Cross-Platform**: Works on Windows, Linux, macOS, and Android
- **Semantic Versioning**: Powered by GitVersion for clear version tracking

## Support

If you like the project, you can support me:
- **Ko-fi**: [Support on Ko-fi](https://ko-fi.com/lakritzator)
- **PayPal**: [Donate with PayPal](https://paypal.me/lakritzator)

## Supported Formats

| Format | Extension | Support |
|--------|-----------|---------|
| CBZ (ZIP) | .cbz | ✅ Native |
| CBR (RAR) | .cbr | ✅ Native (converted if solid) |
| CB7 (7-Zip) | .cb7 | ✅ Native |
| CBT (TAR) | .cbt | ✅ Native |
| PDF | .pdf | ✅ Converted to CBZ |
| EPUB | .epub | ✅ Supported on android (convert on import or convert while reading) |

### Supported Image Formats

Comics can contain images in the following formats:
- JPEG (.jpg, .jpeg)
- PNG (.png)
- GIF (.gif)
- WebP (.webp)
- BMP (.bmp)
- TIFF (.tiff, .tif)
- AVIF (.avif)

## Supported Platforms

- Windows
- Android (APK)
- Linux (still needs testing)
- macOS (still needs testing)

## Installation

### Android APK

Download the latest APK from the [Releases](https://github.com/dapplo/StripWolf/releases) page:

1. Download `StripWolf-Android.apk` from the latest release
2. On your Android device, enable "Install from unknown sources" in Settings > Security
3. Open the downloaded APK file and follow the installation prompts
4. The app is now ready to use

**Note**: If you are trying to build the APK yourself or run from Visual Studio, the app is signed with a development key for sideloading. This will conflict with the packages build officially, and you will lose your local comics and information.

### Windows

Download the `StripWolf-Windows-x64.zip` from the [Releases](https://github.com/dapplo/StripWolf/releases) page and extract it to your preferred location.

### Linux

Download the `StripWolf-Linux-x64.tar.gz` from the [Releases](https://github.com/dapplo/StripWolf/releases) page:

```bash
mkdir -p ~/StripWolf
tar -xzvf StripWolf-Linux-x64.tar.gz -C ~/StripWolf
chmod +x ~/StripWolf/StripWolf.Desktop
```

### macOS

Download the appropriate archive from the [Releases](https://github.com/dapplo/StripWolf/releases) page:
- **Intel Macs**: `StripWolf-macOS-x64.tar.gz`
- **Apple Silicon (M1/M2/M3)**: `StripWolf-macOS-arm64.tar.gz`

Extract the archive:
```bash
tar -xzvf StripWolf-macOS-*.tar.gz -C ~/Applications
```

**Note**: On first run, you may need to allow the app in System Preferences > Security & Privacy if macOS blocks it.

## Requirements

- .NET 10.0 SDK

## Building

### Desktop (Windows/Linux/macOS)

```bash
cd src/StripWolf
dotnet build StripWolf.Desktop/StripWolf.Desktop.csproj -c Release
```

### Android APK

```bash
cd src/StripWolf
dotnet build StripWolf.Android/StripWolf.Android.csproj -c Release
```

## Project Structure

```
src/StripWolf/
├── StripWolf/               # Core library (shared code)
│   ├── Data/             # Database service for local storage
│   ├── Models/           # Data models (Comic, KomgaServer, etc.)
│   │   └── Komga/        # Komga API models
│   ├── Services/         # Business logic services
│   │   ├── ComicReaderService.cs       # CBZ/CBR reading
│   │   ├── PdfToCbzConverterService.cs # PDF to CBZ conversion
│   │   ├── KomgaApiService.cs          # Komga API client
│   │   └── LibraryService.cs           # Library management
│   ├── ViewModels/       # MVVM view models
│   └── Views/            # Avalonia XAML views
├── StripWolf.Desktop/       # Desktop launcher (Windows/Linux/macOS)
└── StripWolf.Android/       # Android launcher
```

## Komga API

This app connects to [Komga](https://github.com/gotson/komga), a media server for comics/mangas/BDs. 
Configure your server URL and credentials in the Settings page to:

- Browse libraries and series
- Download books for offline reading
- Sync reading progress

## Technology Stack

- **.NET**: Current version 10
- **UI Framework**: Avalonia UI 12.x (cross-platform)
- **MVVM**: CommunityToolkit.Mvvm
- **Storage**: SQLite via sqlite-net-pcl
- **CB(R/T/Z/7) Support**: SharpCompress
- **PDF Support**: PDFiumCore for PDF rendering, SixLabors.ImageSharp for image processing
- **EPUB support**: VerseOne.EPub rendered in a WebView, currently only working on Windows (but not the package one) and Android
- Development is supported by AI, using Gemini and Copilot.


## License

GPLv3, see [LICENSE](LICENSE) file for details.
