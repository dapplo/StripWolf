#!/usr/bin/env python3
"""
Generates the small synthetic comic fixtures used by StripWolf.Core.Tests.

The generated files are committed, so this only has to be re-run when the fixtures should change.
Requirements: Python 3.9+, Pillow (>= 10.1), img2pdf, and the `7z` and `rar` command line tools on the PATH
(rar is needed for the CBR fixtures, which can't be created with free tools).

    python3 generate_fixtures.py

All fixtures contain the same four pages, deliberately stored in a scrambled physical order and with names that
only sort correctly with a natural sort:

    page1.jpg      300x450
    page2.png      300x450  (PNG on purpose: mixed formats in one archive)
    page3-4.jpg    600x450  (double page spread)
    page10.jpg     300x450

Plus entries that must be ignored by the reader: Thumbs.db, notes.txt and __MACOSX/._page1.jpg.
"""

import io
import os
import shutil
import subprocess
import sys
import tarfile
import tempfile
import zipfile

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
PAGES_DIR = os.path.join(HERE, "pages")

# Natural order of the pages (this is what the tests expect from the reader)
PAGES = [
    ("page1.jpg", 300, 450, "1", (230, 80, 60)),
    ("page2.png", 300, 450, "2", (60, 140, 220)),
    ("page3-4.jpg", 600, 450, "3-4", (70, 170, 90)),
    ("page10.jpg", 300, 450, "10", (200, 160, 40)),
]

# Physical order inside the archives: scrambled, so a reader that relies on the archive order fails
PHYSICAL_ORDER = ["page10.jpg", "page2.png", "page1.jpg", "page3-4.jpg"]

JUNK = {
    "Thumbs.db": b"\xd0\xcf\x11\xe0 not a real thumbs.db",
    "notes.txt": b"These notes are not a page.\n",
    "__MACOSX/._page1.jpg": b"\x00\x05\x16\x07 AppleDouble resource fork, not an image",
}

# Note: <Title/><Series/><Number/> directly follow each other (regression test for the reader skipping
# every second element), there is an unknown element with children, and a Pages element.
COMIC_INFO = """<?xml version="1.0" encoding="utf-8"?>
<ComicInfo xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <Title>The Wolf Strip</Title><Series>StripWolf Test Series</Series><Number>7</Number>
  <Count>12</Count>
  <Volume>2</Volume>
  <Summary>A synthetic comic used by the automated tests.</Summary>
  <Year>2024</Year>
  <Month>5</Month>
  <Day>17</Day>
  <Writer>Alice Writer</Writer>
  <Penciller>Bob Penciller</Penciller>
  <Publisher>Dapplo Test Press</Publisher>
  <UnknownElement><Nested>ignored</Nested></UnknownElement>
  <PageCount>4</PageCount>
  <LanguageISO>en</LanguageISO>
  <Manga>No</Manga>
  <Pages>
    <Page Image="0" Type="FrontCover" ImageWidth="300" ImageHeight="450" />
    <Page Image="2" DoublePage="True" ImageWidth="600" ImageHeight="450" />
  </Pages>
</ComicInfo>
"""

FIXED_TIME = (2024, 5, 17, 12, 0, 0)
FIXED_EPOCH = 1715947200  # 2024-05-17 12:00:00 UTC


def font(size):
    try:
        return ImageFont.load_default(size=size)
    except TypeError:  # Pillow < 10.1
        return ImageFont.load_default()


def draw_page(width, height, label, color):
    image = Image.new("RGB", (width, height), (250, 248, 240))
    draw = ImageDraw.Draw(image)
    draw.rectangle([8, 8, width - 9, height - 9], outline=color, width=8)
    big = font(160 if len(label) <= 2 else 130)
    box = draw.textbbox((0, 0), label, font=big)
    x = (width - (box[2] - box[0])) / 2 - box[0]
    y = (height - (box[3] - box[1])) / 2 - box[1]
    draw.text((x, y), label, fill=color, font=big)
    draw.text((20, height - 40), "StripWolf test page", fill=(90, 90, 90), font=font(18))
    return image


def write_pages():
    os.makedirs(PAGES_DIR, exist_ok=True)
    for name, width, height, label, color in PAGES:
        image = draw_page(width, height, label, color)
        path = os.path.join(PAGES_DIR, name)
        if name.endswith(".png"):
            image = image.quantize(colors=16)
            image.save(path, "PNG", optimize=True)
        else:
            image.save(path, "JPEG", quality=60, optimize=True)


def page_bytes(name):
    with open(os.path.join(PAGES_DIR, name), "rb") as f:
        return f.read()


