<div align="center">
  <img src="StripFlow.png" alt="StripWolf Logo" width="300" />
  <p><i>As comics are called strips in Dutch, and follow a flow, Wolf is the reverse of flow.</i></p>
</div>

A cross-platform comic book reader built with Avalonia UI that supports offline reading and Komga integration.

## Features

- **Local Comic Reading**: Read CBZ, CBR, CB7, CBT, PDF, and EPUB files stored on your device
- **PDF Support**: Import PDF files - they are automatically converted to CBZ format for optimal viewing
- **EPUB Support**: Import EPUB files either by converting them to CBZ on import or by converting pages on demand while you read
- **Extended Format Support**: Read CB7 (7-Zip) and CBT (TAR) archives natively, while solid RAR archives are automatically converted to CBZ for reliable reading
- **ComicInfo.xml Support**: Automatically extracts and displays metadata from ComicInfo.xml files embedded in comic archives
- **Komga Integration**: Connect to your Komga server to browse and download comics
- **Offline Reading**: Download comics from Komga for offline access
- **Reading Progress**: Automatically tracks your reading progress
- **Cross-Platform**: Works on Windows, Linux, macOS, and Android

## Supported Formats

| Format | Extension | Support |
|--------|-----------|---------|
| CBZ (ZIP) | .cbz | ✅ Native |
| CBR (RAR) | .cbr | ✅ Native (converted if solid) |
| CB7 (7-Zip) | .cb7 | ✅ Native |
| CBT (TAR) | .cbt | ✅ Native |
| PDF | .pdf | ✅ Converted to CBZ |
| EPUB | .epub | ✅ Supported (convert on import or convert while reading) |

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
- Linux
- macOS
- Android (APK)

## Installation

### Android APK

Download the latest APK from the [Releases](https://github.com/dapplo/StripWolf/releases) page:

1. Download `StripWolf-Android.apk` from the latest release
2. On your Android device, enable "Install from unknown sources" in Settings > Security
3. Open the downloaded APK file and follow the installation prompts
4. The app is now ready to use

**Note**: The APK is signed with a development key for sideloading. To install updates, you may need to uninstall the previous version first if the signing key changes.

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

- **UI Framework**: Avalonia UI 11.x (cross-platform)
- **MVVM**: CommunityToolkit.Mvvm
- **Storage**: SQLite via sqlite-net-pcl
- **CBR Support**: SharpCompress
- **PDF Support**: PDFiumCore for PDF rendering, SixLabors.ImageSharp for image processing

## Publishing to Google Play Store

To publish this app to the Google Play Store, follow these steps:

### Prerequisites

1. **Google Play Developer Account**: Register at [Google Play Console](https://play.google.com/console) ($25 one-time fee)
2. **Signing Keystore**: Create a production signing keystore (keep it secure - losing it means you can't update your app)

### Generate a Production Keystore

```bash
keytool -genkeypair -v \
  -keystore StripWolf-release.keystore \
  -alias StripWolf-release \
  -keyalg RSA \
  -keysize 2048 \
  -validity 10000 \
  -storepass YOUR_STORE_PASSWORD \
  -keypass YOUR_KEY_PASSWORD \
  -dname "CN=StripWolf, OU=Development, O=Your Organization, L=City, ST=State, C=Country"
```

### Configure GitHub Secrets (for automated releases)

To use the production keystore with GitHub Actions:

1. Base64 encode your keystore: `base64 -i StripWolf-release.keystore -o keystore-base64.txt`
2. Add these secrets to your GitHub repository:
   - `ANDROID_KEYSTORE_BASE64`: Contents of `keystore-base64.txt`
   - `ANDROID_SIGNING_KEY_PASS`: Your key password
   - `ANDROID_SIGNING_STORE_PASS`: Your store password

### Build for Google Play

For Google Play Store distribution, build an AAB (Android App Bundle) instead of APK:

1. Update `StripWolf.Android.csproj`:
   ```xml
   <AndroidPackageFormat>aab</AndroidPackageFormat>
   ```

2. Build the release:
   ```bash
   dotnet publish src/StripWolf/StripWolf.Android/StripWolf.Android.csproj -c Release
   ```

### Upload to Google Play

1. Go to [Google Play Console](https://play.google.com/console)
2. Create a new app or select your existing app
3. Navigate to Production > Create new release
4. Upload the signed AAB file
5. Complete the store listing (screenshots, descriptions, etc.)
6. Submit for review

### App Store Requirements

Before submitting, ensure you have:

- [ ] App icon in various sizes
- [ ] Feature graphic (1024x500)
- [ ] Screenshots for phones and tablets
- [ ] Privacy policy URL
- [ ] Content rating questionnaire completed
- [ ] Target audience and content declaration

## License

See [LICENSE](LICENSE) file for details.
