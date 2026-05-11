using StripWolf.Models;
using StripWolf.Models.Komga;

namespace StripWolf.Services;

public sealed class KomgaDownloadedFile
{
    public required KomgaBook Book { get; init; }

    public int? ServerId { get; init; }

    public required string FilePath { get; init; }

    public required ComicFormat Format { get; init; }

    public bool RequiresConversion { get; init; }
}
