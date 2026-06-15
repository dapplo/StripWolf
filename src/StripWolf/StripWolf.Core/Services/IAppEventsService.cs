using System;
using StripWolf.Core.Models;

namespace StripWolf.Core.Services;

public class ComicOpenedEventArgs : EventArgs
{
    public int ComicId { get; }
    public ComicSource Source { get; }
    public string Identifier { get; } // FilePath for local, BookId for Komga

    public ComicOpenedEventArgs(int comicId, ComicSource source, string identifier)
    {
        ComicId = comicId;
        Source = source;
        Identifier = identifier;
    }
}

/// <summary>
/// Centralized service to publish and subscribe to application-wide events.
/// </summary>
public interface IAppEventsService
{
    event EventHandler<string>? LocalComicImported;
    event EventHandler<string>? KomgaBookDownloaded;
    event EventHandler<ComicOpenedEventArgs>? ComicOpened;
    event EventHandler? PageRead;

    void RaiseLocalComicImported(string filePath);
    void RaiseKomgaBookDownloaded(string bookId);
    void RaiseComicOpened(int comicId, ComicSource source, string identifier);
    void RaisePageRead();
}
