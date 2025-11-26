using Kom2go.Data;
using Kom2go.Models;
using Kom2go.Models.Komga;

namespace Kom2go.Services;

/// <summary>
/// Service for managing the local comic library
/// </summary>
public class LibraryService
{
    private readonly DatabaseService _databaseService;
    private readonly ComicReaderService _comicReaderService;
    private readonly KomgaApiService _komgaApiService;
    private readonly string _comicsDirectory;
    private readonly string _coversDirectory;

    public LibraryService(
        DatabaseService databaseService,
        ComicReaderService comicReaderService,
        KomgaApiService komgaApiService)
    {
        _databaseService = databaseService;
        _comicReaderService = comicReaderService;
        _komgaApiService = komgaApiService;
        
        _comicsDirectory = Path.Combine(FileSystem.AppDataDirectory, "Comics");
        _coversDirectory = Path.Combine(FileSystem.AppDataDirectory, "Covers");
        
        Directory.CreateDirectory(_comicsDirectory);
        Directory.CreateDirectory(_coversDirectory);
    }

    /// <summary>
    /// Gets all comics in the local library
    /// </summary>
    public Task<List<Comic>> GetAllComicsAsync()
    {
        return _databaseService.GetComicsAsync();
    }

    /// <summary>
    /// Gets recently read or added comics
    /// </summary>
    public Task<List<Comic>> GetRecentComicsAsync(int count = 10)
    {
        return _databaseService.GetRecentComicsAsync(count);
    }

    /// <summary>
    /// Gets comics that are currently being read
    /// </summary>
    public Task<List<Comic>> GetInProgressComicsAsync()
    {
        return _databaseService.GetInProgressComicsAsync();
    }

    /// <summary>
    /// Gets a comic by ID
    /// </summary>
    public Task<Comic?> GetComicAsync(int id)
    {
        return _databaseService.GetComicAsync(id);
    }

    /// <summary>
    /// Imports a local comic file into the library
    /// </summary>
    public async Task<Comic> ImportLocalComicAsync(string filePath)
    {
        // Check if already imported
        var existing = await _databaseService.GetComicByFilePathAsync(filePath);
        if (existing is not null)
        {
            return existing;
        }

        var format = ComicReaderService.GetComicFormat(filePath);
        if (format == ComicFormat.Unknown)
        {
            throw new NotSupportedException("Unsupported comic format. Only CBZ and CBR files are supported.");
        }

        var (pageCount, fileSize) = await _comicReaderService.GetComicInfoAsync(filePath);
        
        // Create cover directory for this comic
        var comicId = Guid.NewGuid().ToString();
        var coverDir = Path.Combine(_coversDirectory, comicId);
        Directory.CreateDirectory(coverDir);
        
        // Extract cover
        string? coverPath = null;
        try
        {
            coverPath = await _comicReaderService.ExtractCoverAsync(filePath, coverDir);
        }
        catch
        {
            // Cover extraction failed, continue without cover
        }

        var comic = new Comic
        {
            Title = Path.GetFileNameWithoutExtension(filePath),
            FilePath = filePath,
            PageCount = pageCount,
            FileSize = fileSize,
            CoverPath = coverPath,
            Format = format,
            Source = ComicSource.Local,
            AddedDate = DateTime.UtcNow
        };

        await _databaseService.SaveComicAsync(comic);
        return comic;
    }

