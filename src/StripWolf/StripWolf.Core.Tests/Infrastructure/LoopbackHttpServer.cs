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

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace StripWolf.Core.Tests;

internal sealed record LoopbackRequest(string Method, string PathAndQuery, IReadOnlyDictionary<string, string> Headers, string Body);

internal sealed record LoopbackResponse(int StatusCode, string? Body = null, string ContentType = "application/json", byte[]? BinaryBody = null)
{
    public static LoopbackResponse Json(string json) => new(200, json);

    public static LoopbackResponse Binary(byte[] content, string contentType) => new(200, null, contentType, content);

    public static LoopbackResponse Status(int statusCode) => new(statusCode);
}

/// <summary>
/// A minimal HTTP/1.1 server on 127.0.0.1 with a random port.
/// Needed for services which create their own KomgaApiService (KomgaApiServiceFactory has no handler seam),
/// e.g. KomgaSyncService. One request per connection ("Connection: close"), bodies only via Content-Length.
/// </summary>
internal sealed class LoopbackHttpServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly ConcurrentQueue<LoopbackRequest> _requests = new();
    private readonly Task _acceptLoop;

    public LoopbackHttpServer(Func<LoopbackRequest, LoopbackResponse> handler)
    {
        Handler = handler;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        BaseUrl = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/";
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    public string BaseUrl { get; }

    public Func<LoopbackRequest, LoopbackResponse> Handler { get; set; }

    public IReadOnlyList<LoopbackRequest> Requests => _requests.ToArray();

    private async Task AcceptLoopAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cancellation.Token);
            }
            catch (Exception)
            {
                return;
            }

            _ = Task.Run(() => HandleClientAsync(client));
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using var ownedClient = client;
        try
        {
            var stream = client.GetStream();
            var head = await ReadHeadAsync(stream, _cancellation.Token);
            if (head is null)
            {
                return;
            }

            var lines = head.Split("\r\n");
            var requestLine = lines[0].Split(' ');
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines.Skip(1))
            {
                var separator = line.IndexOf(':');
                if (separator > 0)
                {
                    headers[line[..separator].Trim()] = line[(separator + 1)..].Trim();
                }
            }

            var body = string.Empty;
            if (headers.TryGetValue("Content-Length", out var lengthText) && int.TryParse(lengthText, out var length) && length > 0)
            {
                var buffer = new byte[length];
                await stream.ReadExactlyAsync(buffer, _cancellation.Token);
                body = Encoding.UTF8.GetString(buffer);
            }

            var request = new LoopbackRequest(requestLine[0], requestLine.Length > 1 ? requestLine[1] : "/", headers, body);
            _requests.Enqueue(request);

            LoopbackResponse response;
            try
            {
                response = Handler(request);
            }
            catch (Exception ex)
            {
                response = new LoopbackResponse(500, ex.Message, "text/plain");
            }

            var responseBody = response.BinaryBody ?? Encoding.UTF8.GetBytes(response.Body ?? string.Empty);
            var responseHead =
                $"HTTP/1.1 {response.StatusCode} {GetReasonPhrase(response.StatusCode)}\r\n" +
                $"Content-Type: {response.ContentType}\r\n" +
                $"Content-Length: {responseBody.Length}\r\n" +
                "Connection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(responseHead), _cancellation.Token);
            await stream.WriteAsync(responseBody, _cancellation.Token);
            await stream.FlushAsync(_cancellation.Token);
        }
        catch (Exception)
        {
            // The client went away or the server is shutting down
        }
    }

    private static async Task<string?> ReadHeadAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var bytes = new List<byte>(1024);
        var single = new byte[1];
        while (bytes.Count < 64 * 1024)
        {
            var read = await stream.ReadAsync(single, cancellationToken);
            if (read == 0)
            {
                return null;
            }

            bytes.Add(single[0]);
            var count = bytes.Count;
            if (count >= 4 &&
                bytes[count - 4] == (byte)'\r' &&
                bytes[count - 3] == (byte)'\n' &&
                bytes[count - 2] == (byte)'\r' &&
                bytes[count - 1] == (byte)'\n')
            {
                return Encoding.ASCII.GetString(bytes.ToArray(), 0, count - 4);
            }
        }

        return null;
    }

    private static string GetReasonPhrase(int statusCode)
    {
        return statusCode switch
        {
            200 => "OK",
            204 => "No Content",
            401 => "Unauthorized",
            404 => "Not Found",
            500 => "Internal Server Error",
            _ => "Status"
        };
    }

    public async ValueTask DisposeAsync()
    {
        _cancellation.Cancel();
        _listener.Stop();
        try
        {
            await _acceptLoop;
        }
        catch (Exception)
        {
            // ignored
        }

        _cancellation.Dispose();
    }
}
