using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using BulkNotes;

namespace BulkNotesTests
{
    /// <summary>
    /// Test host for the Bulk Note Editing plugin code, run on project folders (Settings.xml + Notes_*.xml).
    /// PROJ may be several folders joined by ';' (list, resolve, assign): their notes are read together
    /// through NoteReader and changed through BulkRunner.RunAll, as in the window.
    /// WHERE is "BOOK CH" (a chapter), "BOOK CH:V" (a verse), "BOOK" (the whole book) or "ALL" (the project).
    ///   TestHost list PROJ WHERE [filters]                  the notes listed, one per line
    ///   TestHost users PROJ                                 the people a note can be assigned to
    ///   TestHost resolve PROJ WHERE [filters] [--comment T] resolves the notes listed
    ///   TestHost assign PROJ USER WHERE [filters] [--comment T]   assigns them (USER "-" = nobody)
    ///   TestHost markread PROJ WHERE [filters]              marks them read for the test user (BN_NOMARKREAD=1:
    ///                                                       as a Paratext that does not allow it)
    ///   TestHost markunread PROJ WHERE [filters]            marks them unread for the test user
    ///   TestHost form PROJ [OTHER...] [--lang L] [--ref BOOK CH [V]]
    /// filters: --status open|resolved|all (default open)  --created NAME  --commented NAME
    ///          --assigned NAME|-  --from yyyy-mm-dd  --to yyyy-mm-dd  --last (dates of last comment)
    ///          --text WORDS  --tag ID (e.g. 2, or -1 = conflict; the first project's tag of that id)
    ///          --tagname NAME  --read read|unread  --keys K1,K2 (only these notes, by thread id)
    ///   TestHost tags PROJ                                  the tag list (id, name)
    /// list prints: key, reference, status, assigned, created by, created, last by, last date,
    /// comments, tag ids ("?" = unknown; a conflict note gets a "C" before them), row kind
    /// (Normal / Resolved / WordlistOrTerm / Conflict: the row's colour), text, project, read/unread.
    /// BN_LANG picks the language of the results. BN_MOVE="BOOK CH [V]" (form) acts as if Paratext
    /// moved there 2 seconds after the window opens.
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("usage: TestHost list|users|resolve|assign|form PROJ ...");
                return 2;
            }
            // A WinExe has no console to set an encoding on: write UTF-8 to whatever stdout is.
            var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
            stdout.AutoFlush = true;
            Console.SetOut(stdout);
            List<FolderNoteProject> projects = args[1].Split(';').Where(x => x.Length > 0).Select(x => new FolderNoteProject(x)).ToList();
            FolderNoteProject project = projects[0];
            Strings s = Strings.For(Environment.GetEnvironmentVariable("BN_LANG"));
            switch (args[0])
            {
                case "users":
                    foreach (string u in project.Users) Console.WriteLine(u);
                    return 0;
                case "tags":
                {
                    IList<TagInfo> tags = CommentTags.Read(project.Folder, s);
                    if (tags == null) Console.WriteLine("(no folder)");
                    else foreach (TagInfo t in tags) Console.WriteLine(t.Id.ToString(CultureInfo.InvariantCulture) + "\t" + t.Name);
                    return 0;
                }
                case "list":
                {
                    int i = 2;
                    foreach (NoteThread t in Select(projects, args, ref i, s)) Print(t);
                    return 0;
                }
                case "resolve":
                case "markread":
                case "markunread":
                case "assign":
                {
                    int i = 2;
                    string user = null;
                    if (args[0] == "assign")
                    {
                        user = args[i++];
                        if (user == "-") user = "";
                    }
                    int bookNum, chapter, start = i;
                    Where(args, ref i, out bookNum, out chapter);
                    i = start;   // Select reads WHERE again
                    IList<NoteThread> notes = Select(projects, args, ref i, s);
                    string comment = Option(args, "--comment");
                    BulkAction action = args[0] == "resolve" ? BulkAction.Resolve
                        : args[0] == "markread" ? BulkAction.MarkRead
                        : args[0] == "markunread" ? BulkAction.MarkUnread : BulkAction.Assign;
                    Dictionary<INoteProject, IList<TagInfo>> tags = NoteReader.TagLists(projects.Cast<INoteProject>(), s);
                    List<BulkReport> reports = BulkRunner.RunAll(action, notes, user, comment, s,
                        p => NoteReader.Read(new[] { p }, bookNum, chapter, true, tags, null));
                    Console.WriteLine("ITEMS " + notes.Count);
                    Console.WriteLine(BulkRunner.DescribeAll(reports, s));
                    Console.WriteLine(string.Format("DONE {0} SKIPPED {1} FAILED {2} NOTCONFIRMED {3} NOLOCK {4} NOTACTIVE {5} NOMARKREAD {6} READNOTMARKED {7}",
                        reports.Sum(r => r.Done.Count), reports.Sum(r => r.Skipped.Count), reports.Sum(r => r.Failed.Count),
                        reports.Sum(r => r.NotConfirmed.Count), reports.Any(r => r.NoLock), reports.Sum(r => r.NotActive.Count),
                        reports.Any(r => r.NoMarkRead), reports.Sum(r => r.ReadNotMarked)));
                    return 0;
                }
                case "form":
                {
                    string lang = null;
                    int refBook = 0, refChapter = 0, refVerse = 0;
                    for (int i = 2; i < args.Length; i++)
                    {
                        if (args[i] == "--lang") lang = args[++i];
                        else if (args[i] == "--ref")
                        {
                            refBook = FolderNoteProject.BookNumber(args[++i]);
                            refChapter = int.Parse(args[++i]);
                            int v;
                            if (i + 1 < args.Length && int.TryParse(args[i + 1], out v)) { refVerse = v; i++; }
                        }
                        else projects.Add(new FolderNoteProject(args[i]));
                    }
                    var host = new FolderNoteHost(projects, project, refBook, refChapter, refVerse);
                    Application.EnableVisualStyles();
                    var form = new BulkNotesForm(host, Strings.For(lang));
                    // BN_LOCATION="x,y": where the window opens (the UI test keeps it away from the mouse
                    // cursor, whose hover effects disturb posted clicks).
                    string at = Environment.GetEnvironmentVariable("BN_LOCATION");
                    if (!string.IsNullOrEmpty(at))
                    {
                        string[] xy = at.Split(',');
                        form.StartPosition = FormStartPosition.Manual;
                        form.Location = new System.Drawing.Point(int.Parse(xy[0]), int.Parse(xy[1]));
                    }
                    string move = Environment.GetEnvironmentVariable("BN_MOVE");
                    if (!string.IsNullOrEmpty(move))
                    {
                        string[] m = move.Split(' ');
                        var timer = new Timer { Interval = 2000 };
                        timer.Tick += delegate
                        {
                            timer.Stop();
                            host.Move(FolderNoteProject.BookNumber(m[0]), int.Parse(m[1]), m.Length > 2 ? int.Parse(m[2]) : 1);
                        };
                        form.Shown += delegate { timer.Start(); };
                    }
                    // So the UI test can check that Go to asks Paratext to go to the verse.
                    form.FormClosed += delegate
                    {
                        Console.WriteLine("GOTO " + string.Join(", ", projects.SelectMany(p => p.GoTos).ToArray()));
                    };
                    Application.Run(form);
                    return 0;
                }
            }
            Console.Error.WriteLine("unknown mode " + args[0]);
            return 2;
        }

        /// <summary>Reads WHERE at args[i]: "BOOK", "BOOK CH" or "BOOK CH:V" (verse returned through the filter).</summary>
        private static int Where(string[] args, ref int i, out int book, out int chapter)
        {
            book = FolderNoteProject.BookNumber(args[i++]);
            chapter = 0;
            int verse = 0;
            if (i < args.Length && args[i].Length > 0 && char.IsDigit(args[i][0]))
            {
                string[] cv = args[i++].Split(':');
                chapter = int.Parse(cv[0]);
                if (cv.Length > 1) verse = int.Parse(cv[1]);
            }
            return verse;
        }

        private static IList<NoteThread> Select(List<FolderNoteProject> projects, string[] args, ref int i, Strings s)
        {
            int book, chapter;
            int verse = Where(args, ref i, out book, out chapter);
            var f = new NoteFilter();
            f.Verse = verse;
            string status = Option(args, "--status") ?? "open";
            f.Status = status == "all" ? StatusFilter.All : status == "resolved" ? StatusFilter.Resolved : StatusFilter.Unresolved;
            f.CreatedBy = Option(args, "--created");
            f.CommentedBy = Option(args, "--commented");
            string assigned = Option(args, "--assigned");
            f.AssignedTo = assigned == "-" ? NoteFilter.Nobody : assigned;
            string from = Option(args, "--from"), to = Option(args, "--to");
            if (from != null) f.From = DateTime.ParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (to != null) f.To = DateTime.ParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (args.Contains("--last")) f.DateField = DateField.LastActivity;
            string read = Option(args, "--read");
            if (read != null) f.Read = read == "read" ? ReadFilter.Read : ReadFilter.Unread;
            f.Text = Option(args, "--text");
            Dictionary<INoteProject, IList<TagInfo>> tags = NoteReader.TagLists(projects.Cast<INoteProject>(), s);
            string tag = Option(args, "--tag");
            if (tag != null)
            {
                // The tag of that id in the first project; with no tag list, a name no note has.
                int id = int.Parse(tag, CultureInfo.InvariantCulture);
                IList<TagInfo> first = tags[projects[0]];
                TagInfo info = first != null ? first.FirstOrDefault(x => x.Id == id) : null;
                f.TagName = info != null ? info.Name : "#" + tag;
            }
            if (Option(args, "--tagname") != null) f.TagName = Option(args, "--tagname");
            IList<NoteThread> notes = f.Apply(NoteReader.Read(projects.Cast<INoteProject>().ToList(), book, chapter, f.NeedsResolved, tags, null));
            string keys = Option(args, "--keys");
            if (keys != null)
            {
                var wanted = new HashSet<string>(keys.Split(','));
                notes = notes.Where(t => wanted.Contains(NoteReader.ProjectKey(t))).ToList();
            }
            return notes;
        }

        private static string Option(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        private static void Print(NoteThread t)
        {
            Console.WriteLine(string.Join("\t", new[] {
                NoteReader.ProjectKey(t), t.RangeReference, t.IsResolved ? "resolved" : "open", t.IsAssigned ? t.AssignedUser : "-",
                t.CreatedBy, t.Created.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                t.LastBy, t.LastDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                t.Comments.Count.ToString(CultureInfo.InvariantCulture),
                (t.IsConflict ? "C" : "") + (t.Tags == null ? "?" : string.Join(",", t.Tags.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToArray())),
                t.Kind.ToString(),
                t.FirstText.Replace("\n", " / "),
                t.Project != null ? t.Project.ShortName : "",
                t.IsRead ? "read" : "unread" }));
        }
    }
}
