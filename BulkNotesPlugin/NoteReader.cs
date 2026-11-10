using System;
using System.Collections.Generic;
using System.Linq;

namespace BulkNotes
{
    /// <summary>
    /// Reads the notes of several projects as one list (the user's request, 1.4): the same book,
    /// chapter or whole project from each, with each note's project and tag names set, and its
    /// key made unique across projects by the project ID in front.
    /// </summary>
    public static class NoteReader
    {
        /// <summary>Reads the notes (book 0 = whole project, chapter 0 = whole book) in reference
        /// order; notes of the same verse stay in project order. tagLists gives each project's tags
        /// (null for a project whose tags are unknown). A project that cannot be read is skipped
        /// and named in errors.</summary>
        public static List<NoteThread> Read(IList<INoteProject> projects, int book, int chapter, bool includeResolved,
            IDictionary<INoteProject, IList<TagInfo>> tagLists, List<string> errors)
        {
            var all = new List<NoteThread>();
            foreach (INoteProject p in projects)
            {
                IList<NoteThread> notes;
                try
                {
                    notes = p.GetNotes(book, chapter, includeResolved);
                }
                catch (Exception ex)
                {
                    if (errors != null) errors.Add(p.ShortName + ": " + ex.Message);
                    continue;
                }
                IList<TagInfo> tags = null;
                if (tagLists != null) tagLists.TryGetValue(p, out tags);
                foreach (NoteThread t in notes)
                {
                    t.Project = p;
                    t.Key = KeyIn(p, t.Key);
                    t.TagNames = tags != null && t.Tags != null ? CommentTags.NameList(t.Tags, tags) : null;
                    all.Add(t);
                }
            }
            // Stable: within a verse, the projects keep their order and each project its own order.
            return all.OrderBy(t => t.Book).ThenBy(t => t.Chapter).ThenBy(t => t.Verse).ToList();
        }

        /// <summary>A note's key in the combined list.</summary>
        public static string KeyIn(INoteProject p, string key)
        {
            return p.Id + "|" + key;
        }

        /// <summary>The key as the project itself gives it (without the project ID in front).</summary>
        public static string ProjectKey(NoteThread t)
        {
            if (t.Project == null || t.Key == null) return t.Key;
            string prefix = t.Project.Id + "|";
            return t.Key.StartsWith(prefix, StringComparison.Ordinal) ? t.Key.Substring(prefix.Length) : t.Key;
        }

        /// <summary>Each project's tags (CommentTags.Read); null for a project whose folder is unknown.</summary>
        public static Dictionary<INoteProject, IList<TagInfo>> TagLists(IEnumerable<INoteProject> projects, Strings s)
        {
            var map = new Dictionary<INoteProject, IList<TagInfo>>();
            foreach (INoteProject p in projects) map[p] = CommentTags.Read(p.Folder, s);
            return map;
        }
    }
}
