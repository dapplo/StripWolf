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

using System.Globalization;
using System.Resources;

namespace StripWolf.Services;

/// <summary>
/// Service for managing application localization
/// </summary>
public class LocalizationService
{
    private static readonly ResourceManager ResourceManager = new("StripWolf.Resources.Strings", typeof(LocalizationService).Assembly);
    
    private CultureInfo _currentCulture;
    private bool _useSystemLanguage = true;
    
    /// <summary>
    /// Event raised when the language changes
    /// </summary>
    public event EventHandler? LanguageChanged;
    
    /// <summary>
    /// Available languages for the application
    /// </summary>
    public static readonly IReadOnlyList<LanguageOption> AvailableLanguages =
    [
        new LanguageOption("System Default", null),
        new LanguageOption("English", "en"),
        new LanguageOption("Deutsch", "de"),
        new LanguageOption("FranÃ§ais", "fr"),
        new LanguageOption("EspaÃ±ol", "es"),
        new LanguageOption("Nederlands", "nl")
    ];
    
    public LocalizationService()
    {
        _currentCulture = CultureInfo.CurrentUICulture;
    }
    
    /// <summary>
    /// Gets or sets whether to use the system language
    /// </summary>
    public bool UseSystemLanguage
    {
        get => _useSystemLanguage;
        set
        {
            _useSystemLanguage = value;
            if (value)
            {
                SetCulture(CultureInfo.CurrentUICulture);
            }
        }
    }
    
    /// <summary>
    /// Gets the current culture code (e.g., "en", "de", "fr")
    /// </summary>
    public string CurrentLanguageCode => _currentCulture.TwoLetterISOLanguageName;
    
    /// <summary>
    /// Gets the current culture
    /// </summary>
    public CultureInfo CurrentCulture => _currentCulture;
    
    /// <summary>
    /// Sets the application language by culture code
    /// </summary>
    /// <param name="cultureCode">The two-letter ISO language code (e.g., "en", "de", "fr") or null for system default</param>
    public void SetLanguage(string? cultureCode)
    {
        if (string.IsNullOrEmpty(cultureCode))
        {
            _useSystemLanguage = true;
            SetCulture(CultureInfo.CurrentUICulture);
        }
        else
        {
            _useSystemLanguage = false;
            try
            {
                SetCulture(new CultureInfo(cultureCode));
            }
            catch (CultureNotFoundException)
            {
                // If the culture is not found, fall back to system default
                _useSystemLanguage = true;
                SetCulture(CultureInfo.CurrentUICulture);
            }
        }
    }
    
    /// <summary>
    /// Sets the current culture
    /// </summary>
    private void SetCulture(CultureInfo culture)
    {
        _currentCulture = culture;
        
        // Update the culture for all threads in the application
        // DefaultThreadCurrentUICulture affects new threads as well
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }
    
    /// <summary>
    /// Gets a localized string by key
    /// </summary>
    /// <param name="key">The resource key</param>
    /// <returns>The localized string, or the key if not found</returns>
    public string GetString(string key)
    {
        try
        {
            return ResourceManager.GetString(key, _currentCulture) ?? key;
        }
        catch
        {
            return key;
        }
    }
    
    /// <summary>
    /// Gets a formatted localized string
    /// </summary>
    /// <param name="key">The resource key</param>
    /// <param name="args">Format arguments</param>
    /// <returns>The formatted localized string</returns>
    public string GetString(string key, params object[] args)
    {
        var format = GetString(key);
        try
        {
            return string.Format(_currentCulture, format, args);
        }
        catch
        {
            return format;
        }
    }
    
    /// <summary>
    /// Static helper to get a string (uses the default culture)
    /// </summary>
    public static string Get(string key)
    {
        try
        {
            return ResourceManager.GetString(key) ?? key;
        }
        catch
        {
            return key;
        }
    }
}

/// <summary>
/// Represents a language option for the UI
/// </summary>
public record LanguageOption(string DisplayName, string? CultureCode)
{
    public override string ToString() => DisplayName;
}

