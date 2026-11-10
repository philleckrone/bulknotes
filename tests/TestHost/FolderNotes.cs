using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using BulkNotes;

namespace BulkNotesTests
{
    /// <summary>
    /// A project folder as the plugin sees it, for tests: notes are read from its Notes_*.xml
    /// files (comments grouped by Thread and ordered by date, as Paratext does) and written into
    /// Notes_&lt;BN_USER or "Test User"&gt;.xml. A note's status is the last non-empty Status of its
    /// comments ("done" = resolved, "deleted" = not shown) and its assignment the last AssignedUser
    /// element (empty = nobody). No versification mapping.
    ///   BN_NOLOCK=PROJ,PROJ   OpenWriter returns null for these projects (as for an observer)
    ///   BN_FAIL=LUK 11:42,..  Resolve and Assign throw for notes at these references
    ///   BN_NORESOLVE=REF,..   Resolve does nothing there, without an error (Paratext keeping nothing)
    ///   BN_USERS=a;b          extra project users, used only without a ProjectUserAccess.xml
    ///                         (then the users are the comment authors plus these)
    ///   BN_THREADSTATUS=FILE  the read status file; otherwise ThreadStatus_&lt;user&gt;.ths in the
    ///                         folder above the project, as Paratext keeps it in the projects folder
    /// A note is read as ParatextData's ThreadStatus works it out: lines "threadId key" list read
    /// comments by their date's ticks (+ version number, 0 here), and the note is read when all its
    /// comments are listed; an old line "threadId n" (n &lt; 1000) says n comments were read.
    /// </summary>
    internal sealed class FolderNoteProject : INoteProject
    {
        internal static readonly string[] Codes =
        {
            "GEN", "EXO", "LEV", "NUM", "DEU", "JOS", "JDG", "RUT", "1SA", "2SA", "1KI", "2KI", "1CH", "2CH",
            "EZR", "NEH", "EST", "JOB", "PSA", "PRO", "ECC", "SNG", "ISA", "JER", "LAM", "EZK", "DAN", "HOS",
            "JOL", "AMO", "OBA", "JON", "MIC", "NAM", "HAB", "ZEP", "HAG", "ZEC", "MAL", "MAT", "MRK", "LUK",
            "JHN", "ACT", "ROM", "1CO", "2CO", "GAL", "EPH", "PHP", "COL", "1TH", "2TH", "1TI", "2TI", "TIT",
            "PHM", "HEB", "JAS", "1PE", "2PE", "1JN", "2JN", "3JN", "JUD", "REV",
        };

        private static readonly int[] Chapters =
        {
            50, 40, 27, 36, 34, 24, 21, 4, 31, 24, 22, 25, 29, 36, 10, 13, 10, 42, 150, 31, 12, 8, 66, 52, 5, 48, 12, 14,
            3, 9, 1, 4, 7, 3, 3, 3, 2, 14, 4, 28, 16, 24, 21, 28, 16, 16, 13, 6, 6, 4, 4, 5, 3, 6, 4, 3,
            1, 13, 5, 5, 3, 5, 1, 1, 1, 22,
        };

        private readonly string folder;
        private readonly string id;
        private readonly string longName;
        public readonly List<string> GoTos = new List<string>();

        public FolderNoteProject(string folder)
        {
            this.folder = Path.GetFullPath(folder).TrimEnd('\\');
            var doc = new XmlDocument();
            doc.Load(Path.Combine(this.folder, "Settings.xml"));
            id = Text(doc, "Guid") ?? ShortName;
            longName = Text(doc, "FullName") ?? "";
        }

        private static string Text(XmlDocument doc, string name)
        {
            XmlNode n = doc.DocumentElement.SelectSingleNode(name);
            return n != null && n.InnerText.Trim().Length > 0 ? n.InnerText.Trim() : null;
        }

        public string Id
        {
            get { return id; }
        }

        public string ShortName
        {
            get { return Path.GetFileName(folder); }
        }

        public string LongName
        {
            get { return longName; }
        }

        /// <summary>BN_NOFOLDER=1 acts as if the folder could not be found (tags unknown).</summary>
        public string Folder
        {
            get { return Environment.GetEnvironmentVariable("BN_NOFOLDER") == "1" ? null : folder; }
        }

        public bool CanEditNotes
        {
            get { return !InList("BN_NOLOCK", ShortName); }
        }

