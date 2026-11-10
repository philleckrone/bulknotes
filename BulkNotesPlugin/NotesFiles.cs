using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;

namespace BulkNotes
{
    /// <summary>
    /// What the plugin API does not give about notes: each note's tags and whether it is a conflict
    /// note. They are read from the project's Notes_*.xml files and worked out as ParatextData's
    /// CommentThread does (checked in its IL):
    ///   - Type (CommentThread.Type) is the first comment's &lt;Type&gt;; "conflict" makes a conflict note.
    ///   - Tags (CommentThread.TagIds): a conflict note has only the Conflict tag (-1). Otherwise the
    ///     comments are walked from the last to the first; a tag added by a comment counts unless a
    ///     later comment added or removed it already. With no tag found, the note has To Do (1).
    /// Files are read with XmlReader (ZTG has 125 MB of notes) and kept while their size and time stay
    /// the same. A note from the API is matched to its thread by any of its comments' date and author.
    /// </summary>
    public static class NotesFiles
    {
        public const int ToDoTag = 1;
        public const int ConflictTag = -1;
        public const int SpellingTag = -2;
        public const int BiblicalTermTag = -3;

        private sealed class RawComment
        {
            public string Thread;
            public string User;
            public DateTimeOffset Date;
            public string Type;
            public List<int> Added;
            public List<int> Removed;
            /// <summary>The comment names a biblical term (&lt;BiblicalTermId&gt;).</summary>
            public bool TermId;
            public int Order;
        }

        private sealed class CachedFile
        {
            public long Size;
            public DateTime Time;
            public List<RawComment> Comments;
        }

        private sealed class ThreadInfo
        {
            public List<int> Tags;
            public bool IsConflict;
            public bool IsBiblicalTerm;
        }

        private static readonly Dictionary<string, CachedFile> Cache =
            new Dictionary<string, CachedFile>(StringComparer.OrdinalIgnoreCase);
        private static readonly object Gate = new object();

        /// <summary>The match key of a comment: its date (UTC ticks) and author.</summary>
        public static string MatchKey(DateTimeOffset date, string author)
        {
            return date.UtcTicks.ToString(CultureInfo.InvariantCulture) + "|" + (author ?? "");
        }

        /// <summary>
        /// Sets Tags and IsConflict on the notes from the project's Notes_*.xml files. Returns false
        /// (Tags left null) when the folder is unknown or has no notes files.
        /// </summary>
        public static bool Annotate(IEnumerable<NoteThread> notes, string projectFolder)
        {
            Dictionary<string, ThreadInfo> byComment = Read(projectFolder);
            if (byComment == null) return false;
            foreach (NoteThread t in notes)
            {
                ThreadInfo info = null;
                foreach (NoteComment c in t.Comments)
                    if (byComment.TryGetValue(MatchKey(c.Created, c.Author), out info)) break;
                if (info == null)
                {
                    // Not in the files yet (not saved): Paratext's default for a new note.
                    t.Tags = new List<int> { ToDoTag };
                    t.IsConflict = false;
                    continue;
                }
                t.Tags = new List<int>(info.Tags);
                t.IsConflict = info.IsConflict;
                t.IsBiblicalTermNote = info.IsBiblicalTerm;
            }
            return true;
        }

