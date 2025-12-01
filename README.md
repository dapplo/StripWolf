# Kom2go

A cross-platform comic book reader built with Avalonia UI that supports offline reading and Komga integration.

## Features

- **Local Comic Reading**: Read CBZ, CBR, and PDF files stored on your device
- **PDF Support**: Import PDF files - they are automatically converted to CBZ format for optimal viewing
- **Komga Integration**: Connect to your Komga server to browse and download comics
- **Offline Reading**: Download comics from Komga for offline access
- **Reading Progress**: Automatically tracks your reading progress
- **Cross-Platform**: Works on Windows, Linux, macOS, and Android

## Supported Platforms

- Windows
- Linux
- macOS
- Android (APK)

## Installation

### Android APK

Download the latest APK from the [Releases](https://github.com/dapplo/Kom2go/releases) page:

1. Download `Kom2go-Android.apk` from the latest release
2. On your Android device, enable "Install from unknown sources" in Settings > Security
3. Open the downloaded APK file and follow the installation prompts
4. The app is now ready to use

**Note**: The APK is signed with a development key for sideloading. To install updates, you may need to uninstall the previous version first if the signing key changes.

### Windows

Download the `Kom2go-Windows-x64.zip` from the [Releases](https://github.com/dapplo/Kom2go/releases) page and extract it to your preferred location.

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
│   │   ├── ComicReaderService.cs       # CBZ/CBR reading
│   │   ├── PdfToCbzConverterService.cs # PDF to CBZ conversion
│   │   ├── KomgaApiService.cs          # Komga API client
│   │   └── LibraryService.cs           # Library management
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
- **PDF Support**: PDFiumCore for PDF rendering, SixLabors.ImageSharp for image processing

## Publishing to Google Play Store

To publish this app to the Google Play Store, follow these steps:

### Prerequisites

1. **Google Play Developer Account**: Register at [Google Play Console](https://play.google.com/console) ($25 one-time fee)
2. **Signing Keystore**: Create a production signing keystore (keep it secure - losing it means you can't update your app)

### Generate a Production Keystore

```bash
keytool -genkeypair -v \
  -keystore kom2go-release.keystore \
  -alias kom2go-release \
  -keyalg RSA \
  -keysize 2048 \
  -validity 10000 \
  -storepass YOUR_STORE_PASSWORD \
  -keypass YOUR_KEY_PASSWORD \
  -dname "CN=Kom2go, OU=Development, O=Your Organization, L=City, ST=State, C=Country"
```

### Configure GitHub Secrets (for automated releases)

To use the production keystore with GitHub Actions:

1. Base64 encode your keystore: `base64 -i kom2go-release.keystore -o keystore-base64.txt`
2. Add these secrets to your GitHub repository:
   - `ANDROID_KEYSTORE_BASE64`: Contents of `keystore-base64.txt`
   - `ANDROID_SIGNING_KEY_PASS`: Your key password
   - `ANDROID_SIGNING_STORE_PASS`: Your store password

### Build for Google Play

For Google Play Store distribution, build an AAB (Android App Bundle) instead of APK:

1. Update `Kom2go.Android.csproj`:
   ```xml
   <AndroidPackageFormat>aab</AndroidPackageFormat>
   ```

2. Build the release:
   ```bash
   dotnet publish src/Kom2go/Kom2go.Android/Kom2go.Android.csproj -c Release
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