    /// <summary>
    /// Downloads a comic from Komga and adds it to the library
    /// </summary>
    public async Task<Comic> DownloadFromKomgaAsync(KomgaBook book, IProgress<double>? progress = null)
    {
        // Check if already downloaded
        var existing = await _databaseService.GetComicByKomgaIdAsync(book.Id);
        if (existing is not null)
        {
            return existing;
        }

        // Determine file extension from media type
        var extension = book.Media?.MediaType switch
        {
            "application/zip" => ".cbz",
            "application/x-rar-compressed" => ".cbr",
            _ => ".cbz"
        };

        var fileName = SanitizeFileName($"{book.SeriesTitle} - {book.Name}{extension}");
        var filePath = Path.Combine(_comicsDirectory, fileName);

        // Download the book
        var success = await _komgaApiService.DownloadBookToFileAsync(book.Id, filePath, progress);
        if (!success)
        {
            throw new Exception("Failed to download comic from Komga");
        }

        // Create cover directory
        var coverDir = Path.Combine(_coversDirectory, book.Id);
        Directory.CreateDirectory(coverDir);

        // Download thumbnail
        string? coverPath = null;
        var thumbnailData = await _komgaApiService.GetBookThumbnailAsync(book.Id);
        if (thumbnailData is not null)
        {
            coverPath = Path.Combine(coverDir, "cover.jpg");
            await File.WriteAllBytesAsync(coverPath, thumbnailData);
        }

        // Parse release date
        DateTime? releaseDate = null;
        if (!string.IsNullOrEmpty(book.Metadata?.ReleaseDate))
        {
            if (DateTime.TryParse(book.Metadata.ReleaseDate, out var parsed))
            {
                releaseDate = parsed;
            }
        }

        var comic = new Comic
        {
            KomgaId = book.Id,
            Title = book.Metadata?.Title ?? book.Name,
            SeriesName = book.SeriesTitle,
            Number = book.Number,
            Summary = book.Metadata?.Summary,
            Authors = book.Metadata?.Authors is not null 
                ? string.Join(", ", book.Metadata.Authors.Select(a => a.Name))
                : null,
            ReleaseDate = releaseDate,
            FilePath = filePath,
            PageCount = book.Media?.PagesCount ?? 0,
            FileSize = book.SizeBytes,
            CoverPath = coverPath,
            Format = ComicReaderService.GetComicFormat(filePath),
            Source = ComicSource.Komga,
            AddedDate = DateTime.UtcNow,
            CurrentPage = book.ReadProgress?.Page ?? 0,
            IsCompleted = book.ReadProgress?.Completed ?? false
        };

        await _databaseService.SaveComicAsync(comic);
        return comic;
    }

    /// <summary>
    /// Updates reading progress for a comic
    /// </summary>
    public async Task UpdateReadingProgressAsync(Comic comic, int currentPage)
    {
        var isCompleted = currentPage >= comic.PageCount - 1;
        
        await _databaseService.UpdateReadingProgressAsync(comic.Id, currentPage, isCompleted);
        
        // Sync with Komga if this is a Komga comic
        if (comic.Source == ComicSource.Komga && !string.IsNullOrEmpty(comic.KomgaId) && _komgaApiService.IsConfigured)
        {
            try
            {
                await _komgaApiService.UpdateReadProgressAsync(comic.KomgaId, currentPage + 1, isCompleted);
            }
            catch
            {
                // Failed to sync with Komga, continue anyway
            }
        }
    }

    /// <summary>
    /// Deletes a comic from the library
    /// </summary>
    public async Task DeleteComicAsync(Comic comic)
    {
        // Delete the file if it's in our managed directory
        if (comic.FilePath.StartsWith(_comicsDirectory) && File.Exists(comic.FilePath))
        {
            File.Delete(comic.FilePath);
        }

        // Delete cover
        if (!string.IsNullOrEmpty(comic.CoverPath) && File.Exists(comic.CoverPath))
        {
            var coverDir = Path.GetDirectoryName(comic.CoverPath);
            if (!string.IsNullOrEmpty(coverDir) && Directory.Exists(coverDir))
            {
                Directory.Delete(coverDir, true);
            }
        }

        await _databaseService.DeleteComicAsync(comic);
    }

    /// <summary>
    /// Scans a directory for comic files and imports them
    /// </summary>
    public async Task<List<Comic>> ScanAndImportDirectoryAsync(string directoryPath)
    {
        var comics = new List<Comic>();
        
        if (!Directory.Exists(directoryPath))
        {
            return comics;
        }

        var files = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cbz", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".cbr", StringComparison.OrdinalIgnoreCase));

        foreach (var file in files)
        {
            try
            {
                var comic = await ImportLocalComicAsync(file);
                comics.Add(comic);
            }
            catch
            {
                // Skip files that can't be imported
            }
        }

        return comics;
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
    }
}
