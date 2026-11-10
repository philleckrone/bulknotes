# -*- coding: utf-8 -*-
"""Tests of the Bulk Note Editing plugin code through BulkNotesTestHost.exe, on scratch copies:
  FIX   = PDT15's Settings.xml + tests/fixtures/Notes_*.xml (see make_fixtures.py)
  PDT15 = a copy of TestProject/PDT15's Settings.xml and notes
Run: python tests/run_bulk_tests.py   (build tests/TestHost in Release first)"""
import io
import os
import shutil
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
EXE = os.path.join(HERE, 'TestHost', 'bin', 'Release', 'BulkNotesTestHost.exe')
PDT15 = os.path.join(REPO, 'TestProject', 'PDT15')

failures = []
count = [0]


def check(name, ok, detail=''):
    count[0] += 1
    print(('PASS ' if ok else 'FAIL ') + name + ('' if ok else '\n     ' + detail.replace('\n', '\n     ')))
    if not ok:
        failures.append(name)


def run(args, env=None):
    e = dict(os.environ)
    for k in list(e):
        if k.startswith('BN_'):
            del e[k]
    e.update(env or {})
    p = subprocess.run([EXE] + args, stdout=subprocess.PIPE, stderr=subprocess.PIPE, env=e)
    out = p.stdout.decode('utf-8')
    if p.returncode != 0:
        raise RuntimeError('%s failed (%d): %s %s' % (args, p.returncode, out, p.stderr.decode('utf-8', 'replace')))
    return out


def keys(out):
    return [l.split('\t')[0] for l in out.splitlines() if l.strip()]


def rows(out):
    return dict((l.split('\t')[0], l.split('\t')) for l in out.splitlines() if l.strip())


def fresh_fixture(root):
    p = os.path.join(root, 'FIX')
    if os.path.isdir(p):
        shutil.rmtree(p)
    os.makedirs(p)
    shutil.copy(os.path.join(PDT15, 'Settings.xml'), p)
    fx = os.path.join(HERE, 'fixtures')
    for f in os.listdir(fx):
        if (f.startswith('Notes_') and f.endswith('.xml')) or f in ('ProjectUserAccess.xml', 'commentTags.xml'):
            shutil.copy(os.path.join(fx, f), p)
    # The read status lives in the projects folder, as in Paratext (1.4).
    shutil.copy(os.path.join(fx, 'ThreadStatus_Test User.ths'), root)
    return p


def fresh_second(root):
    """TST2: the second fixture project (other tag ids and users), with its own Guid."""
    p = os.path.join(root, 'TST2')
    if os.path.isdir(p):
        shutil.rmtree(p)
    shutil.copytree(os.path.join(HERE, 'fixtures', 'TST2'), p)
    with io.open(os.path.join(PDT15, 'Settings.xml'), encoding='utf-8-sig') as f:
        settings = f.read()
    start = settings.index('<Guid>') + len('<Guid>')
    end = settings.index('</Guid>')
    with io.open(os.path.join(p, 'Settings.xml'), 'w', encoding='utf-8') as f:
        f.write(settings[:start] + 'tst2000000000000000000000000000000000002' + settings[end:])
    return p


def summary(out):
    line = [l for l in out.splitlines() if l.startswith('DONE ')][-1].split()
    return dict(zip(line[0::2], line[1::2]))


