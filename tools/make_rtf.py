# -*- coding: utf-8 -*-
# Writes the Bulk Note Editing user guides (English, Spanish and French) from rtf_text.py:
#     python tools/make_rtf.py
# Lines starting with '# ' are headings; '> ' lines are indented; '!img FILE' embeds a PNG
# from the tools folder, scaled to the page width.
import binascii, io, os, re, struct, sys

def esc(s):
    out = []
    for ch in s:
        o = ord(ch)
        if ch in u'\\{}':
            out.append(u'\\' + ch)
        elif o < 128:
            out.append(ch)
        elif o < 256:
            out.append(u"\\'%02x" % o)
        else:
            out.append(u'\\u%d?' % (o if o < 32768 else o - 65536))
    return u''.join(out)

HERE = os.path.dirname(os.path.abspath(__file__))
PAGE_WIDTH_TWIPS = 9360   # 6.5 inches

def picture(name):
    data = io.open(os.path.join(HERE, name), 'rb').read()
    width, height = struct.unpack('>II', data[16:24])   # PNG IHDR
    goal_w = PAGE_WIDTH_TWIPS
    goal_h = int(round(goal_w * height / float(width)))
    hexdata = binascii.hexlify(data).decode('ascii')
    lines = [hexdata[i:i + 128] for i in range(0, len(hexdata), 128)]
    return (u'{\\pict\\pngblip\\picw%d\\pich%d\\picwgoal%d\\pichgoal%d\r\n' % (width, height, goal_w, goal_h)
            + u'\r\n'.join(lines) + u'}\\par')

def rtf(title, lines):
    r = [u'{\\rtf1\\ansi\\ansicpg1252\\deff0\\nouicompat{\\fonttbl{\\f0\\fswiss\\fcharset0 Arial;}}',
         u'\\viewkind4\\uc1',
         u'\\pard\\sa200\\sl276\\slmult1\\f0\\fs28\\b ' + esc(title) + u'\\b0\\fs22\\par']
    for ln in lines:
        if ln.startswith(u'# '):
            r.append(u'\\par')
            r.append(u'\\b ' + esc(ln[2:]) + u'\\b0\\par')
        elif ln.startswith(u'> '):
            r.append(u'\\tab ' + esc(ln[2:]) + u'\\par')
        elif ln.startswith(u'!img '):
            r.append(picture(ln[5:].strip()))
        else:
            r.append(esc(ln) + u'\\par')
    r.append(u'}')
    return u'\r\n'.join(r) + u'\r\n'

from rtf_text import EN, ES, FR

ROOT = os.path.join(HERE, '..')
# The version is set once, in Strings.ToolVersion.
VERSION = re.search(r'ToolVersion = "([^"]+)"', io.open(os.path.join(ROOT, 'BulkNotesPlugin', 'Strings.cs'), encoding='ascii').read()).group(1)

def write(name, title, lines):
    io.open(os.path.join(ROOT, 'plugin', name), 'w', encoding='ascii', newline='').write(rtf(title, lines))

write('BulkNotes_UserGuide.rtf', u'BULK NOTE EDITING %s (PARATEXT PLUGIN, EXPERIMENTAL)' % VERSION, EN)
write('BulkNotes_UserGuide_sp.rtf', u'EDICIÓN DE NOTAS EN LOTE %s (COMPLEMENTO DE PARATEXT, EXPERIMENTAL)' % VERSION, ES)
write('BulkNotes_UserGuide_fr.rtf', u"MODIFICATION DE NOTES EN LOT %s (MODULE D'EXTENSION DE PARATEXT, EXPÉRIMENTAL)" % VERSION, FR)
print('ok')
