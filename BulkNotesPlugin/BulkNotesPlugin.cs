using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Paratext.PluginInterfaces;

namespace BulkNotes
{
    /// <summary>
    /// Paratext plugin that adds Tools > Bulk Note Editing... It lists the notes of a verse, a
    /// chapter or a book, filtered, and resolves or assigns the notes the user ticks. Everything
    /// goes through the plugin API (GetNotes, Resolve, AddNewComment under a ProjectNotes lock), so
    /// Paratext keeps its notes in memory, merges and syncs them as usual. The API cannot delete
    /// notes, and the user chose not to go around it.
    /// </summary>
    public class BulkNotesPlugin : IParatextStandalonePlugin
    {
        public const string PluginName = "Bulk Note Editing";
        public const string PublisherName = "Phil Leckrone (SIL)";

        /// <summary>The window, if open: a second click brings it to the front.</summary>
        private static BulkNotesForm openWindow;

        public string Name
        {
            get { return PluginName; }
        }

        public Version Version
        {
            get { return new Version(Strings.ToolVersion); }
        }

        public string VersionString
        {
            get { return Strings.ToolVersion; }
        }

        public string Publisher
        {
            get { return PublisherName; }
        }

        public IEnumerable<PluginMenuEntry> PluginMenuEntries
        {
            get
            {
                var entry = new PluginMenuEntry(Strings.English().MenuText, OnMenuClicked, PluginMenuLocation.ScrTextTools);
                entry.LocalizedTextNeeded += LocalizeMenuText;
                yield return entry;
            }
        }

        /// <summary>Menu text in Paratext's interface language; null keeps English.</summary>
        internal static string LocalizeMenuText(string defaultText, string locale)
        {
            string lang = string.IsNullOrEmpty(locale) ? "" : locale.ToLowerInvariant();
            if (lang.StartsWith("es", StringComparison.Ordinal)) return Strings.Spanish().MenuText;
            if (lang.StartsWith("fr", StringComparison.Ordinal)) return Strings.French().MenuText;
            return null;
        }

        public string GetDescription(string locale)
        {
            return Strings.For(locale).Description;
        }

        public IDataFileMerger GetMerger(IPluginHost host, string dataIdentifier)
        {
            return null;   // the plugin keeps no project data
        }

        private void OnMenuClicked(IPluginHost host, IParatextChildState state)
        {
            Strings strings = Strings.For(UiLocale(host));
            try
            {
                if (openWindow != null && !openWindow.IsDisposed)
                {
                    if (openWindow.WindowState == FormWindowState.Minimized) openWindow.WindowState = FormWindowState.Normal;
                    openWindow.Activate();
                    // Opening the tool again reads the notes again, for the window the command came from.
                    IProject p = state != null ? state.Project : null;
                    IVerseRef r = state != null ? state.VerseRef : null;
                    openWindow.RefreshFromParatext(p != null ? p.ID : null, r != null ? r.BookNum : 0,
                        r != null ? r.ChapterNum : 0, r != null ? r.VerseNum : 0);
                    return;
                }
                var noteHost = new ParatextNoteHost(this, host, state);
                if (noteHost.Projects.Count == 0)
                {
                    Report(host, strings, strings.OpenProjectFirst, null);
                    return;
                }
                // Not modal, so the user can work in Paratext beside the list.
                var form = new BulkNotesForm(noteHost, strings);
                openWindow = form;
                form.FormClosed += delegate
                {
                    openWindow = null;
                    form.Dispose();
                };
                form.Show();
            }
            catch (Exception ex)
            {
                // Never let an error escape into Paratext.
                Report(host, strings, string.Format(strings.Error, ex.Message), ex);
            }
        }

