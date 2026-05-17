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

namespace StripWolf.Services;

/// <summary>
/// Interface for rendering PDF pages to image files.
/// Platform-specific implementations provide the actual rendering capability.
/// </summary>
public interface IPdfRenderer
{
    /// <summary>
    /// The DPI to use when rendering PDF pages
    /// </summary>
    int RenderDpi { get; set; }

    /// <summary>
    /// The JPEG quality to use when saving pages (1-100)
    /// </summary>
    int JpegQuality { get; set; }

    /// <summary>
    /// Creates a reusable PDF render session for a document.
    /// </summary>
    /// <param name="pdfFilePath">Path to the PDF file</param>
    Task<IPdfRenderSession> CreateRenderSessionAsync(string pdfFilePath);

    /// <summary>
    /// Gets the number of pages in a PDF file
    /// </summary>
    /// <param name="pdfFilePath">Path to the PDF file</param>
    /// <returns>Number of pages in the PDF</returns>
    int GetPageCount(string pdfFilePath);

    /// <summary>
    /// Renders all pages of a PDF to JPG files in the specified output directory
    /// </summary>
    /// <param name="pdfFilePath">Path to the PDF file</param>
    /// <param name="outputDir">Directory where JPG files will be saved</param>
    /// <param name="progress">Optional progress reporter (0-1)</param>
    Task RenderPdfPagesToJpgAsync(string pdfFilePath, string outputDir, IProgress<double>? progress);

    /// <summary>
    /// Extracts metadata from a PDF file
    /// </summary>
    /// <param name="pdfFilePath">Path to the PDF file</param>
    /// <returns>PDF metadata, or null if no metadata is available</returns>
    PdfMetadata? GetMetadata(string pdfFilePath);
}