def entries(with_comic_info=True, with_junk=True):
    """(archive name, bytes) in physical order"""
    result = [(name, page_bytes(name)) for name in PHYSICAL_ORDER]
    if with_junk:
        result.insert(1, ("Thumbs.db", JUNK["Thumbs.db"]))
        result.append(("notes.txt", JUNK["notes.txt"]))
        result.append(("__MACOSX/._page1.jpg", JUNK["__MACOSX/._page1.jpg"]))
    if with_comic_info:
        result.insert(2, ("ComicInfo.xml", COMIC_INFO.encode("utf-8")))
    return result


def write_zip(path, items):
    with zipfile.ZipFile(path, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for name, data in items:
            info = zipfile.ZipInfo(name, date_time=FIXED_TIME)
            info.compress_type = zipfile.ZIP_DEFLATED
            archive.writestr(info, data)


def write_tar(path, items):
    with tarfile.open(path, "w", format=tarfile.USTAR_FORMAT) as archive:
        for name, data in items:
            info = tarfile.TarInfo(name)
            info.size = len(data)
            info.mtime = FIXED_EPOCH
            info.mode = 0o644
            archive.addfile(info, io.BytesIO(data))


def stage(items):
    """Write the entries to a temporary directory, returns (directory, relative names in physical order)"""
    directory = tempfile.mkdtemp(prefix="stripwolf_fixture_")
    names = []
    for name, data in items:
        target = os.path.join(directory, *name.split("/"))
        os.makedirs(os.path.dirname(target), exist_ok=True)
        with open(target, "wb") as f:
            f.write(data)
        os.utime(target, (FIXED_EPOCH, FIXED_EPOCH))
        names.append(name)
    return directory, names


def run(command, cwd):
    print("  $", " ".join(command))
    subprocess.run(command, cwd=cwd, check=True, stdout=subprocess.DEVNULL)


def write_7z(path, items, solid):
    directory, names = stage(items)
    try:
        if os.path.exists(path):
            os.remove(path)
        run(["7z", "a", "-t7z", "-mx=5", "-ms=" + ("on" if solid else "off"), path] + names, directory)
    finally:
        shutil.rmtree(directory)


def write_rar(path, items, solid):
    if shutil.which("rar") is None:
        print("  WARNING: rar not found, skipping", os.path.basename(path))
        return
    directory, names = stage(items)
    try:
        if os.path.exists(path):
            os.remove(path)
        # rar keeps the relative paths by default, -m3: normal compression, -idq: quiet
        command = ["rar", "a", "-m3", "-idq"]
        if solid:
            command.append("-s")
        run(command + [path] + names, directory)
    finally:
        shutil.rmtree(directory)


def write_pdf(path):
    import img2pdf
    # img2pdf embeds JPEGs as-is, the PNG is re-encoded losslessly
    images = [os.path.join(PAGES_DIR, name) for name, *_ in PAGES]
    with open(path, "wb") as f:
        f.write(img2pdf.convert(images, title="The Wolf Strip (PDF)", author="Alice Writer",
                                creationdate=None, moddate=None, nodate=True))


def write_epub(path):
    """Fixed layout, image only EPUB 3 with a nav document, an NCX and a cover image"""
    chapters = []
    for index, (name, width, height, label, _) in enumerate(PAGES, start=1):
        body = f'<img src="../images/{name}" alt="Page {label}" width="{width}" height="{height}"/>'
        if index == 2:
            # Unsafe markup: the converter must strip it before it reaches the WebView
            body += ('<script>alert("stripwolf-script")</script>'
                     '<a href="javascript:alert(\'stripwolf-href\')" onclick="alert(\'stripwolf-onclick\')"> </a>')
        chapters.append((f"page{index}.xhtml", width, height, body))

    container = """<?xml version="1.0" encoding="UTF-8"?>
<container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
  <rootfiles>
    <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
  </rootfiles>
</container>
"""
    manifest = [
        '<item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/>',
        '<item id="ncx" href="toc.ncx" media-type="application/x-dtbncx+xml"/>',
    ]
    for index, (name, *_rest) in enumerate(PAGES, start=1):
        media_type = "image/png" if name.endswith(".png") else "image/jpeg"
        properties = ' properties="cover-image"' if index == 1 else ""
        manifest.append(f'<item id="img{index}" href="images/{name}" media-type="{media_type}"{properties}/>')
    for index, (chapter, *_rest) in enumerate(chapters, start=1):
        manifest.append(f'<item id="p{index}" href="pages/{chapter}" media-type="application/xhtml+xml"/>')
    spine = "\n    ".join(f'<itemref idref="p{index}"/>' for index in range(1, len(chapters) + 1))

    opf = f"""<?xml version="1.0" encoding="UTF-8"?>
<package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="bookid" prefix="rendition: http://www.idpf.org/vocab/rendition/#">
  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
    <dc:identifier id="bookid">urn:uuid:5f0c7c3e-8d3b-4c55-9a59-0a7b3f6e2a11</dc:identifier>
    <dc:title>The Wolf Strip (EPUB)</dc:title>
    <dc:creator>Alice Writer</dc:creator>
    <dc:description>A fixed layout test EPUB.</dc:description>
    <dc:language>en</dc:language>
    <meta property="dcterms:modified">2024-05-17T12:00:00Z</meta>
    <meta property="rendition:layout">pre-paginated</meta>
    <meta property="rendition:spread">auto</meta>
    <meta name="cover" content="img1"/>
  </metadata>
  <manifest>
    {chr(10).join("    " + item for item in manifest).strip()}
  </manifest>
  <spine toc="ncx">
    {spine}
  </spine>
</package>
"""
    nav_items = "\n".join(
        f'      <li><a href="pages/{chapter}">Page {index}</a></li>' for index, (chapter, *_rest) in enumerate(chapters, start=1))
    nav = f"""<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE html>
<html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
<head><title>Contents</title></head>
<body>
  <nav epub:type="toc" id="toc">
    <ol>
{nav_items}
    </ol>
  </nav>
</body>
</html>
"""
    nav_points = "\n".join(
        f'    <navPoint id="np{index}" playOrder="{index}"><navLabel><text>Page {index}</text></navLabel>'
        f'<content src="pages/{chapter}"/></navPoint>'
        for index, (chapter, *_rest) in enumerate(chapters, start=1))
    ncx = f"""<?xml version="1.0" encoding="UTF-8"?>
<ncx xmlns="http://www.daisy.org/z3986/2005/ncx/" version="2005-1">
  <head><meta name="dtb:uid" content="urn:uuid:5f0c7c3e-8d3b-4c55-9a59-0a7b3f6e2a11"/></head>
  <docTitle><text>The Wolf Strip (EPUB)</text></docTitle>
  <navMap>
{nav_points}
  </navMap>
</ncx>
"""

    with zipfile.ZipFile(path, "w") as archive:
        # The mimetype has to be the first entry and stored uncompressed
        info = zipfile.ZipInfo("mimetype", date_time=FIXED_TIME)
        info.compress_type = zipfile.ZIP_STORED
        archive.writestr(info, "application/epub+zip")

        def add(name, data, compress=zipfile.ZIP_DEFLATED):
            entry = zipfile.ZipInfo(name, date_time=FIXED_TIME)
            entry.compress_type = compress
            archive.writestr(entry, data)

        add("META-INF/container.xml", container)
        add("OEBPS/content.opf", opf)
        add("OEBPS/nav.xhtml", nav)
        add("OEBPS/toc.ncx", ncx)
        for name, *_rest in PAGES:
            add(f"OEBPS/images/{name}", page_bytes(name), zipfile.ZIP_STORED)
        for chapter, width, height, body in chapters:
            add(f"OEBPS/pages/{chapter}", f"""<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE html>
<html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
<head>
  <title>{chapter}</title>
  <meta name="viewport" content="width={width}, height={height}"/>
</head>
<body style="margin:0">{body}</body>
</html>
""")


def main():
    os.chdir(HERE)
    print("Drawing pages")
    write_pages()

    print("CBZ")
    write_zip("comic_with_info.cbz", entries())
    write_zip("comic_no_info.cbz", entries(with_comic_info=False))
    # Pages in sub folders: ch2 must come before ch10, files inside a folder are sorted naturally as well
    write_zip("comic_subfolders.cbz", [
        ("ch10/p1.jpg", page_bytes("page10.jpg")),
        ("ch2/p10.jpg", page_bytes("page3-4.jpg")),
        ("ch2/p1.jpg", page_bytes("page1.jpg")),
        ("ch2/p2.png", page_bytes("page2.png")),
    ])

    print("CBT")
    write_tar("comic.cbt", entries())

    print("CB7")
    write_7z(os.path.join(HERE, "comic.cb7"), entries(), solid=False)
    write_7z(os.path.join(HERE, "comic_solid.cb7"), entries(), solid=True)

    print("CBR")
    write_rar(os.path.join(HERE, "comic.cbr"), entries(), solid=False)
    write_rar(os.path.join(HERE, "comic_solid.cbr"), entries(), solid=True)

    print("PDF")
    write_pdf("comic.pdf")

    print("EPUB")
    write_epub("comic.epub")

    total = 0
    for name in sorted(os.listdir(HERE)):
        full = os.path.join(HERE, name)
        if os.path.isfile(full) and not name.endswith(".py"):
            total += os.path.getsize(full)
    for name in os.listdir(PAGES_DIR):
        total += os.path.getsize(os.path.join(PAGES_DIR, name))
    print(f"Done, total fixture size: {total / 1024:.0f} KiB")
    return 0


if __name__ == "__main__":
    sys.exit(main())