def filters(root):
    p = fresh_fixture(root)
    check('chapter, unresolved', keys(run(['list', p, 'MRK', '2'])) == ['bn01', 'bn02', 'bn04', 'bn05', 'bn06'])
    check('chapter, resolved only', keys(run(['list', p, 'MRK', '2', '--status', 'resolved'])) == ['bn03', 'bn10'])
    check('chapter, all', len(keys(run(['list', p, 'MRK', '2', '--status', 'all']))) == 7)
    out = run(['list', p, 'MRK', '--status', 'all'])
    check('whole book, all chapters in order', keys(out) == ['bn08', 'bn01', 'bn02', 'bn03', 'bn04', 'bn05', 'bn06', 'bn10', 'bn07'], out)
    check('deleted note never listed', 'bn09' not in keys(out))
    out = run(['list', p, 'ALL', '--status', 'all'])
    check('whole project: every book, in book order', keys(out) ==
          ['bn11', 'bn08', 'bn01', 'bn02', 'bn03', 'bn04', 'bn05', 'bn06', 'bn10', 'bn07', 'bn12', 'bn13',
           'FIX_palabra', 'BT_logos', 'bn16'], out)
    check('whole project, assigned to Team', keys(run(['list', p, 'ALL', '--assigned', 'Team'])) == ['bn04', 'bn12'])
    # Tags, as ParatextData's CommentThread.TagIds works them out (tags_oracle.ps1 checks real projects)
    tags = dict((r[0], r[9]) for r in rows(run(['list', p, 'ALL', '--status', 'all'])).values())
    check('tags: one added', tags['bn02'] == '2', tags['bn02'])
    check('tags: two added by one comment, in order', tags['bn04'] == '3,2', tags['bn04'])
    check('tags: removed by a later comment', tags['bn06'] == '3', tags['bn06'])
    check('tags: To Do removed and nothing added gives To Do again', tags['bn07'] == '1', tags['bn07'])
    check('tags: no tag gives To Do', tags['bn01'] == '1' and tags['bn11'] == '1')
    check('conflict note: only the Conflict tag, its TagAdded ignored', tags['bn13'] == 'C-1', tags['bn13'])
    check('tag filter: Preguntas (2)', keys(run(['list', p, 'ALL', '--status', 'all', '--tag', '2'])) == ['bn02', 'bn04'])
    check('tag filter: To Do (1)', keys(run(['list', p, 'MRK', '--tag', '1'])) == ['bn08', 'bn01', 'bn05', 'bn07'])
    check('tag filter: conflict notes (-1)', keys(run(['list', p, 'ALL', '--tag', '-1'])) == ['bn13'])
    check('tag filter: conflict notes, resolved too', keys(run(['list', p, 'ALL', '--status', 'all', '--tag', '-1'])) == ['bn13', 'bn16'])
    check('tag filter: Wordlist notes (Spelling, -2)', keys(run(['list', p, 'ALL', '--tag', '-2'])) == ['FIX_palabra'])
    # Row colours (the user's choices, 1.3): the test host prints the row kind in column 11
    kinds = dict((r[0], r[10]) for r in rows(run(['list', p, 'ALL', '--status', 'all'])).values())
    check('colour: unresolved conflict = light red', kinds['bn13'] == 'Conflict', kinds['bn13'])
    check('colour: resolved conflict = light green, not red', kinds['bn16'] == 'Resolved', kinds['bn16'])
    check('colour: Wordlist note = yellow', kinds['FIX_palabra'] == 'WordlistOrTerm', kinds['FIX_palabra'])
    check('colour: resolved Biblical Terms note = yellow', kinds['BT_logos'] == 'WordlistOrTerm', kinds['BT_logos'])
    check('colour: other resolved notes = light green', kinds['bn03'] == 'Resolved' and kinds['bn10'] == 'Resolved')
    check('colour: other unresolved notes = none', kinds['bn01'] == 'Normal' and kinds['bn04'] == 'Normal')
    luis = os.path.join(p, 'Notes_Luis Revisor.xml')
    with io.open(luis, encoding='utf-8-sig') as f:
        xml = f.read()
    xml = xml.replace(u'</CommentList>', u'  <Comment Thread="BT_logos" User="Luis Revisor" VerseRef="ACT 2:12" Language="es" '
                      u'Date="2026-06-25T09:00:00.0000000-05:00"><Status></Status><Type></Type><Contents />'
                      u'<TagRemoved>-3</TagRemoved></Comment>\n</CommentList>')
    with io.open(luis, 'w', encoding='utf-8-sig') as f:
        f.write(xml)
    row = rows(run(['list', p, 'ACT', '2:12', '--status', 'all']))['BT_logos']
    check('Biblical Terms note with its tag removed: still yellow through <BiblicalTermId>',
          row[9] == '1' and row[10] == 'WordlistOrTerm', str(row))
    check('tag filter with other filters', keys(run(['list', p, 'MRK', '--tag', '3', '--created', 'Ana Consultora'])) == ['bn04', 'bn06'])
    unknown = rows(run(['list', p, 'MRK', '2'], {'BN_NOFOLDER': '1'}))
    check('no project folder: tags unknown', set(r[9] for r in unknown.values()) == set(['?']))
    check('no project folder: a tag filter lists nothing', keys(run(['list', p, 'MRK', '--tag', '1'], {'BN_NOFOLDER': '1'})) == [])
    out = run(['tags', p])
    check('tag list: commentTags.xml, then the built-in tags',
          out.splitlines() == ['1\tTo Do', '2\tPreguntas', '3\tClaridad', '-1\tConflict', '-2\tSpelling', '-3\tBiblical term'], out)
    out = run(['tags', p], {'BN_LANG': 'es'})
    check('tag list in Spanish: built-in names translated', '-1\tConflicto' in out and '-3\tTérmino bíblico' in out, out)
    os.remove(os.path.join(p, 'commentTags.xml'))
    out = run(['tags', p])
    check('no commentTags.xml: To Do plus the built-in tags', out.splitlines()[0] == '1\tTo Do' and len(out.splitlines()) == 4, out)
    check('verse 5 (two notes, one resolved)', keys(run(['list', p, 'MRK', '2:5', '--status', 'all'])) == ['bn03', 'bn04'])
    check('verse 11 is inside the range 10-12', keys(run(['list', p, 'MRK', '2:11'])) == ['bn05'])
    check('verse 13 lists nothing', keys(run(['list', p, 'MRK', '2:13'])) == [])
    check('created by Luis', keys(run(['list', p, 'MRK', '--status', 'all', '--created', 'Luis Revisor'])) == ['bn03', 'bn05', 'bn07'])
    check('created by: any case', keys(run(['list', p, 'MRK', '--created', 'luis revisor'])) == ['bn05', 'bn07'])
    check('comment by Luis (any comment)', keys(run(['list', p, 'MRK', '--status', 'all', '--commented', 'Luis Revisor'])) ==
          ['bn02', 'bn03', 'bn05', 'bn10', 'bn07'])
    check('assigned to Luis', keys(run(['list', p, 'MRK', '--assigned', 'Luis Revisor'])) == ['bn02'])
    check('assigned to Team', keys(run(['list', p, 'MRK', '--assigned', 'Team'])) == ['bn04'])
    check('assigned to nobody (bn06 was unassigned again)', keys(run(['list', p, 'MRK', '--assigned', '-'])) == ['bn08', 'bn01', 'bn05', 'bn06', 'bn07'])
    check('created from 2026-09-20', keys(run(['list', p, 'MRK', '--status', 'all', '--from', '2026-09-20'])) ==
          ['bn08', 'bn03', 'bn04', 'bn05'])
    check('created up to 2026-09-01 (day included)', keys(run(['list', p, 'MRK', '--status', 'all', '--to', '2026-09-01'])) == ['bn01', 'bn06', 'bn10'])
    check('last comment from 2026-10-01 to 2026-10-15', keys(run(['list', p, 'MRK', '--status', 'all', '--last',
                                                                  '--from', '2026-10-01', '--to', '2026-10-15'])) == ['bn02', 'bn03', 'bn04'])
    check('words in a later comment', keys(run(['list', p, 'MRK', '--text', 'cuatro hombres'])) == ['bn02'])
    check('words, any case and accents as typed', keys(run(['list', p, 'MRK', '--text', 'LEVÍ'])) == ['bn06'])
    check('words attached to the note', keys(run(['list', p, 'MRK', '--text', 'hijo'])) == ['bn04', 'bn06'])
    check('filters combined', keys(run(['list', p, 'MRK', '--status', 'all', '--created', 'Ana Consultora', '--commented', 'Luis Revisor',
                                        '--from', '2026-08-01'])) == ['bn02'])
    out = rows(run(['list', p, 'MRK', '2', '--status', 'all']))
    check('columns: created, last by, last date, count', out['bn02'][4:9] == ['Ana Consultora', '2026-09-05', 'Luis Revisor', '2026-10-10', '2'], str(out['bn02']))
    users = run(['users', p], {'BN_USERS': 'Extra Person'}).splitlines()
    check('assign-to users = active users of ProjectUserAccess.xml, in file order',
          users == ['Ana Consultora', 'Luis Revisor', 'Rosa Admin', 'Olga Observadora'], str(users))
    check('role None left out', 'Pedro Antiguo' not in users and 'Marta Retirada' not in users, str(users))
    os.remove(os.path.join(p, 'ProjectUserAccess.xml'))
    users = run(['users', p], {'BN_USERS': 'Extra Person'}).splitlines()
    check('without ProjectUserAccess.xml: the fallback list', users == ['Ana Consultora', 'Extra Person', 'Luis Revisor'], str(users))
    with io.open(os.path.join(p, 'ProjectUserAccess.xml'), 'w', encoding='utf-8') as f:
        f.write(u'<ProjectUserAccess><User UserName="Broken"')
    users = run(['users', p], {'BN_USERS': 'Extra Person'}).splitlines()
    check('unreadable ProjectUserAccess.xml: the fallback list', users == ['Ana Consultora', 'Extra Person', 'Luis Revisor'], str(users))


