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

#if Windows
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace StripWolf.Core.Desktop.Services.Windows;

/// <summary>
/// Helper to handle per-user Windows file association registrations.
/// </summary>
[SupportedOSPlatform("windows")]
public static class FileAssociationHelper
{
    [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);

    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    private const int SHCNF_IDLIST = 0x0000;

    /// <summary>
    /// Registers supported comic book and document extensions under HKCU for the current user.
    /// </summary>
    public static void RegisterAssociations()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                return;
            }

            var extensions = new[] { ".cbz", ".cbr", ".cb7", ".cbt", ".pdf", ".epub" };
            bool anyChanged = false;

            foreach (var ext in extensions)
            {
                var progId = "StripWolf" + ext.Replace(".", "_");
                var desc = ext.ToUpperInvariant().Substring(1) + " Comic Archive";
                if (ext == ".pdf") desc = "PDF Document";
                if (ext == ".epub") desc = "EPUB Book";

                // Read current ProgID for the extension
                using (var extKey = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ext}"))
                {
                    var existingProgId = extKey?.GetValue("") as string;
                    if (existingProgId != progId)
                    {
                        Registry.SetValue($@"HKEY_CURRENT_USER\Software\Classes\{ext}", "", progId);
                        anyChanged = true;
                    }
                }

                // Write/update ProgID details
                var openCommand = $"\"{exePath}\" \"%1\"";
                using (var progKey = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{progId}\shell\open\command"))
                {
                    var existingCommand = progKey?.GetValue("") as string;
                    if (existingCommand != openCommand)
                    {
                        Registry.SetValue($@"HKEY_CURRENT_USER\Software\Classes\{progId}", "", desc);
                        Registry.SetValue($@"HKEY_CURRENT_USER\Software\Classes\{progId}\DefaultIcon", "", $"\"{exePath}\",0");
                        Registry.SetValue($@"HKEY_CURRENT_USER\Software\Classes\{progId}\shell\open\command", "", openCommand);
                        anyChanged = true;
                    }
                }
            }

            if (anyChanged)
            {
                SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to register Windows file associations: {ex.Message}");
        }
    }
}
#endif
