using System;
using System.Collections.Generic;
using System.Linq;

namespace BulkNotes
{
    /// <summary>One comment of a note (a note is a string of comments).</summary>
    public sealed class NoteComment
    {
        public string Author;
        public DateTimeOffset Created;
        /// <summary>The user this comment assigned the note to: null = no change, "" = unassigned.</summary>
        public string AssignedUser;
        /// <summary>The comment's text, one line per paragraph.</summary>
        public string Text = "";

        public bool HasText
        {
            get { return Text.Length > 0; }
        }
    }

    /// <summary>One note (thread) of a project.</summary>
    public sealed class NoteThread
    {
        /// <summary>Identifies the note within its project while the window is open.</summary>
        public string Key;
        public int Book;
        public string BookCode;
        public int Chapter;
        public int Verse;
        /// <summary>The last verse the note covers (the same as Verse unless it is on a range, "GEN 6:1-22").</summary>
        public int LastVerse;
        /// <summary>e.g. "LUK 11:42".</summary>
        public string Reference;
        /// <summary>The words the note is attached to (empty when it is attached to the verse).</summary>
        public string SelectedText;
        public bool IsResolved;
        /// <summary>Who the note is assigned to now: "" or null = nobody, "Team", or a user name.</summary>
        public string AssignedUser;
        public readonly List<NoteComment> Comments = new List<NoteComment>();
        /// <summary>The provider's own object for this note.</summary>
        public object Handle;
        /// <summary>The note's tag ids, as Paratext works them out (NotesFiles.TagsOf); null when the
        /// notes files could not be read, so the tags are unknown.</summary>
        public List<int> Tags;
        /// <summary>A conflict note (the first comment's Type is "conflict"); its only tag is Conflict.</summary>
        public bool IsConflict;
        /// <summary>A comment of the note names a biblical term (&lt;BiblicalTermId&gt;): a Biblical Terms note.</summary>
        public bool IsBiblicalTermNote;
        /// <summary>The current user has read every comment of the note (Paratext's IProjectNote.IsRead,
        /// kept per user in ThreadStatus_&lt;user&gt;.ths in the projects folder).</summary>
        public bool IsRead;
        /// <summary>The project the note was read from (set by NoteReader).</summary>
        public INoteProject Project;
        /// <summary>The names of the note's tags in its own project (set by NoteReader; tag ids differ
        /// between projects); null when the tags are unknown.</summary>
        public List<string> TagNames;

        /// <summary>The tag names as one text, "" when unknown.</summary>
        public string TagText
        {
            get { return TagNames != null ? string.Join(", ", TagNames.ToArray()) : ""; }
        }

        /// <summary>A note made in the Wordlist (Paratext's Spelling tag, -2) or in Biblical Terms
        /// (the Biblical term tag, -3, or a comment naming a term).</summary>
        public bool IsWordlistOrBiblicalTerm
        {
            get
            {
                if (IsBiblicalTermNote) return true;
                return Tags != null && (Tags.Contains(NotesFiles.SpellingTag) || Tags.Contains(NotesFiles.BiblicalTermTag));
            }
        }

        /// <summary>How the row is highlighted (the user's choice, 1.3): an unresolved conflict note
        /// is light red; a Wordlist or Biblical Terms note is yellow, resolved or not; any other
        /// resolved note is light green.</summary>
        public RowKind Kind
        {
            get
            {
                if (IsConflict && !IsResolved) return RowKind.Conflict;
                if (IsWordlistOrBiblicalTerm) return RowKind.WordlistOrTerm;
                return IsResolved ? RowKind.Resolved : RowKind.Normal;
            }
        }

        /// <summary>"LUK 11:42" or "GEN 6:1-22" for a note on a range.</summary>
        public string RangeReference
        {
            get { return LastVerse > Verse ? Reference + "-" + LastVerse : Reference; }
        }

        public NoteComment First
        {
            get { return Comments.Count > 0 ? Comments[0] : null; }
        }

        public NoteComment Last
        {
            get { return Comments.Count > 0 ? Comments[Comments.Count - 1] : null; }
        }

        /// <summary>Who wrote the note (the first comment).</summary>
        public string CreatedBy
        {
            get { return First != null ? First.Author ?? "" : ""; }
        }

        public DateTimeOffset Created
        {
            get { return First != null ? First.Created : DateTimeOffset.MinValue; }
        }

        /// <summary>Who added the last comment.</summary>
        public string LastBy
        {
            get { return Last != null ? Last.Author ?? "" : ""; }
        }