def resolving(root):
    p = fresh_fixture(root)
    out = run(['resolve', p, 'MRK', '2', '--keys', 'bn01,bn05'])
    s = summary(out)
    check('resolve two notes', s['DONE'] == '2' and s['NOTCONFIRMED'] == '0' and s['FAILED'] == '0', out)
    after = rows(run(['list', p, 'MRK', '2', '--status', 'all']))
    check('both now resolved', after['bn01'][2] == 'resolved' and after['bn05'][2] == 'resolved')
    check('others untouched', after['bn02'][2] == 'open' and after['bn04'][2] == 'open' and after['bn02'][8] == '2')
    check('resolve adds one comment when no text', after['bn01'][8] == '2', str(after['bn01']))
    check('report names the notes', 'MRK 2:1' in out and 'MRK 2:10-12' in out, out)

    out = run(['resolve', p, 'MRK', '2', '--status', 'all', '--keys', 'bn01,bn02', '--comment', 'Revisado en la reunión.'])
    s = summary(out)
    check('already resolved note is skipped', s['DONE'] == '1' and s['SKIPPED'] == '1', out)
    after = rows(run(['list', p, 'MRK', '2', '--status', 'all']))
    check('comment then resolve: two comments added', after['bn02'][8] == '4' and after['bn02'][2] == 'resolved', str(after['bn02']))
    check('comment text kept', 'bn02' in keys(run(['list', p, 'MRK', '2', '--status', 'all', '--text', 'reunión'])))

    p = fresh_fixture(root)
    out = run(['resolve', p, 'MRK', '2'], {'BN_NOLOCK': 'FIX'})
    check('no lock: nothing changed, said so', summary(out)['NOLOCK'] == 'True' and 'Nothing was changed' in out, out)
    check('no lock: notes still open', len(keys(run(['list', p, 'MRK', '2']))) == 5)

    out = run(['resolve', p, 'MRK', '2'], {'BN_FAIL': 'MRK 2:3'})
    s = summary(out)
    check('one failure, the rest resolved', s['DONE'] == '4' and s['FAILED'] == '1' and 'MRK 2:3: test failure' in out, out)
    check('failed note still open', keys(run(['list', p, 'MRK', '2'])) == ['bn02'])

    p = fresh_fixture(root)
    out = run(['resolve', p, 'MRK', '2', '--keys', 'bn01,bn04'], {'BN_NORESOLVE': 'MRK 2:5'})
    s = summary(out)
    check('silent refusal is caught by the read-back', s['DONE'] == '2' and s['NOTCONFIRMED'] == '1' and 'read back unchanged' in out, out)
    check('the refused note is not reported as resolved', out.split('read back unchanged')[0].count('MRK 2:5') == 0, out)

    p = fresh_fixture(root)
    out = run(['resolve', p, 'ALL', '--keys', 'bn11,bn12,bn01'])
    check('whole project: notes in three books resolved and confirmed',
          summary(out)['DONE'] == '3' and summary(out)['NOTCONFIRMED'] == '0', out)
    check('whole project: they read back resolved', keys(run(['list', p, 'ALL', '--status', 'resolved'])) ==
          ['bn11', 'bn01', 'bn03', 'bn10', 'bn12', 'BT_logos', 'bn16'])
    p = fresh_fixture(root)
    out = run(['resolve', p, 'MRK', '--keys', 'bn07,bn08'])
    check('whole book: notes in other chapters resolved', summary(out)['DONE'] == '2' and summary(out)['NOTCONFIRMED'] == '0', out)