        private static bool InList(string variable, string value)
        {
            string list = Environment.GetEnvironmentVariable(variable) ?? "";
            return list.Split(',').Any(p => string.Equals(p.Trim(), value, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>As in Paratext: the active users of ProjectUserAccess.xml when the folder has one.</summary>
        public IList<string> Users
        {
            get
            {
                IList<string> active = ProjectUsers.Active(folder);
                if (active != null) return active;
                var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string file in Directory.GetFiles(folder, "Notes_*.xml"))
                {
                    var doc = new XmlDocument();
                    doc.Load(file);
                    foreach (XmlElement e in doc.DocumentElement.SelectNodes("Comment"))
                        if (e.GetAttribute("User").Length > 0) names.Add(e.GetAttribute("User"));
                }
                foreach (string u in (Environment.GetEnvironmentVariable("BN_USERS") ?? "").Split(';'))
                    if (u.Trim().Length > 0) names.Add(u.Trim());
                return names.ToList();
            }
        }

        public IList<BookInfo> Books
        {
            get { return Codes.Select((c, i) => new BookInfo { Book = i + 1, Code = c, LastChapter = Chapters[i] }).ToList(); }
        }

        internal static int BookNumber(string code)
        {
            return Array.IndexOf(Codes, code) + 1;
        }

        private sealed class RawComment
        {
            public string Thread;
            public string VerseRef;
            public string Selected;
            public string Status;
            public NoteComment Comment;
            public int Index;
        }

        public IList<NoteThread> GetNotes(int book, int chapter, bool includeResolved)
        {
            var raw = new List<RawComment>();
            foreach (string file in Directory.GetFiles(folder, "Notes_*.xml").OrderBy(f => f, StringComparer.Ordinal))
            {
                var doc = new XmlDocument();
                doc.Load(file);
                foreach (XmlElement e in doc.DocumentElement.SelectNodes("Comment"))
                {
                    if (e.GetAttribute("Thread").Length == 0) continue;   // belongs to no note
                    raw.Add(new RawComment
                    {
                        Thread = e.GetAttribute("Thread"),
                        VerseRef = e.GetAttribute("VerseRef"),
                        Selected = Child(e, "SelectedText"),
                        Status = Child(e, "Status"),
                        Comment = ReadComment(e),
                        Index = raw.Count,
                    });
                }
            }
            var list = new List<NoteThread>();
            Dictionary<string, ThreadRead> readStatus = ThreadRead.Load(ThreadStatusPath);
            foreach (var group in raw.GroupBy(r => r.Thread))
            {
                List<RawComment> comments = group.OrderBy(r => r.Comment.Created).ThenBy(r => r.Index).ToList();
                RawComment first = comments[0];
                NoteThread t = NewThread(group.Key, first.VerseRef, first.Selected);
                if ((book > 0 && t.Book != book) || (chapter > 0 && t.Chapter != chapter)) continue;
                string status = "";
                string assigned = "";
                foreach (RawComment r in comments)
                {
                    if (r.Status.Length > 0) status = r.Status;
                    if (r.Comment.AssignedUser != null) assigned = r.Comment.AssignedUser;
                    t.Comments.Add(r.Comment);
                }
                if (status == "deleted") continue;
                t.IsResolved = status == "done" || status == "resolved";
                if (t.IsResolved && !includeResolved) continue;
                t.AssignedUser = assigned;
                ThreadRead readLines;
                t.IsRead = readStatus.TryGetValue(group.Key, out readLines) && readLines.IsRead(t.Comments);
                list.Add(t);
            }
            NotesFiles.Annotate(list, Folder);   // the plugin's own code, as in Paratext
            return list.OrderBy(t => t.Book).ThenBy(t => t.Chapter).ThenBy(t => t.Verse).ToList();
        }

        private string ThreadStatusPath
        {
            get
            {
                string user = Environment.GetEnvironmentVariable("BN_USER") ?? "Test User";
                return Environment.GetEnvironmentVariable("BN_THREADSTATUS")
                    ?? Path.Combine(Path.GetDirectoryName(folder), "ThreadStatus_" + user + ".ths");
            }
        }

        private static NoteThread NewThread(string threadId, string verseRef, string selected)
        {
            var t = new NoteThread();
            t.Key = threadId;
            t.Handle = threadId;
            string[] parts = verseRef.Split(' ');
            t.BookCode = parts[0];
            t.Book = BookNumber(parts[0]);
            string[] cv = parts.Length > 1 ? parts[1].Split(':') : new[] { "0" };
            t.Chapter = Leading(cv[0]);
            t.Verse = cv.Length > 1 ? Leading(cv[1]) : 0;
            int dash = cv.Length > 1 ? cv[1].IndexOf('-') : -1;
            t.LastVerse = dash > 0 ? Leading(cv[1].Substring(dash + 1)) : t.Verse;
            t.Reference = t.BookCode + " " + t.Chapter + ":" + t.Verse;
            t.SelectedText = selected;
            return t;
        }

        private static int Leading(string s)
        {
            int n = 0, i = 0;
            while (i < s.Length && char.IsDigit(s[i])) n = n * 10 + (s[i++] - '0');
            return n;
        }

        private static string Child(XmlElement e, string name)
        {
            XmlNode n = e.SelectSingleNode(name);
            return n != null ? n.InnerText : "";
        }

        private static NoteComment ReadComment(XmlElement e)
        {
            var c = new NoteComment();
            c.Author = e.GetAttribute("User");
            c.Created = DateTimeOffset.Parse(e.GetAttribute("Date"), CultureInfo.InvariantCulture);
            XmlNode assigned = e.SelectSingleNode("AssignedUser");
            c.AssignedUser = assigned != null ? assigned.InnerText.Trim() : null;
            XmlNode contents = e.SelectSingleNode("Contents");
            if (contents != null)
            {
                bool hasP = contents.ChildNodes.Cast<XmlNode>().Any(n => n.Name == "p");
                IEnumerable<XmlNode> paras = hasP
                    ? contents.ChildNodes.Cast<XmlNode>().Where(n => n.Name == "p")
                    : new[] { contents };
                c.Text = string.Join("\n", paras.Select(p => p.InnerText.Trim()).Where(x => x.Length > 0).ToArray());
            }
            return c;
        }

        /// <summary>No versification here: the chapter's highest note verse, at least 30.</summary>
        public int LastVerse(int book, int chapter)
        {
            int last = 30;
            foreach (NoteThread t in GetNotes(book, chapter, true)) last = Math.Max(last, t.LastVerse);
            return last;
        }

        public INoteWriter OpenWriter()
        {
            return CanEditNotes ? new Writer(this) : null;
        }

        public void GoTo(int book, int chapter, int verse)
        {
            GoTos.Add(ShortName + " " + Codes[book - 1] + " " + chapter + ":" + verse);
        }

        /// <summary>BN_NOMARKREAD=1 acts as a Paratext without ThreadStatus.MarkThreadRead/MarkThreadUnread.</summary>
        public bool CanChangeRead(NoteThread sample, bool read)
        {
            return Environment.GetEnvironmentVariable("BN_NOMARKREAD") != "1";
        }

        /// <summary>As ParatextData's ThreadStatus: read appends "threadId key" for each comment the
        /// note has now (read from the files again, so a comment just added counts, as on Paratext's
        /// live thread); unread removes the note's lines. BN_FAIL references throw.</summary>
        public void SetRead(NoteThread thread, bool read)
        {
            if (InList("BN_FAIL", thread.Reference)) throw new InvalidOperationException("test failure");
            string id = (string)thread.Handle;
            string path = ThreadStatusPath;
            if (!read)
            {
                if (!File.Exists(path)) return;
                File.WriteAllLines(path, File.ReadAllLines(path).Where(l => !l.Trim().StartsWith(id + " ", StringComparison.Ordinal)).ToArray());
                return;
            }
            NoteThread now = GetNotes(thread.Book, thread.Chapter, true).FirstOrDefault(t => (string)t.Handle == id) ?? thread;
            var lines = now.Comments.Select(c => id + " " + c.Created.Ticks.ToString(CultureInfo.InvariantCulture));
            File.AppendAllText(path, string.Join("\r\n", lines.ToArray()) + "\r\n");
        }

        private sealed class Writer : INoteWriter
        {
            private readonly string path;
            private readonly XmlDocument doc = new XmlDocument();
            private readonly string user;
            private bool dirty;
            /// <summary>Each new comment is a little later than the one before, so the order is kept.</summary>
            private DateTimeOffset clock = DateTimeOffset.Now;

            public Writer(FolderNoteProject owner)
            {
                user = Environment.GetEnvironmentVariable("BN_USER") ?? "Test User";
                path = Path.Combine(owner.folder, "Notes_" + user + ".xml");
                doc.PreserveWhitespace = true;
                if (File.Exists(path)) doc.Load(path);
                else doc.LoadXml("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<CommentList>\n</CommentList>");
            }

            private static void FailIfAsked(NoteThread thread)
            {
                if (InList("BN_FAIL", thread.Reference)) throw new InvalidOperationException("test failure");
            }

            public void Resolve(NoteThread thread, string comment)
            {
                FailIfAsked(thread);
                if (InList("BN_NORESOLVE", thread.Reference)) return;
                if (thread.IsResolved) return;   // as Paratext: nothing to do
                if (!string.IsNullOrEmpty(comment)) Append(NewComment(thread, "", null, comment));
                Append(NewComment(thread, "done", null, ""));
            }

            public void Assign(NoteThread thread, string user, string comment)
            {
                FailIfAsked(thread);
                if (string.IsNullOrEmpty(comment)) throw new ArgumentException("Empty list of paragraphs.");
                Append(NewComment(thread, "", user ?? "", comment));
            }

            private XmlElement NewComment(NoteThread thread, string status, string assigned, string text)
            {
                clock = clock.AddMilliseconds(1);
                XmlElement e = doc.CreateElement("Comment");
                e.SetAttribute("Thread", (string)thread.Handle);   // the thread id (the key may have the project in front)
                e.SetAttribute("User", user);
                e.SetAttribute("VerseRef", thread.Reference);
                e.SetAttribute("Language", "");
                e.SetAttribute("Date", clock.ToString("yyyy-MM-ddTHH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture));
                var fields = new List<string[]>
                {
                    new[] { "SelectedText", thread.SelectedText ?? "" }, new[] { "StartPosition", "0" },
                    new[] { "ContextBefore", "" }, new[] { "ContextAfter", "" }, new[] { "Status", status },
                    new[] { "Type", "" }, new[] { "ConflictType", "" }, new[] { "Verse", "" },
                };
                if (assigned != null) fields.Add(new[] { "AssignedUser", assigned });
                fields.Add(new[] { "HideInTextWindow", "false" });
                foreach (string[] kv in fields)
                {
                    XmlElement c = doc.CreateElement(kv[0]);
                    if (kv[1].Length > 0) c.InnerText = kv[1];
                    e.AppendChild(c);
                }
                XmlElement contents = doc.CreateElement("Contents");
                string[] lines = text.Replace("\r\n", "\n").Split('\n').Where(l => l.Trim().Length > 0).ToArray();
                foreach (string l in lines)
                {
                    if (lines.Length == 1) contents.AppendChild(doc.CreateTextNode(l.Trim()));
                    else contents.AppendChild(doc.CreateElement("p")).AppendChild(doc.CreateTextNode(l.Trim()));
                }
                e.AppendChild(contents);
                return e;
            }

            private void Append(XmlElement e)
            {
                doc.DocumentElement.AppendChild(doc.CreateWhitespace("  "));
                doc.DocumentElement.AppendChild(e);
                doc.DocumentElement.AppendChild(doc.CreateWhitespace("\n"));
                dirty = true;
            }

            public void SendNotifications()
            {
            }

            public void Dispose()
            {
                if (!dirty) return;
                var ws = new XmlWriterSettings();
                ws.Encoding = new UTF8Encoding(true);
                using (XmlWriter w = XmlWriter.Create(path, ws)) doc.Save(w);
            }
        }
    }

    /// <summary>One note's lines of a ThreadStatus_&lt;user&gt;.ths file (ParatextData's ThreadStatusDetails).</summary>
    internal sealed class ThreadRead
    {
        private readonly HashSet<long> keys = new HashSet<long>();
        private int count;

        public bool IsRead(IList<NoteComment> comments)
        {
            if (keys.Count > 0) return comments.All(c => keys.Contains(c.Created.Ticks));
            return count > 0 && count >= comments.Count;
        }

        public static Dictionary<string, ThreadRead> Load(string path)
        {
            var map = new Dictionary<string, ThreadRead>();
            if (!File.Exists(path)) return map;
            foreach (string line in File.ReadAllLines(path))
            {
                string[] parts = line.Trim().Split(' ');
                long value;
                if (parts.Length != 2 || !long.TryParse(parts[1], out value)) continue;
                ThreadRead t;
                if (!map.TryGetValue(parts[0], out t)) map[parts[0]] = t = new ThreadRead();
                if (value < 1000) t.count = (int)value;
                else t.keys.Add(value);
            }
            return map;
        }
    }

    internal sealed class FolderNoteHost : INoteHost
    {
        private readonly List<FolderNoteProject> projects;
        private readonly FolderNoteProject active;
        private readonly int book;
        private readonly int chapter;
        private readonly int verse;

        public FolderNoteHost(List<FolderNoteProject> projects, FolderNoteProject active, int book, int chapter, int verse)
        {
            this.projects = projects;
            this.active = active;
            this.book = book;
            this.chapter = chapter;
            this.verse = verse;
        }

        public INoteProject Active
        {
            get { return active; }
        }

        public int ActiveBook
        {
            get { return book; }
        }

        public int ActiveChapter
        {
            get { return chapter; }
        }

        public int ActiveVerse
        {
            get { return verse; }
        }

        public IList<INoteProject> Projects
        {
            get { return projects.Cast<INoteProject>().ToList(); }
        }

        public string SettingsFile
        {
            get
            {
                return Environment.GetEnvironmentVariable("BN_SETTINGS")
                    ?? Path.Combine(Path.Combine(Path.GetTempPath(), "BulkNotesTest"), "settings.xml");
            }
        }

        public event Action<int, int, int> ReferenceChanged;

        /// <summary>Acts as if the user moved to another place in Paratext.</summary>
        public void Move(int b, int c, int v)
        {
            Action<int, int, int> h = ReferenceChanged;
            if (h != null) h(b, c, v);
        }
    }
}
