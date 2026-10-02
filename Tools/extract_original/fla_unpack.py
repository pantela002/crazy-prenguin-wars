#!/usr/bin/env python3
"""Unpack the (slightly broken) .fla zips in cpw-assets/fla by walking the local
file headers, so the central-directory damage that makes `unzip` refuse them
does not matter. Usage: fla_unpack.py <file.fla> <outdir>"""
import os, struct, sys, zlib


def unpack(path, out):
    data = open(path, 'rb').read()
    pos, n = 0, 0
    while True:
        pos = data.find(b'PK\x03\x04', pos)
        if pos < 0:
            break
        (_, _, flags, method, _, _, _, csize, usize, nlen, xlen) = struct.unpack_from('<IHHHHHIIIHH', data, pos)
        name = data[pos + 30:pos + 30 + nlen].decode('utf-8', 'replace')
        start = pos + 30 + nlen + xlen
        if flags & 8 or csize == 0 and usize != 0:
            # sizes are in a trailing data descriptor: inflate until the stream ends
            d = zlib.decompressobj(-15)
            body = d.decompress(data[start:])
            csize = len(data[start:]) - len(d.unused_data)
        else:
            raw = data[start:start + csize]
            body = zlib.decompress(raw, -15) if method == 8 else raw
        pos = start + max(csize, 0)
        if name.endswith('/') or '..' in name:
            continue
        dst = os.path.join(out, name)
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        with open(dst, 'wb') as f:
            f.write(body)
        n += 1
    return n


if __name__ == '__main__':
    print(unpack(sys.argv[1], sys.argv[2]))