def assigning(root):
    p = fresh_fixture(root)
    out = run(['assign', p, 'Luis Revisor', 'MRK', '2', '--keys', 'bn01,bn02,bn06'])
    s = summary(out)
    check('assign: one already assigned is skipped', s['DONE'] == '2' and s['SKIPPED'] == '1' and s['NOTCONFIRMED'] == '0', out)
    after = rows(run(['list', p, 'MRK', '2']))
    check('assigned now', after['bn01'][3] == 'Luis Revisor' and after['bn06'][3] == 'Luis Revisor')
    check('default comment says who', 'bn01' in keys(run(['list', p, 'MRK', '2', '--text', 'Assigned to Luis Revisor.'])))
    out = run(['assign', p, 'Team', 'MRK', '2', '--keys', 'bn01', '--comment', 'Para todos.'], {'BN_LANG': 'es'})
    check('assign to Team with a comment', rows(run(['list', p, 'MRK', '2']))['bn01'][3] == 'Team' and 'Equipo' in out, out)
    check('the user comment is used, not the default', 'bn01' in keys(run(['list', p, 'MRK', '2', '--text', 'Para todos.'])))
    out = run(['assign', p, '-', 'MRK', '2', '--keys', 'bn01,bn05'])
    s = summary(out)
    check('unassign: one had nobody already', s['DONE'] == '1' and s['SKIPPED'] == '1' and s['NOTCONFIRMED'] == '0', out)
    check('unassigned now', rows(run(['list', p, 'MRK', '2']))['bn01'][3] == '-')
    check('unassign comment', 'bn01' in keys(run(['list', p, 'MRK', '2', '--text', 'No longer assigned'])))
    out = run(['assign', p, 'Ana Consultora', 'MRK', '2:5', '--status', 'all'])
    check('assign in verse scope also works on a resolved note', summary(out)['DONE'] == '2', out)
    check('a resolved note stays resolved when assigned', rows(run(['list', p, 'MRK', '2', '--status', 'all']))['bn03'][2] == 'resolved')
    out = run(['assign', p, 'Luis Revisor', 'MRK', '3'], {'BN_FAIL': 'MRK 3:1'})
    check('assign failure reported', summary(out)['FAILED'] == '1' and 'MRK 3:1' in out, out)
    for lang, word in (('es', 'asignada'), ('fr', 'attribuée')):
        p = fresh_fixture(root)
        out = run(['assign', p, 'Luis Revisor', 'MRK', '2', '--keys', 'bn01'], {'BN_LANG': lang})
        check('results in ' + lang, word in out, out)


