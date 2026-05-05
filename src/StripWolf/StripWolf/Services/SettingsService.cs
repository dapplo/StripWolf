using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using StripWolf.Models;

namespace StripWolf.Services;

/// <summary>
/// Service for persisting application settings with secure password storage
/// </summary>
public class SettingsService
{
    private const string SettingsFileName = "settings.json";
    private const string PasswordsFileName = "credentials.dat";
    
    private readonly string _settingsDir;
    private readonly string _settingsPath;
    private readonly string _passwordsPath;
    private readonly byte[] _encryptionKey;
    
    private AppSettings? _cachedSettings;

    public event EventHandler<AppSettings>? SettingsChanged;

    public SettingsService()
    {
        _settingsDir = GetAppDataDirectory();
        Directory.CreateDirectory(_settingsDir);
        _settingsPath = Path.Combine(_settingsDir, SettingsFileName);
        _passwordsPath = Path.Combine(_settingsDir, PasswordsFileName);
        _encryptionKey = GetOrCreateEncryptionKey();
    }

    private static string GetAppDataDirectory()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(baseDir, "StripWolf");
    }

    /// <summary>
    /// Gets or creates a machine-specific encryption key
    /// </summary>
    private byte[] GetOrCreateEncryptionKey()
    {
        var keyPath = Path.Combine(_settingsDir, ".key");
        
        if (File.Exists(keyPath))
        {
            try
            {
                var keyBytes = File.ReadAllBytes(keyPath);
                if (keyBytes.Length == 32)
                {
                    return keyBytes;
                }
            }
            catch (IOException)
            {
                // Fall through to generate new key
            }
            catch (UnauthorizedAccessException)
            {
                // Fall through to generate new key
            }
        }

        // Generate a new random 256-bit key
        var newKey = RandomNumberGenerator.GetBytes(32);
        
        try
        {
            // Set file as hidden on Windows
            File.WriteAllBytes(keyPath, newKey);
            if (OperatingSystem.IsWindows())
            {
                File.SetAttributes(keyPath, FileAttributes.Hidden);
            }
        }
        catch (IOException)
        {
            // If we can't persist the key, return it anyway
            // Passwords will need to be re-entered after app restart
        }
        catch (UnauthorizedAccessException)
        {
            // If we can't persist the key, return it anyway
            // Passwords will need to be re-entered after app restart
        }

        return newKey;
    }

    /// <summary>
    /// Load settings from disk
    /// </summary>
    public AppSettings LoadSettings()
    {
        if (_cachedSettings is not null)
        {
            return _cachedSettings;
        }

        _cachedSettings = new AppSettings();

        if (File.Exists(_settingsPath))
        {
            try
            {
                var json = File.ReadAllText(_settingsPath);
                _cachedSettings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            catch (IOException)
            {
                // If settings file cannot be read, start fresh
                _cachedSettings = new AppSettings();
            }
            catch (JsonException)
            {
                // If settings file is corrupted, start fresh
                _cachedSettings = new AppSettings();
            }
        }

        NormalizeSectionPreferences(_cachedSettings);

        // Load passwords separately and decrypt them
        LoadPasswords(_cachedSettings);

        return _cachedSettings;
    }

    /// <summary>
    /// Save settings to disk
    /// </summary>
    public async Task SaveSettingsAsync(AppSettings settings)
    {
        NormalizeSectionPreferences(settings);
        _cachedSettings = settings;

        // Save settings without sensitive data
        var settingsToSave = settings.Clone();
        foreach (var server in settingsToSave.Servers)
        {
            server.Password = string.Empty; 
            server.ApiKey = string.Empty;
        }

        var json = JsonSerializer.Serialize(settingsToSave, new JsonSerializerOptions 
        { 
            WriteIndented = true 
        });
        await File.WriteAllTextAsync(_settingsPath, json);

        // Save sensitive data separately encrypted
        await SavePasswordsAsync(settings);

        SettingsChanged?.Invoke(this, settings.Clone());
    }

    /// <summary>
    /// Encrypts and saves passwords to a separate file
    /// </summary>
    private async Task SavePasswordsAsync(AppSettings settings)
    {
        var sensitiveData = new Dictionary<int, (string? Password, string? ApiKey)>();
        foreach (var server in settings.Servers)
        {
            if (!string.IsNullOrEmpty(server.Password) || !string.IsNullOrEmpty(server.ApiKey))
            {
                sensitiveData[server.Id] = (server.Password, server.ApiKey);
            }
        }

        var json = JsonSerializer.Serialize(sensitiveData, new JsonSerializerOptions { IncludeFields = true });
        var encrypted = Encrypt(json);
        await File.WriteAllBytesAsync(_passwordsPath, encrypted);
    }

    /// <summary>
    /// Loads and decrypts sensitive data from the separate file
    /// </summary>
    private void LoadPasswords(AppSettings settings)
    {
        if (!File.Exists(_passwordsPath))
        {
            return;
        }

        try
        {
            var encrypted = File.ReadAllBytes(_passwordsPath);
            var json = Decrypt(encrypted);
            
            // Try new format first (tuple)
            try 
            {
                var sensitiveData = JsonSerializer.Deserialize<Dictionary<int, (string? Password, string? ApiKey)>>(json, new JsonSerializerOptions { IncludeFields = true });
                if (sensitiveData is not null)
                {
                    foreach (var server in settings.Servers)
                    {
                        if (sensitiveData.TryGetValue(server.Id, out var data))
                        {
                            server.Password = data.Password ?? string.Empty;
                            server.ApiKey = data.ApiKey ?? string.Empty;
                        }
                    }
                    return;
                }
            }
            catch { /* fallback to old format */ }

            // Fallback to old format (string password only)
            var passwords = JsonSerializer.Deserialize<Dictionary<int, string>>(json);
            if (passwords is not null)
            {
                foreach (var server in settings.Servers)
                {
                    if (passwords.TryGetValue(server.Id, out var password))
                    {
                        server.Password = password;
                    }
                }
            }
        }
        catch (IOException)
        {
            // If passwords file cannot be read, passwords will need to be re-entered
        }
        catch (JsonException)
        {
            // If passwords file is corrupted, passwords will need to be re-entered
        }
        catch (CryptographicException)
        {
            // If decryption fails (key changed), passwords will need to be re-entered
        }
    }

    /// <summary>
    /// Encrypt a string using AES-GCM
    /// </summary>
    private byte[] Encrypt(string plainText)
    {
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];
        var cipherBytes = new byte[plainBytes.Length];

        using var aes = new AesGcm(_encryptionKey, AesGcm.TagByteSizes.MaxSize);
        aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

        // Combine nonce + tag + cipher for storage
        var result = new byte[nonce.Length + tag.Length + cipherBytes.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, result, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipherBytes, 0, result, nonce.Length + tag.Length, cipherBytes.Length);

        return result;
    }

    /// <summary>
    /// Decrypt a byte array using AES-GCM
    /// </summary>
    private string Decrypt(byte[] encrypted)
    {
        var nonceSize = AesGcm.NonceByteSizes.MaxSize;
        var tagSize = AesGcm.TagByteSizes.MaxSize;

        var nonce = new byte[nonceSize];
        var tag = new byte[tagSize];
        var cipherBytes = new byte[encrypted.Length - nonceSize - tagSize];

        Buffer.BlockCopy(encrypted, 0, nonce, 0, nonceSize);
        Buffer.BlockCopy(encrypted, nonceSize, tag, 0, tagSize);
        Buffer.BlockCopy(encrypted, nonceSize + tagSize, cipherBytes, 0, cipherBytes.Length);

        var plainBytes = new byte[cipherBytes.Length];
        using var aes = new AesGcm(_encryptionKey, tagSize);
        aes.Decrypt(nonce, cipherBytes, tag, plainBytes);

        return Encoding.UTF8.GetString(plainBytes);
    }

    private static void NormalizeSectionPreferences(AppSettings settings)
    {
        settings.LibrarySections = SectionLayoutPreference.MergeWithDefaults(
            settings.LibrarySections,
            SectionLayoutPreference.CreateDefaultLibrarySections());

        settings.KomgaSections = SectionLayoutPreference.MergeWithDefaults(
            settings.KomgaSections,
            SectionLayoutPreference.CreateDefaultKomgaSections());
    }
}