        private void Report(IPluginHost host, Strings strings, string message, Exception ex)
        {
            try
            {
                if (host != null)
                    host.Log(this, message + (ex != null ? "\n" + ex : ""));
            }
            catch (Exception) { }
            MessageBox.Show(message, strings.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private static string UiLocale(IPluginHost host)
        {
            try
            {
                if (host != null && host.UserSettings != null)
                    return host.UserSettings.UiLocale;
            }
            catch (Exception) { }
            return null;
        }
    }

    /// <summary>Paratext as the window sees it.</summary>
    internal sealed class ParatextNoteHost : INoteHost
    {
        private readonly IPluginHost host;
        private readonly List<INoteProject> projects = new List<INoteProject>();
        private readonly ParatextNoteProject active;
        private readonly int activeBook;
        private readonly int activeChapter;
        private readonly int activeVerse;
        private Action<int, int, int> referenceChanged;

        public ParatextNoteHost(IPluginObject plugin, IPluginHost host, IParatextChildState state)
        {
            this.host = host;
            IProject activeProject = state != null ? state.Project : null;
            var all = new List<ParatextNoteProject>();
            foreach (IProject p in host.GetAllProjects())
            {
                if (p == null || p.IsResource) continue;
                all.Add(new ParatextNoteProject(plugin, host, p));
            }
            if (activeProject != null && !activeProject.IsResource)
            {
                active = all.FirstOrDefault(p => SameId(p.Id, activeProject.ID));
                if (active == null)
                {
                    active = new ParatextNoteProject(plugin, host, activeProject);
                    all.Add(active);
                }
            }
            if (state != null && state.VerseRef != null)
            {
                activeBook = state.VerseRef.BookNum;
                activeChapter = state.VerseRef.ChapterNum;
                activeVerse = state.VerseRef.VerseNum;
            }
            projects.AddRange(all.OrderBy(p => p.ShortName, StringComparer.OrdinalIgnoreCase));
        }

        private static bool SameId(string a, string b)
        {
            return a != null && b != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        public INoteProject Active
        {
            get { return active; }
        }

        public int ActiveBook
        {
            get { return activeBook; }
        }

        public int ActiveChapter
        {
            get { return activeChapter; }
        }

        public int ActiveVerse
        {
            get { return activeVerse; }
        }

        public IList<INoteProject> Projects
        {
            get { return projects; }
        }

        public string SettingsFile
        {
            get
            {
                return Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ParatextBulkNotes"), "settings.xml");
            }
        }

        /// <summary>Raised when the user moves in Paratext; listens to Paratext only while someone listens here.</summary>
        public event Action<int, int, int> ReferenceChanged
        {
            add
            {
                if (referenceChanged == null) host.VerseRefChanged += OnVerseRefChanged;
                referenceChanged += value;
            }
            remove
            {
                referenceChanged -= value;
                if (referenceChanged == null) host.VerseRefChanged -= OnVerseRefChanged;
            }
        }

        private void OnVerseRefChanged(IPluginHost sender, IVerseRef newReference, SyncReferenceGroup group)
        {
            Action<int, int, int> handler = referenceChanged;
            if (handler != null && newReference != null)
                handler(newReference.BookNum, newReference.ChapterNum, newReference.VerseNum);
        }
    }

    /// <summary>One project, through the plugin API.</summary>
    internal sealed class ParatextNoteProject : INoteProject
    {
        private readonly IPluginObject plugin;
        private readonly IPluginHost host;
        private readonly IProject project;
        private List<BookInfo> books;
        private string folder;
        private bool folderLooked;

        public ParatextNoteProject(IPluginObject plugin, IPluginHost host, IProject project)
        {
            this.plugin = plugin;
            this.host = host;
            this.project = project;
        }

        public string Id
        {
            get { return project.ID; }
        }

        public string ShortName
        {
            get { return project.ShortName; }
        }

        public string LongName
        {
            get { return project.LongName; }
        }

        /// <summary>The project folder, from the registry lookup (the plugin API gives none); looked up once.</summary>
        public string Folder
        {
            get
            {
                if (!folderLooked)
                {
                    folderLooked = true;
                    try
                    {
                        folder = ProjectFolder.Find(project.ShortName, project.ID);
                    }
                    catch (Exception) { }
                }
                return folder;
            }
        }

        /// <summary>Paratext gives the notes lock to anyone who is not an observer of the project.</summary>
        public bool CanEditNotes
        {
            get
            {
                try
                {
                    return project.CanEdit(plugin, DataType.PluginData);
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>The people still active in the project: users of ProjectUserAccess.xml whose role
        /// is not None (the user's rule). Read again each time, so a change of roles shows when the
        /// project is chosen again. Without that file (or its folder), Paratext's non-observer users.</summary>
        public IList<string> Users
        {
            get
            {
                IList<string> active = ProjectUsers.Active(Folder);
                if (active != null) return active;
                var list = new List<string>();
                try
                {
                    foreach (IUserInfo u in project.NonObserverUsers)
                        if (u != null && !string.IsNullOrEmpty(u.Name)) list.Add(u.Name);
                }
                catch (Exception) { }
                return list;
            }
        }

        /// <summary>GEN-REV, plus any other book the project (or its base project) has.</summary>
        public IList<BookInfo> Books
        {
            get
            {
                if (books != null) return books;
                var numbers = new SortedSet<int>(Enumerable.Range(1, 66));
                AddBooks(numbers, project);
                try
                {
                    if (project.BaseProject != null) AddBooks(numbers, project.BaseProject);
                }
                catch (Exception) { }
                IVersification vrs = project.Versification;
                books = new List<BookInfo>();
                foreach (int n in numbers)
                {
                    try
                    {
                        IVerseRef r = vrs.CreateReference(n, 1, 1);
                        books.Add(new BookInfo { Book = n, Code = r.BookCode, LastChapter = vrs.GetLastChapter(n) });
                    }
                    catch (Exception) { }
                }
                return books;
            }
        }

        private static void AddBooks(SortedSet<int> numbers, IProject p)
        {
            try
            {
                foreach (IBookInfo b in p.AvailableBooks) numbers.Add(b.Number);
            }
            catch (Exception) { }
        }

        /// <summary>Chapter 0 gives the whole book, and book 0 every note of the project: Paratext's
        /// GetNotes reads all notes when the book is 0 and the book's notes when the chapter is 0
        /// (checked in ParatextData's ProjectWrapper.GetNotes).</summary>
        public IList<NoteThread> GetNotes(int book, int chapter, bool includeResolved)
        {
            var list = new List<NoteThread>();
            var seen = new Dictionary<string, int>();
            foreach (IProjectNote note in project.GetNotes(book, chapter, !includeResolved))
            {
                IScriptureTextSelection anchor = note.Anchor;
                IVerseRef r = anchor.VerseRefStart;
                var t = new NoteThread();
                t.Handle = note;
                t.Book = r.BookNum;
                t.BookCode = r.BookCode;
                t.Chapter = r.ChapterNum;
                t.Verse = r.VerseNum;
                t.LastVerse = LastVerseOf(anchor, r);
                t.Reference = r.BookCode + " " + r.ChapterNum + ":" + r.VerseNum;
                t.SelectedText = anchor.SelectedText;
                t.IsResolved = note.IsResolved;
                // Read by the current user (ParatextData's ThreadStatus, per user).
                try
                {
                    t.IsRead = note.IsRead;
                }
                catch (Exception) { }
                IUserInfo assigned = note.AssignedUser;
                t.AssignedUser = assigned != null ? assigned.Name ?? "" : "";
                foreach (IComment c in note.Comments) t.Comments.Add(Convert(c));
                NoteComment first = t.First;
                // The plugin API gives notes no ID. The key must stay the same when the notes are
                // read again after a change (ticks and the check after a change go by key), so it is
                // made from the note itself: reference + first comment. Exact duplicates are numbered.
                string key = t.Reference + "|" + (first != null ? first.Created.UtcTicks + "|" + first.Author + "|" + first.Text : "");
                int n;
                seen.TryGetValue(key, out n);
                seen[key] = n + 1;
                t.Key = n == 0 ? key : key + "|" + n;
                list.Add(t);
            }
            // Tags and conflict notes: the plugin API gives neither, so they come from the notes files.
            NotesFiles.Annotate(list, Folder);
            // Stable: Paratext's order within a verse.
            return list.OrderBy(t => t.Book).ThenBy(t => t.Chapter).ThenBy(t => t.Verse).ToList();
        }

        /// <summary>The last verse a note covers in its chapter: the end of a range ("GEN 6:1-22")
        /// or of a selection that runs into later verses.</summary>
        private static int LastVerseOf(IScriptureTextSelection anchor, IVerseRef r)
        {
            int last = r.VerseNum;
            try
            {
                if (r.RepresentsMultipleVerses)
                    foreach (IVerseRef v in r.AllVerses) last = Math.Max(last, v.VerseNum);
                IVerseRef end = anchor.VerseRefEnd;
                if (end != null && end.BookNum == r.BookNum && end.ChapterNum == r.ChapterNum)
                    last = Math.Max(last, end.VerseNum);
            }
            catch (Exception) { }
            return last;
        }

        public int LastVerse(int book, int chapter)
        {
            try
            {
                return project.Versification.GetLastVerse(book, chapter);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static NoteComment Convert(IComment c)
        {
            var n = new NoteComment();
            n.Author = c.Author != null ? c.Author.Name : "";
            n.Created = c.Created;
            IUserInfo assigned = c.AssignedUser;
            n.AssignedUser = assigned != null ? assigned.Name ?? "" : null;
            var lines = new List<string>();
            foreach (CommentParagraph p in c.Contents)
            {
                string line = string.Concat(p.Spans.Where(f => f != null).Select(f => f.Text ?? "").ToArray()).Trim();
                if (line.Length > 0) lines.Add(line);
            }
            n.Text = string.Join("\n", lines.ToArray());
            return n;
        }

        public INoteWriter OpenWriter()
        {
            IWriteLock writeLock = project.RequestWriteLock(plugin, ReleaseRequested, WriteLockScope.ProjectNotes);
            return writeLock != null ? new Writer(writeLock) : null;
        }

        /// <summary>
        /// Marking read (the user's request, 1.5) and unread (1.6). The plugin API can tell whether a
        /// note is read (IProjectNote.IsRead) but not change it, so this calls Paratext's own
        /// ParatextData ThreadStatus.MarkThreadRead / MarkThreadUnread(CommentThread), the methods
        /// its Notes window uses, on the CommentThread held in the API wrapper's private "thread"
        /// field (checked in ParatextData 9.5). Paratext's own code keeps its read-status file and
        /// its memory in step. It is not a supported plugin call: when a Paratext version lacks
        /// them, CanChangeRead is false and nothing is changed. Only the current user's read status
        /// changes; it is kept outside the project and not shared by Send/Receive.
        /// </summary>
        private static MethodInfo markThreadRead;
        private static MethodInfo markThreadUnread;
        private static FieldInfo wrapperThread;
        private static bool readMethodsLooked;

        private static bool FindReadMethods(object note)
        {
            if (!readMethodsLooked)
            {
                readMethodsLooked = true;
                try
                {
                    FieldInfo field = note.GetType().GetField("thread", BindingFlags.NonPublic | BindingFlags.Instance);
                    Type status = field != null ? field.FieldType.Assembly.GetType("Paratext.Data.ProjectComments.ThreadStatus") : null;
                    if (status != null)
                    {
                        wrapperThread = field;
                        markThreadRead = StatusMethod(status, "MarkThreadRead", field.FieldType);
                        markThreadUnread = StatusMethod(status, "MarkThreadUnread", field.FieldType);
                    }
                }
                catch (Exception) { }
            }
            return wrapperThread != null && wrapperThread.DeclaringType.IsInstanceOfType(note);
        }

        private static MethodInfo StatusMethod(Type status, string name, Type threadType)
        {
            MethodInfo m = status.GetMethod(name, BindingFlags.Public | BindingFlags.Static, null, new[] { threadType }, null);
            return m != null && m.ReturnType == typeof(bool) ? m : null;
        }

        /// <summary>Looked up on a note of this project (the wrapper type is Paratext's own).</summary>
        public bool CanChangeRead(NoteThread sample, bool read)
        {
            if (sample == null || sample.Handle == null || !FindReadMethods(sample.Handle)) return false;
            return (read ? markThreadRead : markThreadUnread) != null;
        }

        public void SetRead(NoteThread thread, bool read)
        {
            object note = thread.Handle;
            MethodInfo method = note != null && FindReadMethods(note) ? (read ? markThreadRead : markThreadUnread) : null;
            if (method == null) throw new NotSupportedException("Paratext does not let the tool change this note's read status.");
            // The live CommentThread: it already holds any comment just added, so all are marked.
            object commentThread = wrapperThread.GetValue(note);
            if (commentThread == null) throw new InvalidOperationException("The note has no thread.");
            try
            {
                method.Invoke(null, new[] { commentThread });
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException ?? ex;
            }
        }

        private static void ReleaseRequested(IWriteLock writeLock)
        {
            // The lock is held only while one batch is written, then released.
        }

        public void GoTo(int book, int chapter, int verse)
        {
            IVerseRef reference = project.Versification.CreateReference(book, chapter, verse);
            host.OpenTextWindowFor(project, OpenWindowBehavior.UseExisting, reference);
        }

        private sealed class Writer : INoteWriter
        {
            private readonly IWriteLock writeLock;

            public Writer(IWriteLock writeLock)
            {
                this.writeLock = writeLock;
            }

            /// <summary>Paratext's Resolve adds an empty comment with the Resolved status (and does
            /// nothing when the note is already resolved), so the user's comment comes first.</summary>
            public void Resolve(NoteThread thread, string comment)
            {
                var note = (IProjectNote)thread.Handle;
                if (!string.IsNullOrEmpty(comment))
                    note.AddNewComment(writeLock, Paragraphs(comment), null, null);
                note.Resolve(writeLock);
            }

            /// <summary>AddNewComment stores the assigned user's name as the comment's AssignedUser
            /// ("Team", a user, or "" for nobody), as Paratext's own Assign does. It refuses a
            /// comment without paragraphs, so a comment is always passed.</summary>
            public void Assign(NoteThread thread, string user, string comment)
            {
                var note = (IProjectNote)thread.Handle;
                note.AddNewComment(writeLock, Paragraphs(comment), null, new UserName(user ?? ""));
            }

            private static CommentParagraph[] Paragraphs(string text)
            {
                return text.Replace("\r\n", "\n").Split('\n')
                    .Where(l => l.Trim().Length > 0)
                    .Select(l => new CommentParagraph(new FormattedString(l.Trim(), null, Style.Plain)))
                    .ToArray();
            }

            public void SendNotifications()
            {
                writeLock.SendNotifications();
            }

            public void Dispose()
            {
                writeLock.Dispose();
            }
        }

        /// <summary>A user name for AddNewComment, which reads only the name.</summary>
        private sealed class UserName : IUserInfo
        {
            private readonly string name;

            public UserName(string name)
            {
                this.name = name;
            }

            public string Name
            {
                get { return name; }
            }
        }
    }
}