def reading(root):
    """Read / unread (1.4), from ThreadStatus_Test User.ths in the folder above the project."""
    p = fresh_fixture(root)
    out = run(['list', p, 'MRK', '2'])
    read = dict((r[0], r[13]) for r in rows(out).values())
    check('read: by comment keys (bn01), by an old count line (bn04)', read['bn01'] == 'read' and read['bn04'] == 'read', str(read))
    check('unread: only the first of two comments read (bn02), no line (bn05)', read['bn02'] == 'unread' and read['bn05'] == 'unread', str(read))
    check('filter: unread', keys(run(['list', p, 'MRK', '2', '--read', 'unread'])) == ['bn02', 'bn05', 'bn06'])
    check('filter: read', keys(run(['list', p, 'MRK', '2', '--read', 'read'])) == ['bn01', 'bn04'])
    out = run(['list', p, 'ALL', '--status', 'resolved', '--read', 'unread'])
    check('resolved but unread (the translator\'s request): bn10, and bn16 (old count 1 of 2)', keys(out) == ['bn10', 'bn16'], out)
    check('resolved and read: both keys (bn03), old count 2 of 2 (BT_logos)',
          keys(run(['list', p, 'ALL', '--status', 'resolved', '--read', 'read'])) == ['bn03', 'BT_logos'])
    check('read filter with other filters', keys(run(['list', p, 'MRK', '--read', 'unread', '--created', 'Luis Revisor'])) == ['bn05', 'bn07'])
    out = run(['list', p, 'MRK', '2'], {'BN_THREADSTATUS': os.path.join(root, 'none.ths')})
    check('no read-status file: every note unread', set(r[13] for r in rows(out).values()) == set(['unread']), out)
    # 1.6: a note the user resolved or assigned has been read (the user's rule), new comment included.
    out = run(['resolve', p, 'MRK', '2', '--keys', 'bn02'])
    check('resolve marks the note read, the new comment included',
          rows(run(['list', p, 'MRK', '2', '--status', 'all']))['bn02'][13] == 'read' and summary(out)['READNOTMARKED'] == '0', out)
    run(['assign', p, 'Luis Revisor', 'MRK', '2', '--keys', 'bn05'])
    check('assign marks the note read', rows(run(['list', p, 'MRK', '2']))['bn05'][13] == 'read')
    run(['resolve', p, 'MRK', '2', '--status', 'all', '--keys', 'bn10'])
    check('a note needing no change keeps its read status (bn10 was already resolved, still unread)',
          rows(run(['list', p, 'MRK', '2', '--status', 'all']))['bn10'][13] == 'unread')
    out = run(['resolve', p, 'MRK', '2', '--keys', 'bn06'], {'BN_NOMARKREAD': '1'})
    s = summary(out)
    check('a Paratext that does not allow it: resolved anyway, reported as not marked read',
          s['DONE'] == '1' and s['READNOTMARKED'] == '1' and 'could not be marked as read' in out and
          rows(run(['list', p, 'MRK', '2', '--status', 'all']))['bn06'][13] == 'unread', out)
    p = fresh_fixture(root)
    out = run(['resolve', p, 'MRK', '2', '--keys', 'bn02,bn05'], {'BN_FAIL': 'MRK 2:3'})
    after = rows(run(['list', p, 'MRK', '2', '--status', 'all']))
    check('a failed resolve is not marked read (bn02 stays unread, bn05 resolved and read)',
          after['bn02'][13] == 'unread' and after['bn05'][13] == 'read' and summary(out)['FAILED'] == '1', out)


