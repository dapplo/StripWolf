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

using System;
using System.IO;
using System.Text;

namespace StripWolf.Core.Services;

/// <summary>
/// Thread-safe, lightweight rolling file logger with fallback capability.
/// Writes to the debug window and a persistent log file in local application data.
/// </summary>
public static class Logger
{
    private static readonly string LogFilePath;
    private static readonly Lock LockObj = new();
    private const long MaxLogSize = 5 * 1024 * 1024; // 5 MB

    static Logger()
    {
        try
        {
            var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appDir = Path.Combine(baseDir, "StripWolf");
            Directory.CreateDirectory(appDir);
            LogFilePath = Path.Combine(appDir, "stripwolf.log");
        }
        catch
        {
            try
            {
                // Fallback to temp folder if app data directory is not accessible
                LogFilePath = Path.Combine(Path.GetTempPath(), "stripwolf.log");
            }
            catch
            {
                // Fallback if everything fails
                LogFilePath = "stripwolf.log";
            }
        }
    }

    /// <summary>
    /// Log an informational message.
    /// </summary>
    public static void Info(string message) => Log("INFO", message);

    /// <summary>
    /// Log a warning message.
    /// </summary>
    public static void Warning(string message) => Log("WARN", message);

    /// <summary>
    /// Log an error message with optional exception context.
    /// </summary>
    public static void Error(string message, Exception? ex = null) => Log("ERROR", message, ex);

    private static void Log(string level, string message, Exception? ex = null)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var logMessage = $"[{timestamp}] [{level}] {message}";
        if (ex != null)
        {
            logMessage += $"{Environment.NewLine}Exception: {ex.GetType().FullName}: {ex.Message}{Environment.NewLine}Stack Trace:{Environment.NewLine}{ex.StackTrace}";
        }

        // Print to debug output for IDE/debugger (active in debug configurations)
        System.Diagnostics.Debug.WriteLine(logMessage);

        // Write to persistent log file
        try
        {
            lock (LockObj)
            {
                RotateLogFileIfNeeded();
                File.AppendAllText(LogFilePath, logMessage + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Ignore logging errors to avoid crashing the app
        }
    }

    private static void RotateLogFileIfNeeded()
    {
        try
        {
            if (File.Exists(LogFilePath))
            {
                var fileInfo = new FileInfo(LogFilePath);
                if (fileInfo.Length > MaxLogSize)
                {
                    var backupPath = LogFilePath + ".bak";
                    if (File.Exists(backupPath))
                    {
                        File.Delete(backupPath);
                    }
                    File.Move(LogFilePath, backupPath);
                }
            }
        }
        catch
        {
            // Ignore rotation errors
        }
    }
}
