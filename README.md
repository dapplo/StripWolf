# Kom2go

A cross-platform comic book reader built with Avalonia UI that supports offline reading and Komga integration.

## Features

- **Local Comic Reading**: Read CBZ and CBR comic files stored on your device
- **Komga Integration**: Connect to your Komga server to browse and download comics
- **Offline Reading**: Download comics from Komga for offline access
- **Reading Progress**: Automatically tracks your reading progress
- **Cross-Platform**: Works on Windows, Linux, macOS, and Android

## Supported Platforms

- Windows
- Linux
- macOS
- Android (APK)

## Requirements

- .NET 10.0 SDK

## Building

### Desktop (Windows/Linux/macOS)

```bash
cd src/Kom2go
dotnet build Kom2go.Desktop/Kom2go.Desktop.csproj -c Release
```

### Android APK

```bash
cd src/Kom2go
dotnet build Kom2go.Android/Kom2go.Android.csproj -c Release
```

## Project Structure

```
src/Kom2go/
├── Kom2go/               # Core library (shared code)
│   ├── Data/             # Database service for local storage
│   ├── Models/           # Data models (Comic, KomgaServer, etc.)
│   │   └── Komga/        # Komga API models
│   ├── Services/         # Business logic services
│   │   ├── ComicReaderService.cs   # CBZ/CBR reading
│   │   ├── KomgaApiService.cs      # Komga API client
│   │   └── LibraryService.cs       # Library management
│   ├── ViewModels/       # MVVM view models
│   └── Views/            # Avalonia XAML views
├── Kom2go.Desktop/       # Desktop launcher (Windows/Linux/macOS)
└── Kom2go.Android/       # Android launcher
```

## Komga API

This app connects to [Komga](https://github.com/gotson/komga), a media server for comics/mangas/BDs. 
Configure your server URL and credentials in the Settings page to:

- Browse libraries and series
- Download books for offline reading
- Sync reading progress

## Technology Stack

- **UI Framework**: Avalonia UI 11.x (cross-platform)
- **MVVM**: CommunityToolkit.Mvvm
- **Storage**: SQLite via sqlite-net-pcl
- **CBR Support**: SharpCompress

## License

See [LICENSE](LICENSE) file for details.
