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
using System.Text;

namespace StripWolf.Core.Tests;

/// <summary>
/// A request as seen by the fake handler (the HttpRequestMessage itself is disposed by HttpClient)
/// </summary>
internal sealed record RecordedRequest(
    HttpMethod Method,
    Uri Uri,
    IReadOnlyDictionary<string, string> Headers,
    string? ContentType,
    string? Body)
{
    public string Path => Uri.AbsolutePath;

    public string Query => Uri.UnescapeDataString(Uri.Query);

    public string? Header(string name)
    {
        return Headers.TryGetValue(name, out var value) ? value : null;
    }
}

/// <summary>
/// In-memory Komga server for KomgaApiService: <see cref="CreateHandler"/> is passed as handler factory to the internal
/// KomgaApiService constructor. A new handler is created for every Configure (HttpClient disposes its handler),
/// the state (routes and recorded requests) lives here.
/// </summary>
internal sealed class FakeKomgaServer
{
    private readonly ConcurrentQueue<RecordedRequest> _requests = new();

    public FakeKomgaServer(Func<RecordedRequest, CancellationToken, Task<HttpResponseMessage>> responder)
    {
        Responder = responder;
    }

    public FakeKomgaServer(Func<RecordedRequest, HttpResponseMessage> responder)
        : this((request, _) => Task.FromResult(responder(request)))
    {
    }

    public Func<RecordedRequest, CancellationToken, Task<HttpResponseMessage>> Responder { get; set; }

    public IReadOnlyList<RecordedRequest> Requests => _requests.ToArray();

    public int HandlersCreated { get; private set; }

    public HttpMessageHandler CreateHandler()
    {
        HandlersCreated++;
        return new Handler(this);
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    public static HttpResponseMessage Status(HttpStatusCode statusCode)
    {
        return new HttpResponseMessage(statusCode);
    }

    public static HttpResponseMessage Bytes(byte[] content, string mediaType = "image/jpeg", HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new ByteArrayContent(content)
        };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);
        return response;
    }

    private sealed class Handler(FakeKomgaServer server) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in request.Headers)
            {
                headers[header.Key] = string.Join(", ", header.Value);
            }

            string? body = null;
            string? contentType = null;
            if (request.Content is not null)
            {
                body = await request.Content.ReadAsStringAsync(cancellationToken);
                contentType = request.Content.Headers.ContentType?.MediaType;
            }

            var recorded = new RecordedRequest(request.Method, request.RequestUri!, headers, contentType, body);
            server._requests.Enqueue(recorded);

            var response = await server.Responder(recorded, cancellationToken);
            response.RequestMessage ??= request;
            return response;
        }
    }
}