        /// <summary>Every comment's match key -> its thread's tags and type; null without notes files.</summary>
        private static Dictionary<string, ThreadInfo> Read(string projectFolder)
        {
            if (string.IsNullOrEmpty(projectFolder) || !Directory.Exists(projectFolder)) return null;
            string[] files;
            try
            {
                files = Directory.GetFiles(projectFolder, "Notes_*.xml");
            }
            catch (Exception)
            {
                return null;
            }
            if (files.Length == 0) return null;
            var all = new List<RawComment>();
            foreach (string f in files.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                List<RawComment> comments = Comments(f);
                if (comments != null) all.AddRange(comments);
            }
            var result = new Dictionary<string, ThreadInfo>();
            int order = 0;
            foreach (RawComment c in all) c.Order = order++;
            // A comment without a Thread belongs to no note (zzCN4E has 18,073 of them); skip it.
            foreach (var thread in all.Where(c => c.Thread.Length > 0).GroupBy(c => c.Thread))
            {
                List<RawComment> list = thread.OrderBy(c => c.Date).ThenBy(c => c.Order).ToList();
                var info = new ThreadInfo();
                info.IsConflict = string.Equals(list[0].Type, "conflict", StringComparison.OrdinalIgnoreCase);
                info.IsBiblicalTerm = list.Any(c => c.TermId);
                info.Tags = TagsOf(list.Select(c => c.Added).ToList(), list.Select(c => c.Removed).ToList(), info.IsConflict);
                foreach (RawComment c in list) result[MatchKey(c.Date, c.User)] = info;
            }
            return result;
        }

        /// <summary>Paratext's CommentThread.TagIds, from each comment's added and removed tag ids in order.</summary>
        public static List<int> TagsOf(IList<List<int>> added, IList<List<int>> removed, bool isConflict)
        {
            if (isConflict) return new List<int> { ConflictTag };
            var tags = new List<int>();
            var seen = new HashSet<int>();
            for (int i = added.Count - 1; i >= 0; i--)
            {
                foreach (int id in added[i])
                {
                    if (seen.Contains(id)) continue;
                    tags.Add(id);
                    seen.Add(id);
                }
                foreach (int id in removed[i]) seen.Add(id);
            }
            if (tags.Count == 0) tags.Add(ToDoTag);
            return tags;
        }

        /// <summary>The comments of one notes file, from the cache while the file is unchanged.</summary>
        private static List<RawComment> Comments(string path)
        {
            try
            {
                var fi = new FileInfo(path);
                lock (Gate)
                {
                    CachedFile cached;
                    if (Cache.TryGetValue(path, out cached) && cached.Size == fi.Length && cached.Time == fi.LastWriteTimeUtc)
                        return cached.Comments;
                }
                List<RawComment> comments = Parse(path);
                lock (Gate)
                {
                    Cache[path] = new CachedFile { Size = fi.Length, Time = fi.LastWriteTimeUtc, Comments = comments };
                }
                return comments;
            }
            catch (Exception)
            {
                return null;   // a file being written or damaged: its notes keep unknown tags
            }
        }

