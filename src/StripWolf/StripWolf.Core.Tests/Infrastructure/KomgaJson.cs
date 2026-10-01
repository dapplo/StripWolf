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

using System.Globalization;

namespace StripWolf.Core.Tests;

/// <summary>
/// Canned Komga API responses (shapes as returned by Komga 1.x)
/// </summary>
internal static class KomgaJson
{
    public const string Libraries = """
        [
          {
            "id": "0LIB1",
            "name": "Comics",
            "root": "/data/comics",
            "importComicInfoBook": true,
            "importComicInfoSeries": true,
            "importComicInfoCollection": true,
            "importComicInfoReadList": true,
            "importEpubBook": true,
            "importEpubSeries": true,
            "importMylarSeries": true,
            "importLocalArtwork": true,
            "importBarcodeIsbn": false,
            "scanForceModifiedTime": false,
            "scanDeep": false,
            "repairExtensions": false,
            "convertToCbz": false,
            "emptyTrashAfterScan": false,
            "seriesCover": "FIRST",
            "hashFiles": true,
            "hashPages": false,
            "analyzeFile": true,
            "oneshotsDirectory": null,
            "unavailable": false
          },
          {
            "id": "0LIB2",
            "name": "Manga",
            "root": "/data/manga",
            "seriesCover": "FIRST",
            "unavailable": true
          }
        ]
        """;

    public const string Series = """
        {
          "id": "0SER1",
          "libraryId": "0LIB1",
          "name": "Wolf Strips",
          "url": "/data/comics/Wolf Strips",
          "created": "2024-05-17T12:00:00Z",
          "lastModified": "2024-05-18T12:00:00Z",
          "fileLastModified": "2024-05-17T11:00:00Z",
          "booksCount": 3,
          "booksReadCount": 1,
          "booksUnreadCount": 1,
          "booksInProgressCount": 1,
          "metadata": {
            "status": "ONGOING",
            "statusLock": false,
            "title": "Wolf Strips",
            "titleLock": false,
            "titleSort": "Wolf Strips",
            "titleSortLock": false,
            "summary": "Series summary",
            "summaryLock": false,
            "readingDirection": "LEFT_TO_RIGHT",
            "readingDirectionLock": false,
            "publisher": "Dapplo Test Press",
            "publisherLock": false,
            "ageRating": 12,
            "ageRatingLock": false,
            "language": "en",
            "languageLock": false,
            "genres": ["Comedy"],
            "genresLock": false,
            "tags": ["wolves"],
            "tagsLock": false,
            "totalBookCount": 3,
            "totalBookCountLock": false,
            "sharingLabels": [],
            "sharingLabelsLock": false,
            "links": [],
            "linksLock": false,
            "alternateTitles": [],
            "alternateTitlesLock": false,
            "created": "2024-05-17T12:00:00Z",
            "lastModified": "2024-05-18T12:00:00Z"
          },
          "booksMetadata": {
            "authors": [],
            "tags": [],
            "releaseDate": null,
            "summary": "",
            "summaryNumber": "",
            "created": "2024-05-17T12:00:00Z",
            "lastModified": "2024-05-18T12:00:00Z"
          },
          "deleted": false,
          "oneshot": false
        }
        """;

    public static string SeriesPage(int number, bool last) => $$"""
        {
          "content": [ {{Series}} ],
          "pageable": { "pageNumber": {{number}}, "pageSize": 1, "offset": {{number}}, "paged": true, "unpaged": false },
          "totalElements": 2,
          "totalPages": 2,
          "last": {{(last ? "true" : "false")}},
          "size": 1,
          "number": {{number}},
          "numberOfElements": 1,
          "first": {{(number == 0 ? "true" : "false")}},
          "empty": false
        }
        """;

    public static string Book(string id = "0BOOK1", string? readProgress = null) => $$"""
        {
          "id": "{{id}}",
          "seriesId": "0SER1",
          "seriesTitle": "Wolf Strips",
          "libraryId": "0LIB1",
          "name": "Wolf Strips 001",
          "url": "/data/comics/Wolf Strips/Wolf Strips 001.cbz",
          "number": 1.5,
          "created": "2024-05-17T12:00:00Z",
          "lastModified": "2024-05-18T12:00:00Z",
          "fileLastModified": "2024-05-17T11:00:00Z",
          "sizeBytes": 15105,
          "size": "14.8 KiB",
          "media": {
            "status": "READY",
            "mediaType": "application/zip",
            "pagesCount": 4,
            "comment": "",
            "epubDivinaCompatible": false,
            "epubIsKepub": false,
            "mediaProfile": "DIVINA"
          },
          "metadata": {
            "title": "The Wolf Strip",
            "titleLock": false,
            "summary": "Book summary",
            "summaryLock": false,
            "number": "1.5",
            "numberLock": false,
            "numberSort": 1.5,
            "numberSortLock": false,
            "releaseDate": "2024-05-17",
            "releaseDateLock": false,
            "authors": [ { "name": "Alice Writer", "role": "writer" }, { "name": "Bob Penciller", "role": "penciller" } ],
            "authorsLock": false,
            "tags": ["test"],
            "tagsLock": false,
            "isbn": "",
            "isbnLock": false,
            "links": [],
            "linksLock": false,
            "created": "2024-05-17T12:00:00Z",
            "lastModified": "2024-05-18T12:00:00Z"
          },
          "readProgress": {{readProgress ?? "null"}},
          "deleted": false,
          "fileHash": "abc123",
          "oneshot": false
        }
        """;

    public static string ReadProgress(int page, bool completed, DateTime lastModifiedUtc) => $$"""
        {
          "page": {{page}},
          "completed": {{(completed ? "true" : "false")}},
          "readDate": "{{FormatUtc(lastModifiedUtc)}}",
          "created": "2024-05-17T12:00:00Z",
          "lastModified": "{{FormatUtc(lastModifiedUtc)}}",
          "deviceId": "",
          "deviceName": ""
        }
        """;

    private static string FormatUtc(DateTime value)
    {
        return value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    }

    public static string BooksPage(int number, bool last, params string[] ids) => $$"""
        {
          "content": [ {{string.Join(",", ids.Select(id => Book(id)))}} ],
          "totalElements": 3,
          "totalPages": 2,
          "last": {{(last ? "true" : "false")}},
          "size": 2,
          "number": {{number}},
          "numberOfElements": {{ids.Length}},
          "first": {{(number == 0 ? "true" : "false")}},
          "empty": {{(ids.Length == 0 ? "true" : "false")}}
        }
        """;

    public const string BookPages = """
        [
          { "number": 1, "fileName": "page1.jpg", "mediaType": "image/jpeg", "width": 300, "height": 450, "sizeBytes": 5448, "size": "5.3 KiB" },
          { "number": 2, "fileName": "page2.png", "mediaType": "image/png", "width": 300, "height": 450, "sizeBytes": 2044, "size": "2 KiB" }
        ]
        """;
}
