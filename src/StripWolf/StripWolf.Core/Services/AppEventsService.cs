using System;
using StripWolf.Core.Models;

namespace StripWolf.Core.Services;

/// <summary>
/// Centralized implementation of IAppEventsService to publish application-wide events.
/// </summary>
public class AppEventsService : IAppEventsService
{
    public event EventHandler<string>? LocalComicImported;
    public event EventHandler<string>? KomgaBookDownloaded;
    public event EventHandler<ComicOpenedEventArgs>? ComicOpened;
    public event EventHandler? PageRead;

    public void RaiseLocalComicImported(string filePath)
    {
        LocalComicImported?.Invoke(this, filePath);
    }

    public void RaiseKomgaBookDownloaded(string bookId)
    {
        KomgaBookDownloaded?.Invoke(this, bookId);
    }

    public void RaiseComicOpened(int comicId, ComicSource source, string identifier)
    {
        ComicOpened?.Invoke(this, new ComicOpenedEventArgs(comicId, source, identifier));
    }

    public void RaisePageRead()
    {
        PageRead?.Invoke(this, EventArgs.Empty);
    }
}
