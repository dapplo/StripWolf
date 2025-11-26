# Kom2go

A cross-platform comic book reader built with .NET MAUI that supports offline reading and Komga integration.

## Features

- **Local Comic Reading**: Read CBZ and CBR comic files stored on your device
- **Komga Integration**: Connect to your Komga server to browse and download comics
- **Offline Reading**: Download comics from Komga for offline access
- **Reading Progress**: Automatically tracks your reading progress
- **Tablet-Optimized**: Designed primarily for tablet use but works on desktop too

## Supported Platforms

- Android (APK)
- Windows

## Requirements

- .NET 10.0 SDK
- MAUI workloads (`dotnet workload install maui-android maui-windows`)

## Building

### Android APK

```bash
cd src/Kom2go
dotnet build -f net10.0-android -c Release
```

The APK will be generated in `bin/Release/net10.0-android/`.

### Windows

```bash
cd src/Kom2go
dotnet build -f net10.0-windows10.0.19041.0 -c Release
```

## Project Structure

```
src/Kom2go/
├── Converters/          # Value converters for data binding
├── Data/                # Database service for local storage
├── Models/              # Data models (Comic, KomgaServer, etc.)
│   └── Komga/           # Komga API models
├── Services/            # Business logic services
│   ├── ComicReaderService.cs   # CBZ/CBR reading
│   ├── KomgaApiService.cs      # Komga API client
│   └── LibraryService.cs       # Library management
├── ViewModels/          # MVVM view models
├── Views/               # XAML pages
└── Platforms/           # Platform-specific code
```

## Komga API

This app connects to [Komga](https://github.com/gotson/komga), a media server for comics/mangas/BDs. 
Configure your server URL and credentials in the Settings page to:

- Browse libraries and series
- Download books for offline reading
- Sync reading progress

## License

See [LICENSE](LICENSE) file for details.
