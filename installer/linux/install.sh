#!/bin/sh
# Registers StripWolf for the current user: adds it to the application menu, and lets the file manager open comic
# books (CBZ, CBR, CB7, CBT) with StripWolf by double-clicking them. PDF files can be opened with "Open With".
# StripWolf stays where it was extracted, run this script again after moving it.
#
#   ./install.sh               register StripWolf
#   ./install.sh --uninstall   remove the registration (the extracted files are not deleted)
set -e

DIR=$(cd "$(dirname "$0")" && pwd)
DATA_HOME=${XDG_DATA_HOME:-$HOME/.local/share}
APPLICATIONS="$DATA_HOME/applications"
ICONS="$DATA_HOME/icons/hicolor/256x256/apps"
DESKTOP_FILE="$APPLICATIONS/stripwolf.desktop"
# The MIME types of comic books in shared-mime-info, StripWolf becomes their default application
COMIC_TYPES="application/vnd.comicbook+zip application/x-cbz application/vnd.comicbook-rar application/x-cbr application/x-cb7 application/x-cbt"

refresh() {
    if command -v update-desktop-database >/dev/null 2>&1; then
        update-desktop-database "$APPLICATIONS" >/dev/null 2>&1 || true
    fi
    if command -v gtk-update-icon-cache >/dev/null 2>&1; then
        gtk-update-icon-cache -q -t "$DATA_HOME/icons/hicolor" >/dev/null 2>&1 || true
    fi
}

if [ "$1" = "--uninstall" ]; then
    rm -f "$DESKTOP_FILE" "$ICONS/stripwolf.png"
    refresh
    echo "StripWolf is no longer registered."
    exit 0
fi

if [ ! -x "$DIR/StripWolf" ]; then
    echo "StripWolf was not found in $DIR, run this script from the extracted StripWolf folder." >&2
    exit 1
fi

mkdir -p "$APPLICATIONS" "$ICONS"
# Exec is quoted in the desktop file: the characters which have a meaning inside the quotes get a backslash, then
# every backslash is doubled (the escaping of desktop file strings is applied before the quoting)
EXEC=$(printf '%s' "$DIR/StripWolf" | sed -e 's/[\\"`$]/\\&/g' -e 's/\\/\\\\/g')
export EXEC
# awk with index/substr, not sed or gsub: those would interpret characters of the path in the replacement
awk 'BEGIN { path = ENVIRON["EXEC"] }
     { i = index($0, "@EXEC@"); if (i) $0 = substr($0, 1, i - 1) path substr($0, i + 6); print }' \
    "$DIR/stripwolf.desktop.in" > "$DESKTOP_FILE"
cp "$DIR/stripwolf.png" "$ICONS/stripwolf.png"
refresh

if command -v xdg-mime >/dev/null 2>&1; then
    # shellcheck disable=SC2086
    xdg-mime default stripwolf.desktop $COMIC_TYPES || true
fi

echo "StripWolf is registered, comic books now open with StripWolf."