def marking(root):
    """Mark as read (1.5): ParatextData's ThreadStatus.MarkThreadRead, imitated by the test host."""
    p = fresh_fixture(root)
    ths = os.path.join(root, 'ThreadStatus_Test User.ths')
    out = run(['markread', p, 'MRK', '2'])
    s = summary(out)
    check('mark read: 3 unread marked, 2 already read skipped', s['DONE'] == '3' and s['SKIPPED'] == '2' and s['NOTCONFIRMED'] == '0', out)
    check('mark read: nothing unread left in MRK 2', keys(run(['list', p, 'MRK', '2', '--read', 'unread'])) == [])
    check('mark read: no comment added', rows(run(['list', p, 'MRK', '2']))['bn02'][8] == '2')
    check('mark read: results say so', '3 note(s) in FIX marked as read' in out, out)
    out = run(['markread', p, 'ALL', '--status', 'resolved', '--read', 'unread'])
    check('resolved but unread, then marked read: bn10 and bn16 (it had an old count line)', summary(out)['DONE'] == '2', out)
    check('...none left', keys(run(['list', p, 'ALL', '--status', 'resolved', '--read', 'unread'])) == [])

    p = fresh_fixture(root)
    out = run(['markread', p, 'MRK', '2'], {'BN_NOLOCK': 'FIX'})
    check('an observer can mark notes read (only their own read status changes)', summary(out)['DONE'] == '3', out)
    p = fresh_fixture(root)
    with io.open(ths, encoding='utf-8') as f:
        before = f.read()
    out = run(['markread', p, 'MRK', '2'], {'BN_NOMARKREAD': '1'})
    with io.open(ths, encoding='utf-8') as f:
        after = f.read()
    check('a Paratext that does not allow it: nothing changed, said so',
          summary(out)['NOMARKREAD'] == 'True' and summary(out)['DONE'] == '0' and 'does not let the tool' in out and before == after, out)
    out = run(['markread', p, 'MRK', '2'], {'BN_FAIL': 'MRK 2:3'})
    s = summary(out)
    check('one failure: the others still marked', s['DONE'] == '2' and s['FAILED'] == '1' and 'MRK 2:3: test failure' in out, out)

    p = fresh_fixture(root)
    q = fresh_second(root)
    out = run(['markread', p + ';' + q, 'MRK', '2'])
    check('two projects: unread notes of both marked (t201 already read)', summary(out)['DONE'] == '4' and summary(out)['SKIPPED'] == '3', out)
    out = run(['markread', p, 'MRK', '3', '--status', 'all'], {'BN_LANG': 'es'})
    check('results in Spanish', 'marcada(s) como le' in out, out)

    # Mark as unread (1.6): ParatextData's ThreadStatus.MarkThreadUnread removes the note's lines.
    p = fresh_fixture(root)
    out = run(['markunread', p, 'MRK', '2'])
    s = summary(out)
    check('mark unread: 2 read notes marked (bn01 by keys, bn04 by an old count line), 3 skipped',
          s['DONE'] == '2' and s['SKIPPED'] == '3' and s['NOTCONFIRMED'] == '0' and 'marked as unread' in out, out)
    check('mark unread: nothing read left in MRK 2', keys(run(['list', p, 'MRK', '2', '--read', 'read'])) == [])
    check('mark unread: other notes\' lines kept (bn03 still read)', rows(run(['list', p, 'MRK', '2', '--status', 'all']))['bn03'][13] == 'read')
    out = run(['markread', p, 'MRK', '2'])
    check('and read again', summary(out)['DONE'] == '5' and keys(run(['list', p, 'MRK', '2', '--read', 'unread'])) == [], out)
    out = run(['markunread', p, 'MRK', '2'], {'BN_NOMARKREAD': '1'})
    check('mark unread in a Paratext that does not allow it: nothing changed', summary(out)['NOMARKREAD'] == 'True'
          and keys(run(['list', p, 'MRK', '2', '--read', 'unread'])) == [], out)
    out = run(['markunread', p, 'MRK', '2'], {'BN_LANG': 'fr'})
    check('mark unread results in French', 'comme non lue' in out, out)


