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

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.IO.Pipelines;
using StripWolf.Core.Data;
using StripWolf.Core.Models;
using StripWolf.Core.Models.Komga;

namespace StripWolf.Core.Services;

public sealed record KomgaDownloadResult(bool Success, string? ErrorMessage = null);
public sealed record KomgaDownloadProgress(long DownloadedBytes, long? TotalBytes);

/// <summary>
/// Service for interacting with Komga API
/// </summary>
public class KomgaApiService : IDisposable
{
    private HttpClient? _httpClient;
    private KomgaServer? _currentServer;
    private static readonly TimeSpan ConnectionTestTimeout = TimeSpan.FromSeconds(8);

    public KomgaApiService()
    {
    }

    /// <summary>
    /// Gets the base URL of the configured server
    /// </summary>
    public string? BaseUrl => _currentServer?.BaseUrl;

    /// <summary>
    /// Gets the ID of the currently configured server, or null if not configured
    /// </summary>
    public int? CurrentServerId => _currentServer?.Id;

    /// <summary>
    /// Gets the thumbnail URL for a series
    /// </summary>
    public string GetSeriesThumbnailUrl(string seriesId)
    {
        return GetAbsoluteApiUrl($"api/v1/series/{seriesId}/thumbnail");
    }

    /// <summary>
    /// Gets the thumbnail URL for a book
    /// </summary>
    public string GetBookThumbnailUrl(string bookId)
    {
        return GetAbsoluteApiUrl($"api/v1/books/{bookId}/thumbnail");
    }

    /// <summary>
    /// Gets whether the service is configured with a server
    /// </summary>
    public bool IsConfigured => _currentServer is not null && _httpClient is not null;

    /// <summary>
    /// Configures the service with a Komga server
    /// </summary>
    public void Configure(KomgaServer server)
    {
        _currentServer = server;
        
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            UseCookies = true,
            CookieContainer = new CookieContainer()
        };

        if (server.BypassSslValidation)
        {
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }
        
        var backgroundHandler = new BackgroundHttpHandler(handler);
        
        _httpClient?.Dispose();
        _httpClient = new HttpClient(backgroundHandler)
        {
            BaseAddress = new Uri(NormalizeServerBaseUrl(server.BaseUrl).TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromMinutes(20)
        };
        
        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // Add custom headers
        foreach (var header in server.CustomHeaders)
        {
            if (!string.IsNullOrWhiteSpace(header.Name) && !string.IsNullOrWhiteSpace(header.Value))
            {
                _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(header.Name, header.Value);
            }
        }

        // Set up authentication
        if (!string.IsNullOrEmpty(server.ApiKey))
        {
            // API Key authentication (preferred)
            _httpClient.DefaultRequestHeaders.Remove("X-API-Key");
            _httpClient.DefaultRequestHeaders.Add("X-API-Key", server.ApiKey);
            _httpClient.DefaultRequestHeaders.Authorization = null;
        }
        else if (!string.IsNullOrEmpty(server.Username))
        {
            // Fallback to Basic authentication
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{server.Username}:{server.Password}"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            _httpClient.DefaultRequestHeaders.Remove("X-API-Key");
        }
    }

