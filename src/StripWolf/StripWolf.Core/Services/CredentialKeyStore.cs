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

using System.Security.Cryptography;

namespace StripWolf.Core.Services;

/// <summary>
/// Stores the key which encrypts credentials.dat.
/// Before, the raw key was written next to the credentials, so anyone with a copy of both files (e.g. a backup)
/// could decrypt the passwords. Now:
/// - Windows: the key is protected with DPAPI for the current user, a copied key file is useless on another
///   account or machine.
/// - Other platforms: the key and the credentials are only readable by the current user (mode 0600).
///   On Android and iOS the app data directory is already private to the app, and the Android backup rules
///   no longer include the key or the credentials.
/// </summary>
internal static class CredentialKeyStore
{
    internal const int KeySize = 32;

    // Key file layout: "SWK" + format version + protection scheme + key (protected or raw).
    // A file of exactly KeySize bytes is the legacy format (raw key, no header).
    private static ReadOnlySpan<byte> Magic => "SWK"u8;
    private const byte FormatVersion = 1;
    private const int HeaderSize = 5;

    internal enum Protection : byte
    {
        None = 0,
        WindowsDpapi = 1
    }

    // Binds the DPAPI blob to StripWolf, other applications of the same user can't unprotect it by accident
    private static readonly byte[] DpapiEntropy = "StripWolf credential key v1"u8.ToArray();

    /// <summary>
    /// The protection used for new key files on this platform
    /// </summary>
    internal static Protection PlatformProtection => OperatingSystem.IsWindows() ? Protection.WindowsDpapi : Protection.None;

    /// <summary>
    /// Read the key from the key file, or create (and store) a new one when there is none or it can't be used.
    /// A new key means the stored passwords can't be decrypted anymore and need to be entered again.
    /// </summary>
    internal static byte[] GetOrCreateKey(string keyPath)
    {
        var key = TryReadKey(keyPath, out var storedProtection, out var isLegacyFormat);
        if (key is not null)
        {
            if (isLegacyFormat || storedProtection != PlatformProtection)
            {
                // Legacy raw key (or a key file from another platform which could still be read): store it protected
                TryWriteKey(keyPath, key);
            }
            else
            {
                RestrictToCurrentUser(keyPath);
            }
            return key;
        }

        key = RandomNumberGenerator.GetBytes(KeySize);
        // If the key can't be persisted it is still used, passwords will need to be re-entered after an app restart
        TryWriteKey(keyPath, key);
        return key;
    }

    private static byte[]? TryReadKey(string keyPath, out Protection protection, out bool isLegacyFormat)
    {
        protection = Protection.None;
        isLegacyFormat = false;
        try
        {
            if (!File.Exists(keyPath))
            {
                return null;
            }

            var content = File.ReadAllBytes(keyPath);
            if (content.Length == KeySize)
            {
                // Legacy format: the raw key
                isLegacyFormat = true;
                return content;
            }

            if (content.Length <= HeaderSize || !content.AsSpan(0, Magic.Length).SequenceEqual(Magic) || content[3] != FormatVersion)
            {
                return null;
            }

            protection = (Protection)content[4];
            var payload = content.AsSpan(HeaderSize).ToArray();
            var key = protection switch
            {
                Protection.None => payload,
                Protection.WindowsDpapi when OperatingSystem.IsWindows() =>
                    ProtectedData.Unprotect(payload, DpapiEntropy, DataProtectionScope.CurrentUser),
                _ => null
            };
            return key is { Length: KeySize } ? key : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            // Unreadable, or protected for another user/machine: a new key is created
            Logger.Warning($"CredentialKeyStore: the credential key can't be used ({ex.GetType().Name}), stored passwords need to be entered again.");
            return null;
        }
    }

    private static void TryWriteKey(string keyPath, byte[] key)
    {
        try
        {
            var protection = PlatformProtection;
            var payload = protection == Protection.WindowsDpapi && OperatingSystem.IsWindows()
                ? ProtectedData.Protect(key, DpapiEntropy, DataProtectionScope.CurrentUser)
                : key;

            var content = new byte[HeaderSize + payload.Length];
            Magic.CopyTo(content);
            content[3] = FormatVersion;
            content[4] = (byte)protection;
            payload.CopyTo(content.AsSpan(HeaderSize));

            WritePrivateFile(keyPath, content);
            if (OperatingSystem.IsWindows())
            {
                File.SetAttributes(keyPath, File.GetAttributes(keyPath) | FileAttributes.Hidden);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            Logger.Warning($"CredentialKeyStore: the credential key can't be stored ({ex.GetType().Name}).");
        }
    }

    /// <summary>
    /// Write a file which only the current user can read (on Unix-like systems), replacing it atomically,
    /// so a crash while writing never leaves a truncated file behind.
    /// </summary>
    internal static void WritePrivateFile(string path, byte[] content)
    {
        var tempPath = path + ".tmp";
        using (var stream = new FileStream(tempPath, CreatePrivateFileOptions()))
        {
            stream.Write(content);
        }
        RestrictToCurrentUser(tempPath);
        ClearHiddenAttribute(path);
        File.Move(tempPath, path, overwrite: true);
    }

    /// <inheritdoc cref="WritePrivateFile"/>
    internal static async Task WritePrivateFileAsync(string path, byte[] content)
    {
        var tempPath = path + ".tmp";
        await using (var stream = new FileStream(tempPath, CreatePrivateFileOptions()))
        {
            await stream.WriteAsync(content);
        }
        RestrictToCurrentUser(tempPath);
        ClearHiddenAttribute(path);
        File.Move(tempPath, path, overwrite: true);
    }

    /// <summary>
    /// The key file is hidden on Windows, replacing a hidden file can be refused (access denied)
    /// </summary>
    private static void ClearHiddenAttribute(string path)
    {
        if (OperatingSystem.IsWindows() && File.Exists(path))
        {
            File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.Hidden);
        }
    }

    private static FileStreamOptions CreatePrivateFileOptions()
    {
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }
        return options;
    }

    /// <summary>
    /// Make an existing file readable for the current user only (UnixCreateMode only applies to new files,
    /// and files written by older versions have the default mode).
    /// </summary>
    internal static void RestrictToCurrentUser(string path)
    {
        if (OperatingSystem.IsWindows() || !File.Exists(path))
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not fatal, e.g. a file system without Unix permissions
        }
    }
}