        public DateTimeOffset LastDate
        {
            get { return Last != null ? Last.Created : DateTimeOffset.MinValue; }
        }

        /// <summary>Everyone who wrote a comment, in order of first comment.</summary>
        public IList<string> Authors
        {
            get { return Comments.Select(c => c.Author ?? "").Where(a => a.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList(); }
        }

        /// <summary>The note's subject: the first comment with text.</summary>
        public string FirstText
        {
            get
            {
                NoteComment c = Comments.FirstOrDefault(x => x.HasText);
                return c != null ? c.Text : "";
            }
        }

        public bool IsAssigned
        {
            get { return !string.IsNullOrEmpty(AssignedUser); }
        }

        /// <summary>True when the note covers the verse (a note on a range covers each verse of the range).</summary>
        public bool Covers(int verse)
        {
            return verse >= Verse && verse <= Math.Max(Verse, LastVerse);
        }
    }

    /// <summary>The highlight of a note's row.</summary>
    public enum RowKind
    {
        Normal,
        Resolved,
        WordlistOrTerm,
        Conflict,
    }

    /// <summary>A book of the project's versification and its number of chapters.</summary>
    public sealed class BookInfo
    {
        public int Book;
        public string Code;
        public int LastChapter;

        public override string ToString()
        {
            return Code;
        }
    }

    /// <summary>A project whose notes can be read and changed.</summary>
    public interface INoteProject
    {
        string Id { get; }
        string ShortName { get; }
        string LongName { get; }
        /// <summary>False for an observer: the notes can be listed but not changed.</summary>
        bool CanEditNotes { get; }
        /// <summary>The project's folder (for commentTags.xml, ProjectUserAccess.xml and the notes
        /// files); null when it cannot be found.</summary>
        string Folder { get; }
        /// <summary>The books of the project's versification, in book order.</summary>
        IList<BookInfo> Books { get; }
        /// <summary>The people a note can be assigned to: the users still active in the project
        /// (ProjectUserAccess.xml, role other than None).</summary>
        IList<string> Users { get; }
        /// <summary>The notes of one chapter, of the whole book when chapter is 0, or of the whole
        /// project when book is 0, with their tags and conflict flag (NotesFiles.Annotate).</summary>
        IList<NoteThread> GetNotes(int book, int chapter, bool includeResolved);
        /// <summary>The number of verses of a chapter in the project's versification.</summary>
        int LastVerse(int book, int chapter);
        /// <summary>Locks the user's notes for writing; null when the user may not change notes or the notes are busy.</summary>
        INoteWriter OpenWriter();
        /// <summary>True when notes can be marked read (or unread) here. The plugin API cannot do it,
        /// so the Paratext provider reaches Paratext's own ThreadStatus by reflection (the user's
        /// request, 1.5; unread 1.6); it is checked on one of the notes to mark.</summary>
        bool CanChangeRead(NoteThread sample, bool read);
        /// <summary>Marks a note read or unread for the current user only (no lock: the read status is
        /// per user, outside the project, and not shared by Send/Receive). Throws on failure.</summary>
        void SetRead(NoteThread thread, bool read);
        /// <summary>Moves the project's text window (or a new one) to the verse.</summary>
        void GoTo(int book, int chapter, int verse);
    }

    /// <summary>Changes notes while the notes lock is held. Dispose releases the lock.</summary>
    public interface INoteWriter : IDisposable
    {
        /// <summary>Resolves a note, first adding the comment when there is one. Throws on failure.</summary>
        void Resolve(NoteThread thread, string comment);
        /// <summary>Adds a comment that assigns the note to the user ("" = nobody). Throws on failure.</summary>
        void Assign(NoteThread thread, string user, string comment);
        /// <summary>Tells Paratext's windows that the notes changed.</summary>
        void SendNotifications();
    }

    /// <summary>Paratext (or the test host) as the window sees it.</summary>
    public interface INoteHost
    {
        /// <summary>The project of the window the tool was started from (null when none).</summary>
        INoteProject Active { get; }
        int ActiveBook { get; }
        int ActiveChapter { get; }
        int ActiveVerse { get; }
        /// <summary>Every project whose notes can be listed, the active one included.</summary>
        IList<INoteProject> Projects { get; }
        /// <summary>Where the remembered choices are kept.</summary>
        string SettingsFile { get; }
        /// <summary>Raised (book, chapter, verse) when the user moves to another reference in Paratext.</summary>
        event Action<int, int, int> ReferenceChanged;
    }
}
