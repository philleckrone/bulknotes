# -*- coding: utf-8 -*-
"""Writes the test project's files into tests/fixtures:
- Notes_Ana Consultora.xml and Notes_Luis Revisor.xml, in Paratext's Notes_*.xml layout: notes in
  MRK 1-3, plus one in GEN and two in ACT (one a conflict note), with several authors, dates,
  assignments (a user, Team, and one taken away again), a range, words attached, tags added and
  removed, resolved notes and a deleted note;
- commentTags.xml with 3 tags;
- ProjectUserAccess.xml with users of every role, two of them no longer active (role None);
- ThreadStatus_Test User.ths, the test user's read status (1.4), as Paratext keeps it in the
  projects folder: keys (comment date ticks) for some notes, old count lines for others;
- TST2/, a second project (1.4) whose tag ids differ (2 = Claridad, 5 = Preguntas) and whose
  active users are Beto Segundo and Luis Revisor (not Ana)."""
import datetime
import os
from xml.sax.saxutils import escape

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, 'fixtures')

ANA = 'Ana Consultora'
LUIS = 'Luis Revisor'

# thread, user, ref, date, status, assigned (None = no element), selected, text
COMMENTS = [
    ('bn01', ANA, 'MRK 2:1', '2026-09-01T09:00:00', 'todo', None, '', u'Revisar "Capernaúm".'),
    ('bn02', ANA, 'MRK 2:3', '2026-09-05T09:00:00', 'todo', LUIS, '', u'¿Cuatro hombres o cuatro amigos?'),
    ('bn02', LUIS, 'MRK 2:3', '2026-10-10T09:00:00', '', None, '', u'Mejor "cuatro hombres".'),
    ('bn03', LUIS, 'MRK 2:5', '2026-09-20T09:00:00', 'todo', None, '', u'Falta la coma después de "hijo".'),
    ('bn03', ANA, 'MRK 2:5', '2026-10-01T09:00:00', 'done', None, '', u''),
    ('bn04', ANA, 'MRK 2:5', '2026-10-15T09:00:00', 'todo', 'Team', u'hijo', u'Todo el equipo: ¿"hijo" o "amigo"?'),
    ('bn05', LUIS, 'MRK 2:10-12', '2026-10-20T09:00:00', 'todo', None, '', u'Revisar el párrafo entero.'),
    ('bn06', ANA, 'MRK 2:14', '2026-08-01T09:00:00', 'todo', LUIS, '', u'Leví, hijo de Alfeo: ¿ortografía?'),
    ('bn06', ANA, 'MRK 2:14', '2026-08-05T09:00:00', '', '', '', u'Lo reviso yo.'),
    ('bn07', LUIS, 'MRK 3:1', '2026-09-02T09:00:00', 'todo', None, '', u'Sinagoga con mayúscula?'),
    ('bn08', ANA, 'MRK 1:1', '2026-10-30T09:00:00', 'todo', None, '', u'Título del libro.'),
    ('bn09', ANA, 'MRK 2:1', '2026-09-03T09:00:00', 'todo', None, '', u'Nota borrada.'),
    ('bn09', ANA, 'MRK 2:1', '2026-09-04T09:00:00', 'deleted', None, '', u''),
    ('bn10', ANA, 'MRK 2:20', '2026-07-01T09:00:00', 'todo', None, '', u'Días de ayuno.'),
    ('bn10', LUIS, 'MRK 2:20', '2026-07-02T09:00:00', 'done', None, '', u'Corregido.'),
    # Other books, for the whole-project scope
    ('bn11', ANA, 'GEN 1:1', '2026-06-01T09:00:00', 'todo', None, '', u'Principio.'),
    ('bn12', LUIS, 'ACT 2:1', '2026-06-15T09:00:00', 'todo', 'Team', '', u'Pentecostés.'),
    # A conflict note (Send/Receive merge); its TagAdded is ignored, as Paratext does.
    ('bn13', LUIS, 'ACT 2:5', '2026-06-20T09:00:00', 'todo', None, '', u'Conflicto de texto en el versículo.'),
    # A Wordlist (spelling) note, a resolved Biblical Terms note and a resolved conflict note: the
    # row colours (yellow, yellow, green; an unresolved conflict is red).
    ('FIX_palabra', ANA, 'ACT 2:10', '2026-06-22T09:00:00', '', None, u'palabra', u'Revisar la ortografía.'),
    ('BT_logos', LUIS, 'ACT 2:12', '2026-06-23T09:00:00', 'todo', None, '', u'Revisar la traducción del término.'),
    ('BT_logos', LUIS, 'ACT 2:12', '2026-06-24T09:00:00', 'done', None, '', u''),
    ('bn16', LUIS, 'ACT 2:15', '2026-06-25T09:00:00', 'todo', None, '', u'Otro conflicto.'),
    ('bn16', ANA, 'ACT 2:15', '2026-06-26T09:00:00', 'done', None, '', u''),
]