def several(root):
    """Several projects in one list (1.4): FIX and TST2."""
    p = fresh_fixture(root)
    q = fresh_second(root)
    both = p + ';' + q
    out = run(['list', both, 'MRK', '2'])
    check('two projects: notes of both, by reference, project order within a verse',
          keys(out) == ['bn01', 'bn02', 't201', 'bn04', 't202', 'bn05', 'bn06'], out)
    proj = dict((r[0], r[12]) for r in rows(out).values())
    check('two projects: each note names its project', proj['bn01'] == 'FIX' and proj['t201'] == 'TST2', str(proj))
    check('two projects: read status per note', rows(out)['t201'][13] == 'read' and rows(out)['t202'][13] == 'unread')
    check('tag by name across projects whose ids differ: Preguntas (FIX 2, TST2 5)',
          keys(run(['list', both, 'MRK', '--tagname', 'Preguntas'])) == ['bn02', 't201', 'bn04'])
    check('tag by name across projects: Claridad (FIX 3, TST2 2)',
          keys(run(['list', both, 'MRK', '--tagname', 'Claridad'])) == ['bn04', 't202', 'bn06'])
    tags = dict((r[0], r[9]) for r in rows(run(['list', both, 'MRK', '2'])).values())
    check('tag ids stay each project\'s own', tags['t201'] == '5' and tags['bn02'] == '2', str(tags))
    check('whole project scope, both', keys(run(['list', both, 'ALL'])) ==
          ['bn11', 'bn08', 'bn01', 'bn02', 't201', 'bn04', 't202', 'bn05', 'bn06', 'bn07', 'bn12', 't204', 'bn13', 'FIX_palabra'])
    out = run(['resolve', both, 'MRK', '2', '--keys', 'bn01,t201'])
    s = summary(out)
    check('resolve in two projects: both done and confirmed', s['DONE'] == '2' and s['NOTCONFIRMED'] == '0', out)
    check('resolve in two projects: one block per project', 'resolved in FIX' in out and 'resolved in TST2' in out, out)
    after = rows(run(['list', both, 'MRK', '2', '--status', 'all']))
    check('resolve in two projects: both read back resolved', after['bn01'][2] == 'resolved' and after['t201'][2] == 'resolved')

    p = fresh_fixture(root)
    q = fresh_second(root)
    both = p + ';' + q
    out = run(['assign', both, 'Ana Consultora', 'MRK', '2', '--keys', 'bn02,t202'])
    s = summary(out)
    check('assign a person active in only one project: the other project\'s note is skipped',
          s['DONE'] == '1' and s['NOTACTIVE'] == '1' and 'not an active user of TST2' in out, out)
    after = rows(run(['list', both, 'MRK', '2']))
    check('...and left unassigned there', after['bn02'][3] == 'Ana Consultora' and after['t202'][3] == '-', str(after['t202']))
    out = run(['assign', both, 'Luis Revisor', 'MRK', '2', '--keys', 'bn01,t202'])
    check('assign a person active in both projects', summary(out)['DONE'] == '2' and summary(out)['NOTACTIVE'] == '0', out)
    out = run(['assign', both, 'Team', 'MRK', '2', '--keys', 'bn05,t201'])
    check('Team can be assigned in every project', summary(out)['DONE'] == '2', out)
    out = run(['resolve', both, 'MRK', '2', '--keys', 'bn05,t201'], {'BN_NOLOCK': 'TST2'})
    s = summary(out)
    check('no lock in one project: the other is still changed', s['DONE'] == '1' and 'Nothing was changed' in out, out)
    check('...TST2 note still open', rows(run(['list', both, 'MRK', '2']))['t201'][2] == 'open')
    out = run(['assign', both, 'Ana Consultora', 'MRK', '2', '--keys', 't202'], {'BN_LANG': 'es'})
    check('not-active message in Spanish', 'no es un usuario activo de TST2' in out, out)


