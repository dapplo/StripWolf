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
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading;

namespace StripWolf.Core.Services;

/// <summary>
/// Single instance handling for the desktop (Windows and Linux), used from Program.Main before Avalonia is started:
/// opening a comic from the file manager starts StripWolf, when it is already running the new process only passes
/// the file to the running instance (see <see cref="ActivationManager"/>) and exits, without paying the startup cost.
/// macOS is not handled here: LaunchServices already keeps one instance and passes files as Apple events.
/// </summary>
public static class SingleInstance
{
    // Session-local, matching the session id in ActivationManager.PipeName (on Linux .NET maps it to the user session)
    private const string MutexName = @"Local\StripWolf_Mutex";

    /// <summary>
    /// Message which only brings the running instance to the front, for a start without a file.
    /// </summary>
    public const string ShowMessage = "SHOW";

    /// <summary>
    /// Prefix of the message which opens a file in the running instance.
    /// </summary>
    public const string OpenMessagePrefix = "OPEN:";

    private static Mutex? _mutex;

    /// <summary>
    /// True when this process owns the single instance mutex, it then has to run the activation pipe server.
    /// </summary>
    public static bool IsPrimary => _mutex is not null;

    /// <summary>
    /// Claims the single instance mutex. When another instance is running, the file in the arguments (or a request to
    /// show itself) is passed to it and false is returned: the caller must exit without starting the application.
    /// </summary>
    /// <param name="args">The command line arguments, the first one is the file to open</param>
    public static bool TryClaimOrForward(string[] args)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            return true;
        }

        Mutex mutex;
        bool owned;
        try
        {
            // Not "createdNew": on Linux the mutex can still exist after StripWolf crashed, only owning it counts
            mutex = new Mutex(false, MutexName);
            try
            {
                owned = mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                // The previous instance ended without releasing it, the mutex is ours now
                owned = true;
            }
        }
        catch (Exception ex)
        {
            // Never prevent StripWolf from starting
            Debug.WriteLine($"SingleInstance: Mutex not available: {ex.Message}");
            return true;
        }

        if (owned)
        {
            _mutex = mutex;
            return true;
        }

        mutex.Dispose();
        Forward(CreateMessage(args));
        return false;
    }

    /// <summary>
    /// Releases the single instance mutex, on shutdown.
    /// </summary>
    public static void Release()
    {
        var mutex = Interlocked.Exchange(ref _mutex, null);
        if (mutex is null)
        {
            return;
        }

        try
        {
            mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not released on the thread which owns it (shutdown runs on another thread), disposing is enough
        }
        mutex.Dispose();
    }

    private static string CreateMessage(string[] args)
    {
        if (args is not { Length: > 0 } || string.IsNullOrWhiteSpace(args[0]))
        {
            return ShowMessage;
        }

        var filePath = args[0];
        try
        {
            // The running instance has another working directory
            filePath = Path.GetFullPath(filePath);
        }
        catch
        {
            // Pass it on as it is
        }
        return OpenMessagePrefix + filePath;
    }

    private static void Forward(string message)
    {
        if (OperatingSystem.IsWindows())
        {
            // This process was started by the user and may set the foreground window, the running instance may not
            // unless we allow it: otherwise its window only flashes in the taskbar.
            AllowSetForegroundWindow(AsfwAny);
        }

        try
        {
            using var pipeClient = new NamedPipeClientStream(".", ActivationManager.PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            // The running instance may still be starting (it opens the pipe after its services are created)
            pipeClient.Connect(3000);
            using var writer = new StreamWriter(pipeClient, System.Text.Encoding.UTF8);
            writer.WriteLine(message);
            writer.Flush();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SingleInstance: Could not reach the running instance: {ex.Message}");
        }
    }

    private const int AsfwAny = -1;

    [DllImport("user32.dll")]
    private static extern int AllowSetForegroundWindow(int dwProcessId);
}