# (thread, date) -> Type, tags added, tags removed[, biblical term id]. Expected tags (Paratext's
# CommentThread.TagIds): bn02 [2]; bn04 [3, 2]; bn06 [3] (2 removed later); bn07 [1] (To Do removed,
# none added: To Do again); bn13, bn16 conflict [-1]; FIX_palabra [-2]; BT_logos [-3]; others [1] (To Do).
EXTRA = {
    ('FIX_palabra', '2026-06-22T09:00:00'): ('', [-2], []),
    ('BT_logos', '2026-06-23T09:00:00'): ('', [-3], [], u'λόγος'),
    ('bn16', '2026-06-25T09:00:00'): ('conflict', [], []),
    ('bn02', '2026-09-05T09:00:00'): ('', [2], []),
    ('bn04', '2026-10-15T09:00:00'): ('', [3, 2], []),
    ('bn06', '2026-08-01T09:00:00'): ('', [2], []),
    ('bn06', '2026-08-05T09:00:00'): ('', [3], [2]),
    ('bn07', '2026-09-02T09:00:00'): ('', [], [1]),
    ('bn13', '2026-06-20T09:00:00'): ('conflict', [2], []),
}

TAGS = [(1, u'To Do'), (2, u'Preguntas'), (3, u'Claridad')]


BETO = 'Beto Segundo'

# The second project, TST2: same layout as COMMENTS.
COMMENTS2 = [
    ('t201', BETO, 'MRK 2:3', '2026-09-06T09:00:00', 'todo', None, '', u'Segundo proyecto: ¿cuatro hombres?'),
    ('t202', LUIS, 'MRK 2:5', '2026-09-07T09:00:00', 'todo', None, '', u'Segundo proyecto: la coma.'),
    ('t203', BETO, 'MRK 3:1', '2026-09-08T09:00:00', 'todo', None, '', u'Segundo proyecto: sinagoga.'),
    ('t203', BETO, 'MRK 3:1', '2026-09-09T09:00:00', 'done', None, '', u''),
    ('t204', BETO, 'ACT 2:1', '2026-09-10T09:00:00', 'todo', None, '', u'Segundo proyecto: Pentecostés.'),
]
EXTRA2 = {
    ('t201', '2026-09-06T09:00:00'): ('', [5], []),   # Preguntas in TST2
    ('t202', '2026-09-07T09:00:00'): ('', [2], []),   # Claridad in TST2
}
TAGS2 = [(1, u'To Do'), (2, u'Claridad'), (5, u'Preguntas')]
USERS2 = [(BETO, 'Consultant'), (LUIS, 'TeamMember')]

# The test user's read status (expected, Status "all"): read = bn01 (key), bn03 (both keys,
# resolved), bn04 (old count line 1 of 1), BT_logos (old count 2 of 2, resolved), t201 (key);
# unread = bn02 (only its first comment), bn16 (old count 1 of 2, resolved), and every other note.
READ_KEYS = [('bn01', '2026-09-01T09:00:00'), ('bn02', '2026-09-05T09:00:00'),
             ('bn03', '2026-09-20T09:00:00'), ('bn03', '2026-10-01T09:00:00'),
             ('t201', '2026-09-06T09:00:00')]
READ_COUNTS = [('bn04', 1), ('BT_logos', 2), ('bn16', 1)]


def ticks(date):
    """A comment's read-status key: its date's ticks (clock time as written; version number 0)."""
    d = datetime.datetime.strptime(date, '%Y-%m-%dT%H:%M:%S') - datetime.datetime(1, 1, 1)
    return (d.days * 86400 + d.seconds) * 10000000


