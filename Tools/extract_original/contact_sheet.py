#!/usr/bin/env python3
"""Tile PNGs into one labelled contact sheet for eyeballing an export.
Usage: contact_sheet.py out.png CELL_PX COLUMNS file_or_glob [file_or_glob ...]"""
import glob, os, sys
from PIL import Image, ImageDraw


def main():
    out, cell, cols = sys.argv[1], int(sys.argv[2]), int(sys.argv[3])
    files = []
    for g in sys.argv[4:]:
        files += sorted(glob.glob(g, recursive=True))
    if not files:
        sys.exit('no files')
    rows = (len(files) + cols - 1) // cols
    lab = 14
    sheet = Image.new('RGBA', (cols * cell, rows * (cell + lab)), (255, 255, 255, 255))
    d = ImageDraw.Draw(sheet)
    for k, f in enumerate(files):
        x, y = (k % cols) * cell, (k // cols) * (cell + lab)
        for cy in range(0, cell, 16):  # checkerboard shows transparency
            for cx in range(0, cell, 16):
                if (cx // 16 + cy // 16) % 2:
                    d.rectangle([x + cx, y + cy, x + cx + 15, y + cy + 15], fill=(225, 225, 225, 255))
        im = Image.open(f).convert('RGBA')
        im.thumbnail((cell - 4, cell - 4))
        sheet.alpha_composite(im, (x + (cell - im.width) // 2, y + (cell - im.height) // 2))
        name = os.path.splitext(os.path.relpath(f))[0].split('/')
        d.text((x + 2, y + cell), '/'.join(name[-2:])[-(cell // 6):], fill=(0, 0, 0, 255))
    sheet.convert('RGB').save(out)
    print(out, len(files), sheet.size)


if __name__ == '__main__':
    main()
