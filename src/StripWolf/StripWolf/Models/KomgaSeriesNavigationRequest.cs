namespace StripWolf.Models;

/// <summary>
/// Represents a request to navigate to a Komga series, optionally on a specific configured server.
/// </summary>
public class KomgaSeriesNavigationRequest
{
    public required string SeriesId { get; init; }

    public int? ServerId { get; init; }
}
