# -*- coding: utf-8 -*-
# Keeps the C# sources ASCII: replaces every non-ASCII character in the
# Bulk_Notes *.cs files (plugin and tests) with a C# backslash-u escape
# (four hex digits), which is safe in string literals and harmless in comments.
# Prints the files it changed. Run with any Python 3:
#     python tools/asciify_cs.py
import io, os, re

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..')
BS = chr(92)

for dirpath, dirs, files in os.walk(ROOT):
    parts = dirpath.split(os.sep)
    if 'bin' in parts or 'obj' in parts:
        continue
    for fn in files:
        if not fn.endswith('.cs'):
            continue
        p = os.path.join(dirpath, fn)
        s = io.open(p, encoding='utf-8-sig').read()
        t = re.sub('[^\x00-\x7f]', lambda m: BS + 'u%04X' % ord(m.group(0)), s)
        if t != s:
            io.open(p, 'w', encoding='ascii', newline='').write(t)
            print(os.path.relpath(p, ROOT))