def real_project(root):
    p = os.path.join(root, 'PDT15')
    if os.path.isdir(p):
        shutil.rmtree(p)
    os.makedirs(p)
    for f in os.listdir(PDT15):
        if f in ('Settings.xml', 'ProjectUserAccess.xml') or (f.startswith('Notes_') and f.endswith('.xml')):
            shutil.copy(os.path.join(PDT15, f), p)
    users = run(['users', p]).splitlines()
    check('PDT15: the 5 users whose role is not None', users == ['Phil Leckrone', 'Gerson Cal', 'Usuario De Paratext 3',
                                                                'Felipe Enorkcel', 'Beth Bryson'], str(users))
    out = run(['list', p, 'MAT', '--status', 'all'])
    n = len(keys(out))
    check('PDT15: MAT notes listed', n > 0, out)
    out = run(['resolve', p, 'MAT', '1'])
    s = summary(out)
    check('PDT15: resolve MAT 1 confirmed', s['NOTCONFIRMED'] == '0' and s['FAILED'] == '0', out)
    check('PDT15: nothing unresolved left in MAT 1', keys(run(['list', p, 'MAT', '1'])) == [])


def main():
    if not os.path.isfile(EXE):
        sys.exit('Build the test host first: ' + EXE)
    root = tempfile.mkdtemp(prefix='bulknotes_')
    try:
        filters(root)
        resolving(root)
        assigning(root)
        reading(root)
        marking(root)
        several(root)
        real_project(root)
    finally:
        shutil.rmtree(root, ignore_errors=True)
    print('')
    print('%d checks, %d failed' % (count[0], len(failures)))
    sys.exit(1 if failures else 0)


if __name__ == '__main__':
    if hasattr(sys.stdout, 'reconfigure'):
        sys.stdout.reconfigure(encoding='utf-8')
    main()
