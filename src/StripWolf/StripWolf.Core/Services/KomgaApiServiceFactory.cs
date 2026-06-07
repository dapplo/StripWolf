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
using StripWolf.Core.Models;

namespace StripWolf.Core.Services;

/// <summary>
/// Maintains a dedicated API service instance per configured Komga server.
/// </summary>
public sealed class KomgaApiServiceFactory : IDisposable
{
    private readonly ConcurrentDictionary<int, (string Signature, KomgaApiService Service)> _servicesByServerId = new();
    private bool _isDisposed;

    public KomgaApiService GetForServer(KomgaServer server)
    {
        if (server is null)
        {
            throw new ArgumentNullException(nameof(server));
        }

        if (server.Id <= 0)
        {
            throw new InvalidOperationException("Komga server must be saved before requesting a dedicated API service.");
        }

        ThrowIfDisposed();

        var signature = CreateSignature(server);
        var existing = _servicesByServerId.GetOrAdd(server.Id, _ =>
        {
            var created = new KomgaApiService();
            created.Configure(server);
            return (signature, created);
        });

        if (existing.Signature == signature)
        {
            return existing.Service;
        }

        var replacement = new KomgaApiService();
        replacement.Configure(server);
        _servicesByServerId[server.Id] = (signature, replacement);
        existing.Service.Dispose();
        return replacement;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        foreach (var (_, (_, service)) in _servicesByServerId)
        {
            service.Dispose();
        }

        _servicesByServerId.Clear();
    }

    private static string CreateSignature(KomgaServer server)
    {
        var headerPart = string.Join("|", server.CustomHeaders
            .Where(header => !string.IsNullOrWhiteSpace(header.Name))
            .Select(header => $"{header.Name}:{header.Value}")
            .OrderBy(value => value, StringComparer.Ordinal));

        return string.Join("||",
            server.BaseUrl ?? string.Empty,
            server.Username ?? string.Empty,
            server.Password ?? string.Empty,
            server.ApiKey ?? string.Empty,
            server.BypassSslValidation.ToString(),
            headerPart);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }
}