        private static List<RawComment> Parse(string path)
        {
            var list = new List<RawComment>();
            var settings = new XmlReaderSettings();
            settings.DtdProcessing = DtdProcessing.Ignore;
            settings.XmlResolver = null;
            settings.IgnoreWhitespace = true;
            settings.IgnoreComments = true;
            using (XmlReader r = XmlReader.Create(path, settings))
            {
                while (r.Read())
                {
                    if (r.NodeType != XmlNodeType.Element || r.Name != "Comment") continue;
                    var c = new RawComment();
                    c.Thread = r.GetAttribute("Thread") ?? "";
                    c.User = r.GetAttribute("User") ?? "";
                    DateTimeOffset date;
                    DateTimeOffset.TryParse(r.GetAttribute("Date") ?? "", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
                    c.Date = date;
                    c.Type = "";
                    c.Added = new List<int>();
                    c.Removed = new List<int>();
                    list.Add(c);
                    if (r.IsEmptyElement) continue;
                    int depth = r.Depth;
                    r.Read();
                    while (!(r.NodeType == XmlNodeType.EndElement && r.Depth == depth) && !r.EOF)
                    {
                        if (r.NodeType == XmlNodeType.Element && r.Depth == depth + 1)
                        {
                            switch (r.Name)
                            {
                                case "Type":
                                    c.Type = r.ReadElementContentAsString().Trim();
                                    continue;
                                case "TagAdded":
                                    AddIds(c.Added, r.ReadElementContentAsString());
                                    continue;
                                case "TagRemoved":
                                    AddIds(c.Removed, r.ReadElementContentAsString());
                                    continue;
                                case "BiblicalTermId":
                                    if (r.ReadElementContentAsString().Trim().Length > 0) c.TermId = true;
                                    continue;
                                default:
                                    r.Skip();
                                    continue;
                            }
                        }
                        r.Read();
                    }
                }
            }
            return list;
        }

        /// <summary>Tag ids, one per element or several separated by commas.</summary>
        private static void AddIds(List<int> ids, string text)
        {
            foreach (string part in (text ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int id;
                if (int.TryParse(part.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out id)) ids.Add(id);
            }
        }
    }

    /// <summary>A note tag: its id and name.</summary>
    public sealed class TagInfo
    {
        public int Id;
        public string Name;

        public override string ToString()
        {
            return Name;
        }
    }

    /// <summary>The project's tags (commentTags.xml) plus Paratext's built-in ones.</summary>
    public static class CommentTags
    {
        public const string FileName = "commentTags.xml";

        /// <summary>The tags of commentTags.xml in file order, then the built-in Conflict, Spelling and
        /// Biblical term tags (with the window's names); To Do (1) is added first when the file lacks it.
        /// Null when the project folder is unknown.</summary>
        public static IList<TagInfo> Read(string projectFolder, Strings s)
        {
            if (string.IsNullOrEmpty(projectFolder) || !Directory.Exists(projectFolder)) return null;
            var list = new List<TagInfo>();
            string path = Path.Combine(projectFolder, FileName);
            if (File.Exists(path))
            {
                try
                {
                    var doc = new XmlDocument();
                    var settings = new XmlReaderSettings();
                    settings.DtdProcessing = DtdProcessing.Ignore;
                    settings.XmlResolver = null;
                    using (XmlReader reader = XmlReader.Create(path, settings)) doc.Load(reader);
                    foreach (XmlElement e in doc.DocumentElement.SelectNodes("Tag"))
                    {
                        int id;
                        if (!int.TryParse(e.GetAttribute("Id"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out id)) continue;
                        if (list.Any(x => x.Id == id)) continue;
                        string name = e.GetAttribute("Name").Trim();
                        list.Add(new TagInfo { Id = id, Name = name.Length > 0 ? name : id.ToString(CultureInfo.InvariantCulture) });
                    }
                }
                catch (Exception) { }   // a damaged file: the built-in tags only
            }
            if (!list.Any(x => x.Id == NotesFiles.ToDoTag))
                list.Insert(0, new TagInfo { Id = NotesFiles.ToDoTag, Name = s.TagToDo });
            AddBuiltIn(list, NotesFiles.ConflictTag, s.TagConflict);
            AddBuiltIn(list, NotesFiles.SpellingTag, s.TagSpelling);
            AddBuiltIn(list, NotesFiles.BiblicalTermTag, s.TagBiblicalTerm);
            return list;
        }

        private static void AddBuiltIn(List<TagInfo> list, int id, string name)
        {
            if (!list.Any(x => x.Id == id)) list.Add(new TagInfo { Id = id, Name = name });
        }

        /// <summary>The tag names of a note, "#id" for an id the list does not have.</summary>
        public static string Names(IList<int> ids, IList<TagInfo> tags)
        {
            return ids == null ? "" : string.Join(", ", NameList(ids, tags).ToArray());
        }

        /// <summary>The tag names of a note, in its own project's list ("#id" for an id the list does not have).</summary>
        public static List<string> NameList(IList<int> ids, IList<TagInfo> tags)
        {
            var names = new List<string>();
            foreach (int id in ids)
            {
                TagInfo t = tags != null ? tags.FirstOrDefault(x => x.Id == id) : null;
                names.Add(t != null ? t.Name : "#" + id.ToString(CultureInfo.InvariantCulture));
            }
            return names;
        }
    }
}