def comment_xml(c, extras=None):
    thread, user, ref, date, status, assigned, selected, text = c
    extra = (extras or EXTRA).get((thread, date), ('', [], []))
    ctype, added, removed = extra[:3]
    term = extra[3] if len(extra) > 3 else None
    lines = [u'  <Comment Thread="%s" User="%s" VerseRef="%s" Language="es" Date="%s.0000000-05:00">' % (thread, user, ref, date)]
    lines.append(u'    <SelectedText>%s</SelectedText>' % escape(selected) if selected else u'    <SelectedText />')
    lines += [u'    <StartPosition>0</StartPosition>', u'    <ContextBefore />', u'    <ContextAfter />',
              u'    <Status>%s</Status>' % status, u'    <Type>%s</Type>' % ctype,
              u'    <ConflictType>%s</ConflictType>' % ('verseText' if ctype == 'conflict' else 'unknownConflictType'), u'    <Verse />']
    if assigned is not None:
        lines.append(u'    <AssignedUser>%s</AssignedUser>' % assigned if assigned else u'    <AssignedUser />')
    lines.append(u'    <HideInTextWindow>false</HideInTextWindow>')
    # Paragraphs, as Paratext writes longer comments; the tag elements come after them.
    lines.append(u'    <Contents>\n      <p>%s</p>\n    </Contents>' % escape(text) if text else u'    <Contents />')
    if term:
        lines.append(u'    <BiblicalTermId>%s</BiblicalTermId>' % escape(term))
    lines += [u'    <TagAdded>%d</TagAdded>' % i for i in added]
    lines += [u'    <TagRemoved>%d</TagRemoved>' % i for i in removed]
    lines.append(u'  </Comment>')
    return u'\n'.join(lines)


# user, role: in the order of the file
USERS = [(ANA, 'Consultant'), (LUIS, 'TeamMember'), ('Rosa Admin', 'Administrator'),
         ('Pedro Antiguo', 'None'), ('Olga Observadora', 'Observer'), ('Marta Retirada', 'None')]


def access_xml(users=None):
    out = [u'\ufeff<?xml version="1.0" encoding="utf-8"?>', u'<ProjectUserAccess PeerSharing="true">']
    for i, (user, role) in enumerate(users or USERS):
        out += [u'  <User UserName="%s" FirstUser="%s" UnregisteredUser="false">' % (user, 'true' if i == 0 else 'false'),
                u'    <Role>%s</Role>' % role, u'    <AllBooks>true</AllBooks>', u'    <Books />',
                u'    <Permissions />', u'    <AutomaticBooks />', u'    <AutomaticPermissions />', u'  </User>']
    out.append(u'</ProjectUserAccess>')
    return u'\r\n'.join(out) + u'\r\n'


def write_project(folder, comments, extras, users, tag_list):
    if not os.path.isdir(folder):
        os.makedirs(folder)
    for user in sorted(set(c[1] for c in comments)):
        body = u'\n'.join(comment_xml(c, extras) for c in comments if c[1] == user)
        text = u'\ufeff<?xml version="1.0" encoding="utf-8"?>\n<CommentList>\n' + body + u'\n</CommentList>\n'
        with open(os.path.join(folder, 'Notes_%s.xml' % user), 'wb') as f:
            f.write(text.encode('utf-8'))
    with open(os.path.join(folder, 'ProjectUserAccess.xml'), 'wb') as f:
        f.write(access_xml(users).encode('utf-8'))
    tags = [u'\ufeff<?xml version="1.0" encoding="utf-8"?>', u'<TagList>']
    tags += [u'  <Tag Id="%d" Name="%s" Icon="01flag1" CreatorResolve="false" />' % (i, n) for i, n in tag_list]
    tags += [u'  <LastUsedID>%d</LastUsedID>' % max(i for i, n in tag_list), u'</TagList>']
    with open(os.path.join(folder, 'commentTags.xml'), 'wb') as f:
        f.write((u'\r\n'.join(tags) + u'\r\n').encode('utf-8'))


def main():
    write_project(OUT, COMMENTS, EXTRA, USERS, TAGS)
    write_project(os.path.join(OUT, 'TST2'), COMMENTS2, EXTRA2, USERS2, TAGS2)
    lines = [u'%s %d' % (t, ticks(d)) for t, d in READ_KEYS] + [u'%s %d' % (t, n) for t, n in READ_COUNTS]
    with open(os.path.join(OUT, 'ThreadStatus_Test User.ths'), 'wb') as f:
        f.write((u'\r\n'.join(lines) + u'\r\n').encode('utf-8'))
    print('written to ' + OUT)


if __name__ == '__main__':
    main()
