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
using System.Threading;
using System.Threading.Tasks;

namespace StripWolf.Core.Services;

/// <summary>
/// Service to listen to activation paths forwarded from the Gatekeeper stub.
/// </summary>
public class ActivationManager
{
    private CancellationTokenSource? _cts;
    private Task? _pipeServerTask;

    /// <summary>
    /// Event raised when a file path is received via the named pipe.
    /// </summary>
    public event Action<string>? PathReceived;

    /// <summary>
    /// Starts the background Named Pipe server.
    /// </summary>
    public void StartServer()
    {
        if (_pipeServerTask is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _pipeServerTask = Task.Run(() => RunPipeServerAsync(_cts.Token));
    }

    /// <summary>
    /// Stops the Named Pipe server.
    /// </summary>
    public void StopServer()
    {
        _cts?.Cancel();
        try
        {
            _pipeServerTask?.Wait(100);
        }
        catch
        {
            // Ignore
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            _pipeServerTask = null;
        }
    }

    private async Task RunPipeServerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var pipeServer = new NamedPipeServerStream(
                    "StripWolf_Activation_Pipe",
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                // Wait for a client connection
                await pipeServer.WaitForConnectionAsync(cancellationToken);

                using var reader = new StreamReader(pipeServer, System.Text.Encoding.UTF8);
                var message = await reader.ReadLineAsync(cancellationToken);
                
                if (message != null && message.StartsWith("OPEN:", StringComparison.Ordinal))
                {
                    var path = message.Substring(5);
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        PathReceived?.Invoke(path);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in Named Pipe Server: {ex.Message}");
                // Avoid tight error loop
                await Task.Delay(100, cancellationToken);
            }
        }
    }
}
