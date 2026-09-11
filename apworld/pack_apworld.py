"""
Packages apworld/ballxpit/ into ballxpit.apworld with explicit directory entries.

PowerShell's Compress-Archive (used previously) only stores file paths - it never
writes an explicit directory record for a folder like "ballxpit/docs/". Most zip
readers tolerate that (a file's own path implies its parent folders), but
Archipelago loads .apworld files via Python's zipimport, which turned out to be
stricter: it failed to find ballxpit/docs/setup_en.md when its parent had no
directory entry of its own, causing "generate the template YAML" to fail for at
least one player - even though ArchipelagoGenerate.exe seed generation (which
never touches that file) worked fine. Confirmed via a real player's screenshot
comparing a working apworld (docs shown as its own drwxr-xr-x entry) against our
release's (docs\\setup_en.md as a bare flat file path, no folder record).

Usage: py -3.12 pack_apworld.py
Run from this directory (apworld/). Produces ./ballxpit.apworld next to this script.
"""
import os
import zipfile

SRC_DIR = os.path.join(os.path.dirname(__file__), "ballxpit")
DEST = os.path.join(os.path.dirname(__file__), "ballxpit.apworld")

DIR_MODE = 0o40755 << 16  # stat.S_IFDIR | 0o755, shifted into external_attr's high word
FILE_MODE = 0o100644 << 16  # stat.S_IFREG | 0o644


def add_directory(zf: zipfile.ZipFile, arcname: str) -> None:
    info = zipfile.ZipInfo(arcname + "/")
    info.external_attr = DIR_MODE
    zf.writestr(info, b"")


def main() -> None:
    if os.path.exists(DEST):
        os.remove(DEST)

    with zipfile.ZipFile(DEST, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as zf:
        for dirpath, dirnames, filenames in os.walk(SRC_DIR):
            dirnames.sort()
            rel_dir = os.path.relpath(dirpath, os.path.dirname(SRC_DIR))
            arc_dir = rel_dir.replace(os.sep, "/")
            add_directory(zf, arc_dir)

            for filename in sorted(filenames):
                file_path = os.path.join(dirpath, filename)
                arcname = f"{arc_dir}/{filename}"
                info = zipfile.ZipInfo(arcname)
                info.external_attr = FILE_MODE
                info.compress_type = zipfile.ZIP_DEFLATED
                with open(file_path, "rb") as f:
                    zf.writestr(info, f.read())

    print(f"Wrote {DEST}")
    with zipfile.ZipFile(DEST) as zf:
        for n in zf.namelist():
            print(" ", n)


if __name__ == "__main__":
    main()
