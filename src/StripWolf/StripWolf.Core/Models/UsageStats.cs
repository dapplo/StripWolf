using System;
using SQLite;

namespace StripWolf.Core.Models;

/// <summary>
/// Model for logging usage statistics in the database.
/// </summary>
public class UsageStats
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public string Metric { get; set; } = string.Empty; // "KomgaDownload", "LocalImport", "ComicOpen", "PagesRead"

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public string? Metadata { get; set; }
}