/// <summary>
/// Application settings model
/// </summary>
public class AppSettings
{
    public List<KomgaServer> Servers { get; set; } = [];
    
    public int? ActiveServerId { get; set; }
    
    public string? LastOpenedComicPath { get; set; }
    
    public string? ComicsDirectory { get; set; }
    
    /// <summary>
    /// The preferred language code (e.g., "en", "de", "fr"), or null for system default
    /// </summary>
    public string? LanguageCode { get; set; }
    
    /// <summary>
    /// Whether to use the system language setting
    /// </summary>
    public bool UseSystemLanguage { get; set; } = true;
    
    /// <summary>
    /// Preferred reading mode for the comic reader
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ReadingMode PreferredReadingMode { get; set; } = ReadingMode.Normal;
    
    /// <summary>
    /// Handedness preference for zoomed/guided reading layout
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Handedness Handedness { get; set; } = Handedness.RightHanded;
    
    /// <summary>
    /// Default zoom region size for zoomed reading mode (0.1 to 0.8)
    /// </summary>
    public double DefaultZoomRegionSize { get; set; } = 0.3;

    /// <summary>
    /// Whether to use compact overview in zoomed/guided reading mode (saves screen space)
    /// </summary>
    public bool CompactOverview { get; set; } = false;

    /// <summary>
    /// Theme to use for the application UI.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AppThemePreference AppTheme { get; set; } = AppThemePreference.System;

