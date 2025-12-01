using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Kom2go.Models;
using Kom2go.Models.Komga;

namespace Kom2go.Services;

/// <summary>
/// Service for interacting with Komga API
/// </summary>
public class KomgaApiService : IDisposable
{
    private HttpClient? _httpClient;
    private KomgaServer? _currentServer;
    private readonly JsonSerializerOptions _jsonOptions;

    public KomgaApiService()
    {
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    /// <summary>
    /// Gets the base URL of the configured server
    /// </summary>
    public string? BaseUrl => _currentServer?.BaseUrl;

    /// <summary>
    /// Gets the thumbnail URL for a series
    /// </summary>
    public string GetSeriesThumbnailUrl(string seriesId)
    {
        if (_currentServer is null)
        {
            return string.Empty;
        }
        return $"{_currentServer.BaseUrl}/api/v1/series/{seriesId}/thumbnail";
    }

    /// <summary>
    /// Gets the thumbnail URL for a book
    /// </summary>
    public string GetBookThumbnailUrl(string bookId)
    {
        if (_currentServer is null)
        {
            return string.Empty;
        }
        return $"{_currentServer.BaseUrl}/api/v1/books/{bookId}/thumbnail";
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
        
        _httpClient?.Dispose();
        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(server.BaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(30)
        };
        
        // Set up Basic authentication
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{server.Username}:{server.Password}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>
    /// Tests the connection to the Komga server
    /// </summary>
    public async Task<bool> TestConnectionAsync()
    {
        if (!IsConfigured)
        {
            return false;
        }

        try
        {
            var response = await _httpClient!.GetAsync("api/v1/libraries");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    #region Libraries

    /// <summary>
    /// Gets all libraries from Komga
    /// </summary>
    public async Task<List<KomgaLibrary>> GetLibrariesAsync()
    {
        EnsureConfigured();
        
        var response = await _httpClient!.GetAsync("api/v1/libraries");
        response.EnsureSuccessStatusCode();
        
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<List<KomgaLibrary>>(json, _jsonOptions) ?? [];
    }

    #endregion

    #region Series

    /// <summary>
    /// Gets all series with pagination
    /// </summary>
    public async Task<KomgaPage<KomgaSeries>> GetSeriesAsync(int page = 0, int size = 20, string? libraryId = null, string? searchPrefix = null)
    {
        EnsureConfigured();
        
        var url = $"api/v1/series?page={page}&size={size}";
        if (!string.IsNullOrEmpty(libraryId))
        {
            url += $"&library_id={libraryId}";
        }
        if (!string.IsNullOrEmpty(searchPrefix))
        {
            var encodedPrefix = Uri.EscapeDataString(searchPrefix);
            url += $"&search={encodedPrefix}";
        }
        
        var response = await _httpClient!.GetAsync(url);
        response.EnsureSuccessStatusCode();
        
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<KomgaPage<KomgaSeries>>(json, _jsonOptions) ?? new KomgaPage<KomgaSeries>();
    }

    /// <summary>
    /// Searches for series by name
    /// </summary>
    public async Task<KomgaPage<KomgaSeries>> SearchSeriesAsync(string searchQuery, int page = 0, int size = 20)
    {
        EnsureConfigured();
        
        var encodedQuery = Uri.EscapeDataString(searchQuery);
        var url = $"api/v1/series?page={page}&size={size}&search={encodedQuery}";
        
        var response = await _httpClient!.GetAsync(url);
        response.EnsureSuccessStatusCode();
        
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<KomgaPage<KomgaSeries>>(json, _jsonOptions) ?? new KomgaPage<KomgaSeries>();
    }

    /// <summary>
    /// Searches for books by name
    /// </summary>
    public async Task<KomgaPage<KomgaBook>> SearchBooksAsync(string searchQuery, int page = 0, int size = 20)
    {
        EnsureConfigured();
        
        var encodedQuery = Uri.EscapeDataString(searchQuery);
        var url = $"api/v1/books?page={page}&size={size}&search={encodedQuery}";
        
        var response = await _httpClient!.GetAsync(url);
        response.EnsureSuccessStatusCode();
        
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<KomgaPage<KomgaBook>>(json, _jsonOptions) ?? new KomgaPage<KomgaBook>();
    }

    /// <summary>
    /// Gets a specific series by ID
    /// </summary>
    public async Task<KomgaSeries?> GetSeriesAsync(string seriesId)
    {
        EnsureConfigured();
        
        var response = await _httpClient!.GetAsync($"api/v1/series/{seriesId}");
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        
        response.EnsureSuccessStatusCode();
        
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<KomgaSeries>(json, _jsonOptions);
    }

    /// <summary>
    /// Gets series thumbnail
    /// </summary>
    public async Task<byte[]?> GetSeriesThumbnailAsync(string seriesId)
    {
        EnsureConfigured();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/v1/series/{seriesId}/thumbnail");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        var response = await _httpClient!.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        
        return await response.Content.ReadAsByteArrayAsync();
    }

    #endregion

    #region Books

    /// <summary>
    /// Gets books for a series with pagination
    /// </summary>
    public async Task<KomgaPage<KomgaBook>> GetBooksForSeriesAsync(string seriesId, int page = 0, int size = 20)
    {
        EnsureConfigured();
        
        var response = await _httpClient!.GetAsync($"api/v1/series/{seriesId}/books?page={page}&size={size}");
        response.EnsureSuccessStatusCode();
        
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<KomgaPage<KomgaBook>>(json, _jsonOptions) ?? new KomgaPage<KomgaBook>();
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
        
        var response = await _httpClient!.GetAsync(url);
        response.EnsureSuccessStatusCode();
        
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<KomgaPage<KomgaBook>>(json, _jsonOptions) ?? new KomgaPage<KomgaBook>();
    }

    /// <summary>
    /// Gets a specific book by ID
    /// </summary>
    public async Task<KomgaBook?> GetBookAsync(string bookId)
    {
        EnsureConfigured();
        
        var response = await _httpClient!.GetAsync($"api/v1/books/{bookId}");
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        
        response.EnsureSuccessStatusCode();
        
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<KomgaBook>(json, _jsonOptions);
    }

    /// <summary>
    /// Gets book thumbnail
    /// </summary>
    public async Task<byte[]?> GetBookThumbnailAsync(string bookId)
    {
        EnsureConfigured();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/v1/books/{bookId}/thumbnail");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        var response = await _httpClient!.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        
        return await response.Content.ReadAsByteArrayAsync();
    }

    /// <summary>
    /// Gets page information for a book
    /// </summary>
    public async Task<List<KomgaPageInfo>> GetBookPagesAsync(string bookId)
    {
        EnsureConfigured();
        
        var response = await _httpClient!.GetAsync($"api/v1/books/{bookId}/pages");
        response.EnsureSuccessStatusCode();
        
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<List<KomgaPageInfo>>(json, _jsonOptions) ?? [];
    }

    /// <summary>
    /// Gets a specific page image from a book
    /// </summary>
    public async Task<byte[]?> GetBookPageAsync(string bookId, int pageNumber)
    {
        EnsureConfigured();
        
        var response = await _httpClient!.GetAsync($"api/v1/books/{bookId}/pages/{pageNumber}");
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
    /// Downloads a book file to a local path
    /// </summary>
    public async Task<bool> DownloadBookToFileAsync(string bookId, string outputPath, IProgress<double>? progress = null)
    {
        EnsureConfigured();
        
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/v1/books/{bookId}/file");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        
        var response = await _httpClient!.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        var bytesRead = 0L;
        
        await using var contentStream = await response.Content.ReadAsStreamAsync();
        await using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
        
        var buffer = new byte[8192];
        int read;
        
        while ((read = await contentStream.ReadAsync(buffer)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read));
            bytesRead += read;
            
            if (totalBytes > 0)
            {
                progress?.Report((double)bytesRead / totalBytes);
            }
        }
        
        return true;
    }

    #endregion

    #region Read Progress

    /// <summary>
    /// Updates read progress for a book on Komga
    /// </summary>
    public async Task<bool> UpdateReadProgressAsync(string bookId, int page, bool completed = false)
    {
        EnsureConfigured();
        
        var payload = new
        {
            page,
            completed
        };
        
        var json = JsonSerializer.Serialize(payload, _jsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        
        var response = await _httpClient!.PatchAsync($"api/v1/books/{bookId}/read-progress", content);
        return response.IsSuccessStatusCode;
    }

    /// <summary>
    /// Marks a book as read on Komga
    /// </summary>
    public async Task<bool> MarkBookAsReadAsync(string bookId)
    {
        EnsureConfigured();
        
        var response = await _httpClient!.PostAsync($"api/v1/books/{bookId}/read-progress", null);
        return response.IsSuccessStatusCode;
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
        
        var response = await _httpClient!.GetAsync(url);
        response.EnsureSuccessStatusCode();
        
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<KomgaPage<KomgaReadList>>(json, _jsonOptions) ?? new KomgaPage<KomgaReadList>();
    }

    /// <summary>
    /// Gets a specific read list by ID
    /// </summary>
    public async Task<KomgaReadList?> GetReadListAsync(string readListId)
    {
        EnsureConfigured();
        
        var response = await _httpClient!.GetAsync($"api/v1/readlists/{readListId}");
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        
        response.EnsureSuccessStatusCode();
        
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<KomgaReadList>(json, _jsonOptions);
    }

    /// <summary>
    /// Gets read list thumbnail
    /// </summary>
    public async Task<byte[]?> GetReadListThumbnailAsync(string readListId)
    {
        EnsureConfigured();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/v1/readlists/{readListId}/thumbnail");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        var response = await _httpClient!.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        
        return await response.Content.ReadAsByteArrayAsync();
    }

    /// <summary>
    /// Gets books for a read list with pagination
    /// </summary>
    public async Task<KomgaPage<KomgaBook>> GetBooksForReadListAsync(string readListId, int page = 0, int size = 20)
    {
        EnsureConfigured();
        
        var response = await _httpClient!.GetAsync($"api/v1/readlists/{readListId}/books?page={page}&size={size}");
        response.EnsureSuccessStatusCode();
        
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<KomgaPage<KomgaBook>>(json, _jsonOptions) ?? new KomgaPage<KomgaBook>();
    }

    /// <summary>
    /// Gets the thumbnail URL for a read list
    /// </summary>
    public string GetReadListThumbnailUrl(string readListId)
    {
        if (_currentServer is null)
        {
            return string.Empty;
        }
        return $"{_currentServer.BaseUrl}/api/v1/readlists/{readListId}/thumbnail";
    }

    #endregion

    private void EnsureConfigured()
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Komga API service is not configured. Call Configure() first.");
        }
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
        _httpClient = null;
    }
}