    /// <summary>
    /// Tests the connection to the Komga server
    /// </summary>
    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var result = await TestConnectionWithDetailsAsync(cancellationToken);
        return result.Success;
    }

    /// <summary>
    /// Tests the connection to the Komga server and returns diagnostics for failures.
    /// </summary>
    public async Task<(bool Success, string? ErrorMessage)> TestConnectionWithDetailsAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return (false, "Service is not configured");
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(ConnectionTestTimeout);

            using var response = await _httpClient!.GetAsync("api/v1/libraries", HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
            if (response.IsSuccessStatusCode)
            {
                return (true, null);
            }

            var statusCodeText = $"{(int)response.StatusCode} {response.ReasonPhrase}".Trim();
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                statusCodeText += " (check server URL; use the Komga base URL without /api or /api/v1)";
            }

            return (false, statusCodeText);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return (false, "Operation cancelled");
        }
        catch (OperationCanceledException)
        {
            return (false, $"Connection test timed out after {ConnectionTestTimeout.TotalSeconds:0} seconds");
        }
        catch (Exception ex)
        {
            Logger.Error("Komga server connection check failed", ex);
            return (false, ex.Message);
        }
    }

    #region Libraries

    /// <summary>
    /// Gets all libraries from Komga
    /// </summary>
    public async Task<List<KomgaLibrary>> GetLibrariesAsync()
    {
        EnsureConfigured();
        
        var response = await _httpClient!.GetAsync("api/v1/libraries", HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        
        using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync(stream, StripWolfJsonContext.Default.ListKomgaLibrary) ?? [];
    }

    #endregion

    #region Series

    /// <summary>
    /// Gets all series with pagination
    /// </summary>
    public async Task<KomgaPage<KomgaSeries>> GetSeriesAsync(int page = 0, int size = 20, string? libraryId = null, string? searchPrefix = null)
    {
        EnsureConfigured();
        
        var url = $"api/v1/series?page={page}&size={size}&sort=metadata.titleSort,asc";
        if (!string.IsNullOrEmpty(libraryId))
        {
            url += $"&library_id={libraryId}";
        }
        if (!string.IsNullOrEmpty(searchPrefix))
        {
            // Use regex for precise "starts with" filtering
            string regex;
            if (searchPrefix == "0-9")
            {
                regex = "^[0-9].*";
            }
            else
            {
                regex = "^" + searchPrefix + ".*";
            }
            // Komga expects search_regex format: regex,field
            url += $"&search_regex={Uri.EscapeDataString(regex + ",TITLE")}";
        }
        
        var response = await _httpClient!.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        
        using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync(stream, StripWolfJsonContext.Default.KomgaPageKomgaSeries) ?? new KomgaPage<KomgaSeries>();
    }

    /// <summary>
    /// Searches for series by name
    /// </summary>
    public async Task<KomgaPage<KomgaSeries>> SearchSeriesAsync(string searchQuery, int page = 0, int size = 20)
    {
        EnsureConfigured();
        
        var encodedQuery = Uri.EscapeDataString(searchQuery);
        var url = $"api/v1/series?page={page}&size={size}&search={encodedQuery}";
        
        var response = await _httpClient!.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        
        using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync(stream, StripWolfJsonContext.Default.KomgaPageKomgaSeries) ?? new KomgaPage<KomgaSeries>();
    }

    /// <summary>
    /// Searches for books by name
    /// </summary>
    public async Task<KomgaPage<KomgaBook>> SearchBooksAsync(string searchQuery, int page = 0, int size = 20)
    {
        EnsureConfigured();
        
        var encodedQuery = Uri.EscapeDataString(searchQuery);
        var url = $"api/v1/books?page={page}&size={size}&search={encodedQuery}";
        
        var response = await _httpClient!.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        
        using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync(stream, StripWolfJsonContext.Default.KomgaPageKomgaBook) ?? new KomgaPage<KomgaBook>();
    }

    /// <summary>
    /// Gets a specific series by ID
    /// </summary>
    public async Task<KomgaSeries?> GetSeriesAsync(string seriesId)
    {
        EnsureConfigured();
        
        var response = await _httpClient!.GetAsync($"api/v1/series/{seriesId}", HttpCompletionOption.ResponseHeadersRead);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        
        response.EnsureSuccessStatusCode();
        
        using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync(stream, StripWolfJsonContext.Default.KomgaSeries);
    }

    /// <summary>
    /// Gets series thumbnail
    /// </summary>
    public async Task<byte[]?> GetSeriesThumbnailAsync(string seriesId, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/v1/series/{seriesId}/thumbnail");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        var response = await _httpClient!.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    #endregion

    #region Books

    /// <summary>
    /// Gets books for a series with pagination
    /// </summary>
    public async Task<KomgaPage<KomgaBook>> GetBooksForSeriesAsync(string seriesId, int page = 0, int size = 20)
    {
        EnsureConfigured();
        
        var response = await _httpClient!.GetAsync($"api/v1/series/{seriesId}/books?page={page}&size={size}", HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        
        using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync(stream, StripWolfJsonContext.Default.KomgaPageKomgaBook) ?? new KomgaPage<KomgaBook>();
    }

    /// <summary>
    /// Gets all books for a series across all pages.
    /// </summary>
    public async Task<List<KomgaBook>> GetAllBooksForSeriesAsync(string seriesId, int pageSize = 100)
    {
        EnsureConfigured();

        var books = new List<KomgaBook>();
        var page = 0;

        while (true)
        {
            var result = await GetBooksForSeriesAsync(seriesId, page, pageSize);
            books.AddRange(result.Content);

            if (result.Last)
            {
                break;
            }

            page++;
        }

        return books;
    }

    /// <summary>
    /// Gets all books with pagination
    /// </summary>
    public async Task<KomgaPage<KomgaBook>> GetBooksAsync(int page = 0, int size = 20, string? libraryId = null)
    {
        EnsureConfigured();
        
        var url = $"api/v1/books?page={page}&size={size}";
        if (!string.IsNullOrEmpty(libraryId))
        {
            url += $"&library_id={libraryId}";
        }
        
        var response = await _httpClient!.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        
        using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync(stream, StripWolfJsonContext.Default.KomgaPageKomgaBook) ?? new KomgaPage<KomgaBook>();
    }

    /// <summary>
    /// Gets a specific book by ID
    /// </summary>
    public async Task<KomgaBook?> GetBookAsync(string bookId)
    {
        EnsureConfigured();
        
        var response = await _httpClient!.GetAsync($"api/v1/books/{bookId}", HttpCompletionOption.ResponseHeadersRead);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        
        response.EnsureSuccessStatusCode();
        
        using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync(stream, StripWolfJsonContext.Default.KomgaBook);
    }

    /// <summary>
    /// Gets book thumbnail
    /// </summary>
    public async Task<byte[]?> GetBookThumbnailAsync(string bookId, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/v1/books/{bookId}/thumbnail");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        var response = await _httpClient!.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    /// <summary>
    /// Gets page information for a book
    /// </summary>
    public async Task<List<KomgaPageInfo>> GetBookPagesAsync(string bookId)
    {
        EnsureConfigured();
        
        var response = await _httpClient!.GetAsync($"api/v1/books/{bookId}/pages", HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        
        using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync(stream, StripWolfJsonContext.Default.ListKomgaPageInfo) ?? [];
    }

    /// <summary>
    /// Gets a specific page image from a book
    /// </summary>
    public async Task<byte[]?> GetBookPageAsync(string bookId, int pageNumber)
    {
        EnsureConfigured();
        
        var response = await _httpClient!.GetAsync($"api/v1/books/{bookId}/pages/{pageNumber}", HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        
        return await response.Content.ReadAsByteArrayAsync();
    }

    /// <summary>
    /// Downloads a book file
    /// </summary>
    public async Task<Stream?> DownloadBookAsync(string bookId)
    {
        EnsureConfigured();
        
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/v1/books/{bookId}/file");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        
        var response = await _httpClient!.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        
        return await response.Content.ReadAsStreamAsync();
    }

    /// <summary>
    /// Downloads a book file to a local path using System.IO.Pipelines for maximum performance.
    /// </summary>
    public async Task<KomgaDownloadResult> DownloadBookToFileAsync(
        string bookId,
        string outputPath,
        IProgress<double>? progress = null,
        IProgress<KomgaDownloadProgress>? detailedProgress = null,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var partialPath = outputPath + ".partial";
        const int maxAttempts = 4;
        const long chunkSize = 4L * 1024L * 1024L;
        string? lastErrorMessage = null;
        var downloadedBytes = File.Exists(partialPath)
            ? new FileInfo(partialPath).Length
            : 0L;
        long? totalBytes = null;
        double lastReportedProgress = -1;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (totalBytes.HasValue && downloadedBytes >= totalBytes.Value)
            {
                File.Move(partialPath, outputPath, true);
                progress?.Report(1.0);
                detailedProgress?.Report(new KomgaDownloadProgress(downloadedBytes, totalBytes));
                return new KomgaDownloadResult(true);
            }

            var currentChunkStart = downloadedBytes;
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, $"api/v1/books/{bookId}/file");
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
                    request.Headers.Range = new RangeHeaderValue(currentChunkStart, currentChunkStart + chunkSize - 1);

                    using var response = await _httpClient!.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                    {
                        var completeLength = response.Content.Headers.ContentRange?.Length;
                        if (completeLength.HasValue && downloadedBytes >= completeLength.Value)
                        {
                            File.Move(partialPath, outputPath, true);
                            progress?.Report(1.0);
                            detailedProgress?.Report(new KomgaDownloadProgress(downloadedBytes, completeLength));
                            return new KomgaDownloadResult(true);
                        }

                        downloadedBytes = 0;
                        totalBytes = null;
                        lastReportedProgress = -1;
                        File.Delete(partialPath);
                        break;
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        lastErrorMessage = $"Komga returned {(int)response.StatusCode} ({response.ReasonPhrase ?? response.StatusCode.ToString()}).";
                        if (attempt < maxAttempts && IsTransientStatusCode(response.StatusCode))
                        {
                            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken);
                            continue;
                        }

                        return new KomgaDownloadResult(false, lastErrorMessage);
                    }

                    var responseIsPartial = response.StatusCode == HttpStatusCode.PartialContent;
                    if (!responseIsPartial && currentChunkStart > 0)
                    {
                        downloadedBytes = 0;
                        totalBytes = null;
                        lastReportedProgress = -1;
                        File.Delete(partialPath);
                        break;
                    }

                    var serverTotalBytes = response.Content.Headers.ContentRange?.Length;
                    if (serverTotalBytes.HasValue)
                    {
                        totalBytes = serverTotalBytes.Value;
                    }
                    else if (!responseIsPartial && response.Content.Headers.ContentLength.HasValue)
                    {
                        totalBytes = response.Content.Headers.ContentLength.Value;
                    }

                    if (totalBytes.HasValue && downloadedBytes > 0 && progress is not null)
                    {
                        var existingProgress = (double)downloadedBytes / totalBytes.Value;
                        if (existingProgress - lastReportedProgress >= 0.01)
                        {
                            progress.Report(existingProgress);
                            detailedProgress?.Report(new KomgaDownloadProgress(downloadedBytes, totalBytes));
                            lastReportedProgress = existingProgress;
                        }
                    }

                    var bytesReadThisChunk = 0L;
                    await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    await using var fileStream = new FileStream(partialPath, downloadedBytes > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                    var reader = PipeReader.Create(contentStream, new StreamPipeReaderOptions(bufferSize: 64 * 1024, minimumReadSize: 16 * 1024, leaveOpen: false));
                    var writer = PipeWriter.Create(fileStream, new StreamPipeWriterOptions(minimumBufferSize: 64 * 1024, leaveOpen: false));
                    Exception? copyException = null;
                    try
                    {
                        while (true)
                        {
                            var result = await reader.ReadAsync(cancellationToken);
                            var buffer = result.Buffer;

                            if (buffer.IsEmpty && result.IsCompleted)
                            {
                                break;
                            }

                            foreach (var segment in buffer)
                            {
                                var destination = writer.GetSpan(segment.Length);
                                segment.Span.CopyTo(destination);
                                writer.Advance(segment.Length);
                                bytesReadThisChunk += segment.Length;

                                if (totalBytes.HasValue && progress is not null)
                                {
                                    var currentProgress = (double)(downloadedBytes + bytesReadThisChunk) / totalBytes.Value;
                                    if (currentProgress - lastReportedProgress >= 0.01 || currentProgress >= 1.0)
                                    {
                                        progress.Report(currentProgress);
                                        detailedProgress?.Report(new KomgaDownloadProgress(downloadedBytes + bytesReadThisChunk, totalBytes));
                                        lastReportedProgress = currentProgress;
                                    }
                                }
                            }

                            reader.AdvanceTo(buffer.End);
                            var flushResult = await writer.FlushAsync(cancellationToken);
                            if (flushResult.IsCompleted)
                            {
                                break;
                            }

                            if (result.IsCompleted)
                            {
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        copyException = ex;
                        throw;
                    }
                    finally
                    {
                        await writer.CompleteAsync(copyException);
                        await reader.CompleteAsync(copyException);
                    }

                    downloadedBytes += bytesReadThisChunk;
                    if (bytesReadThisChunk == 0)
                    {
                        return new KomgaDownloadResult(false, "Komga returned an empty chunk before download completion.");
                    }

                    if (!responseIsPartial || (totalBytes.HasValue && downloadedBytes >= totalBytes.Value))
                    {
                        File.Move(partialPath, outputPath, true);
                        progress?.Report(1.0);
                        detailedProgress?.Report(new KomgaDownloadProgress(downloadedBytes, totalBytes));
                        return new KomgaDownloadResult(true);
                    }

                    break;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < maxAttempts)
                {
                    lastErrorMessage = "Download timed out while reading data from Komga.";
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return new KomgaDownloadResult(false, lastErrorMessage ?? "Download timed out while reading data from Komga.");
                }
                catch (HttpRequestException ex) when (attempt < maxAttempts)
                {
                    lastErrorMessage = ex.Message;
                    if (TryFinalizeDownloadFromPartial(partialPath, outputPath, totalBytes, ref downloadedBytes, progress, detailedProgress))
                    {
                        return new KomgaDownloadResult(true);
                    }

                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken);
                }
                catch (IOException ex) when (attempt < maxAttempts)
                {
                    lastErrorMessage = ex.Message;
                    if (TryFinalizeDownloadFromPartial(partialPath, outputPath, totalBytes, ref downloadedBytes, progress, detailedProgress))
                    {
                        return new KomgaDownloadResult(true);
                    }

                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken);
                }
                catch (HttpRequestException ex)
                {
                    lastErrorMessage = ex.Message;
                    if (TryFinalizeDownloadFromPartial(partialPath, outputPath, totalBytes, ref downloadedBytes, progress, detailedProgress))
                    {
                        return new KomgaDownloadResult(true);
                    }

                    return new KomgaDownloadResult(false, lastErrorMessage);
                }
                catch (IOException ex)
                {
                    lastErrorMessage = ex.Message;
                    if (TryFinalizeDownloadFromPartial(partialPath, outputPath, totalBytes, ref downloadedBytes, progress, detailedProgress))
                    {
                        return new KomgaDownloadResult(true);
                    }

                    return new KomgaDownloadResult(false, lastErrorMessage);
                }
            }
        }
    }

    private static bool TryFinalizeDownloadFromPartial(
        string partialPath,
        string outputPath,
        long? totalBytes,
        ref long downloadedBytes,
        IProgress<double>? progress,
        IProgress<KomgaDownloadProgress>? detailedProgress)
    {
        if (!File.Exists(partialPath))
        {
            return false;
        }

        var partialLength = new FileInfo(partialPath).Length;
        if (partialLength > downloadedBytes)
        {
            downloadedBytes = partialLength;
        }

        if (!totalBytes.HasValue || partialLength < totalBytes.Value)
        {
            return false;
        }

        File.Move(partialPath, outputPath, true);
        progress?.Report(1.0);
        detailedProgress?.Report(new KomgaDownloadProgress(partialLength, totalBytes));
        return true;
    }

    private static bool IsTransientStatusCode(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.RequestTimeout or
            HttpStatusCode.TooManyRequests or
            HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or
            HttpStatusCode.GatewayTimeout;
    }

    #endregion

    #region Read Progress

    /// <summary>
    /// Updates read progress for a book on Komga
    /// </summary>
    public async Task<bool> UpdateReadProgressAsync(string bookId, int page, bool completed = false)
    {
        EnsureConfigured();
        
        var payload = new KomgaReadProgressUpdate
        {
            Page = page,
            Completed = completed
        };
        
        var json = JsonSerializer.Serialize(payload, StripWolfJsonContext.Default.KomgaReadProgressUpdate);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        
        var response = await _httpClient!.PatchAsync($"api/v1/books/{bookId}/read-progress", content);
        return response.IsSuccessStatusCode;
    }

    /// <summary>
    /// Marks a book as read on Komga
    /// </summary>
    public async Task<bool> MarkBookAsReadAsync(string bookId, int page = 0)
    {
        EnsureConfigured();
        
        return await UpdateReadProgressAsync(bookId, page, completed: true);
    }

    /// <summary>
    /// Deletes read progress for a book on Komga
    /// </summary>
    public async Task<bool> DeleteReadProgressAsync(string bookId)
    {
        EnsureConfigured();
        
        var response = await _httpClient!.DeleteAsync($"api/v1/books/{bookId}/read-progress");
        return response.IsSuccessStatusCode;
    }

    #endregion

    #region Read Lists

    /// <summary>
    /// Gets all read lists with pagination
    /// </summary>
    public async Task<KomgaPage<KomgaReadList>> GetReadListsAsync(int page = 0, int size = 20)
    {
        EnsureConfigured();
        
        var url = $"api/v1/readlists?page={page}&size={size}";
        
        var response = await _httpClient!.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        
        using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync(stream, StripWolfJsonContext.Default.KomgaPageKomgaReadList) ?? new KomgaPage<KomgaReadList>();
    }

    /// <summary>
    /// Gets all read lists across all pages.
    /// </summary>
    public async Task<List<KomgaReadList>> GetAllReadListsAsync(int pageSize = 100)
    {
        EnsureConfigured();

        var readLists = new List<KomgaReadList>();
        var page = 0;

        while (true)
        {
            var result = await GetReadListsAsync(page, pageSize);
            readLists.AddRange(result.Content);

            if (result.Last)
            {
                break;
            }

            page++;
        }

        return readLists;
    }

    /// <summary>
    /// Gets a specific read list by ID
    /// </summary>
    public async Task<KomgaReadList?> GetReadListAsync(string readListId)
    {
        EnsureConfigured();
        
        var response = await _httpClient!.GetAsync($"api/v1/readlists/{readListId}", HttpCompletionOption.ResponseHeadersRead);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        
        response.EnsureSuccessStatusCode();
        
        using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync(stream, StripWolfJsonContext.Default.KomgaReadList);
    }

    /// <summary>
    /// Updates a read list.
    /// </summary>
    public async Task<bool> UpdateReadListAsync(string readListId, string? name = null, string? summary = null, IReadOnlyCollection<string>? bookIds = null, bool? ordered = null)
    {
        EnsureConfigured();

        var payload = new KomgaReadListUpdate
        {
            Name = name,
            Summary = summary,
            BookIds = bookIds,
            Ordered = ordered
        };

        var json = JsonSerializer.Serialize(payload, StripWolfJsonContext.Default.KomgaReadListUpdate);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient!.PatchAsync($"api/v1/readlists/{readListId}", content);
        return response.IsSuccessStatusCode;
        }

    /// <summary>
    /// Gets read list thumbnail
    /// </summary>
    public async Task<byte[]?> GetReadListThumbnailAsync(string readListId, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/v1/readlists/{readListId}/thumbnail");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        var response = await _httpClient!.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    /// <summary>
    /// Gets books for a read list with pagination
    /// </summary>
    public async Task<KomgaPage<KomgaBook>> GetBooksForReadListAsync(string readListId, int page = 0, int size = 20)
    {
        EnsureConfigured();
        
        var response = await _httpClient!.GetAsync($"api/v1/readlists/{readListId}/books?page={page}&size={size}", HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        
        using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync(stream, StripWolfJsonContext.Default.KomgaPageKomgaBook) ?? new KomgaPage<KomgaBook>();
    }

    /// <summary>
    /// Gets the thumbnail URL for a read list
    /// </summary>
    public string GetReadListThumbnailUrl(string readListId)
    {
        return GetAbsoluteApiUrl($"api/v1/readlists/{readListId}/thumbnail");
    }

    #endregion

    #region Smart Lists (Keep Reading, On Deck, Recently Added)

    /// <summary>
    /// Gets books that are currently in progress (keep reading)
    /// </summary>
    public async Task<KomgaPage<KomgaBook>> GetBooksInProgressAsync(int page = 0, int size = 20)
    {
        EnsureConfigured();
        
        var url = $"api/v1/books?page={page}&size={size}&read_status=IN_PROGRESS&sort=readProgress.lastModified,desc";
        
        var response = await _httpClient!.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        
        using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync(stream, StripWolfJsonContext.Default.KomgaPageKomgaBook) ?? new KomgaPage<KomgaBook>();
    }

    /// <summary>
    /// Gets books on deck (first unread book of series with at least one book read)
    /// </summary>
    public async Task<KomgaPage<KomgaBook>> GetBooksOnDeckAsync(int page = 0, int size = 20)
    {
        EnsureConfigured();
        
        var url = $"api/v1/books/ondeck?page={page}&size={size}";
        
        var response = await _httpClient!.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        
        using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync(stream, StripWolfJsonContext.Default.KomgaPageKomgaBook) ?? new KomgaPage<KomgaBook>();
    }

    /// <summary>
    /// Gets recently added/updated books
    /// </summary>
    public async Task<KomgaPage<KomgaBook>> GetBooksLatestAsync(int page = 0, int size = 20)
    {
        EnsureConfigured();
        
        var url = $"api/v1/books/latest?page={page}&size={size}";
        
        var response = await _httpClient!.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        
        using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync(stream, StripWolfJsonContext.Default.KomgaPageKomgaBook) ?? new KomgaPage<KomgaBook>();
    }

    /// <summary>
    /// Gets recently added/updated series
    /// </summary>
    public async Task<KomgaPage<KomgaSeries>> GetSeriesLatestAsync(int page = 0, int size = 20)
    {
        EnsureConfigured();
        
        var url = $"api/v1/series/latest?page={page}&size={size}";
        
        var response = await _httpClient!.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        
        using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync(stream, StripWolfJsonContext.Default.KomgaPageKomgaSeries) ?? new KomgaPage<KomgaSeries>();
    }

    #endregion

    private void EnsureConfigured()
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Komga API service is not configured. Call Configure() first.");
        }
    }

    private string GetAbsoluteApiUrl(string relativePath)
    {
        if (!IsConfigured)
        {
            return string.Empty;
        }

        return new Uri(_httpClient!.BaseAddress!, relativePath).ToString();
    }

    private static string NormalizeServerBaseUrl(string baseUrl)
    {
        var trimmedBaseUrl = baseUrl.Trim().TrimEnd('/');
        if (!Uri.TryCreate(trimmedBaseUrl, UriKind.Absolute, out var parsedUri))
        {
            return trimmedBaseUrl;
        }

        var normalizedPath = parsedUri.AbsolutePath.TrimEnd('/');
        if (normalizedPath.EndsWith("/api/v1", StringComparison.OrdinalIgnoreCase))
        {
            normalizedPath = normalizedPath[..^7];
        }
        else if (normalizedPath.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
        {
            normalizedPath = normalizedPath[..^4];
        }

        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            normalizedPath = "/";
        }

        var builder = new UriBuilder(parsedUri)
        {
            Path = normalizedPath
        };

        return builder.Uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
        _httpClient = null;
    }
}

/// <summary>
/// A delegating handler that forces the underlying HTTP send operation to run on a background thread.
/// This prevents NetworkOnMainThreadException on Android when requests are initiated from the UI thread.
/// </summary>
internal sealed class BackgroundHttpHandler : DelegatingHandler
{
    public BackgroundHttpHandler(HttpMessageHandler innerHandler) : base(innerHandler)
    {
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.Run(() => base.SendAsync(request, cancellationToken), cancellationToken);
    }
}