    /// <summary>
    /// Theme to use when converting EPUB pages into rendered images.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public EpubConversionTheme EpubConversionTheme { get; set; } = EpubConversionTheme.System;

    /// <summary>
    /// Output resolution to use when converting EPUB pages into rendered images.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public EpubOutputResolution EpubOutputResolution { get; set; } = EpubOutputResolution.Low;

    public List<SectionLayoutPreference> LibrarySections { get; set; } = SectionLayoutPreference.CreateDefaultLibrarySections();

    public List<SectionLayoutPreference> KomgaSections { get; set; } = SectionLayoutPreference.CreateDefaultKomgaSections();

    /// <summary>
    /// Creates a deep copy of the settings
    /// </summary>
    public AppSettings Clone()
    {
        return new AppSettings
        {
            Servers = Servers.Select(s => new KomgaServer
            {
                Id = s.Id,
                Name = s.Name,
                BaseUrl = s.BaseUrl,
                Username = s.Username,
                Password = s.Password,
                ApiKey = s.ApiKey,
                CustomHeaders = s.CustomHeaders.Select(h => new KomgaHeader { Name = h.Name, Value = h.Value }).ToList(),
                IsActive = s.IsActive,
                AddedDate = s.AddedDate,
                LastConnected = s.LastConnected
            }).ToList(),
            ActiveServerId = ActiveServerId,
            LastOpenedComicPath = LastOpenedComicPath,
            ComicsDirectory = ComicsDirectory,
            LanguageCode = LanguageCode,
            UseSystemLanguage = UseSystemLanguage,
            AppTheme = AppTheme,
            PreferredReadingMode = PreferredReadingMode,
            Handedness = Handedness,
            DefaultZoomRegionSize = DefaultZoomRegionSize,
            CompactOverview = CompactOverview,
            EpubConversionTheme = EpubConversionTheme,
            EpubOutputResolution = EpubOutputResolution,
            LibrarySections = LibrarySections.Select(section => section.Clone()).ToList(),
            KomgaSections = KomgaSections.Select(section => section.Clone()).ToList()
        };
    }
}
