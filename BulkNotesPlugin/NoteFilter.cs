using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BulkNotes
{
    public enum StatusFilter
    {
        Unresolved = 0,
        Resolved = 1,
        All = 2,
    }

    /// <summary>Read or unread by the current user (the user's request, 1.4).</summary>
    public enum ReadFilter
    {
        Any = 0,
        Unread = 1,
        Read = 2,
    }

    public enum DateField
    {
        /// <summary>The date of the first comment.</summary>
        Created = 0,
        /// <summary>The date of the last comment.</summary>
        LastActivity = 1,
    }

    /// <summary>Which of the notes read are listed. Null or empty fields do not filter.</summary>
    public sealed class NoteFilter
    {
        /// <summary>Stands for "assigned to nobody" in AssignedTo.</summary>
        public const string Nobody = "";

        public StatusFilter Status = StatusFilter.Unresolved;
        /// <summary>With Status: Resolved + Unread finds the resolved notes not read yet.</summary>
        public ReadFilter Read = ReadFilter.Any;
        /// <summary>The author of the first comment.</summary>
        public string CreatedBy;
        /// <summary>The author of any comment.</summary>
        public string CommentedBy;
        /// <summary>Who the note is assigned to now; Nobody ("") = not assigned; null = anyone.</summary>
        public string AssignedTo;
        public DateField DateField = DateField.Created;
        /// <summary>First day included (local date), or null.</summary>
        public DateTime? From;
        /// <summary>Last day included (local date), or null.</summary>
        public DateTime? To;
        /// <summary>Words in any comment or in the words the note is attached to (any case).</summary>
        public string Text;
        /// <summary>Only notes covering this verse (0 = any).</summary>
        public int Verse;
        /// <summary>Only notes with a tag of this name (null = any). Names, not ids, because each
        /// project numbers its own tags. Conflict notes have the Conflict tag. Notes whose tags are
        /// unknown (no notes files) never match a tag.</summary>
        public string TagName;

        /// <summary>Whether resolved notes must be read at all.</summary>
        public bool NeedsResolved
        {
            get { return Status != StatusFilter.Unresolved; }
        }

        public bool Matches(NoteThread t)
        {
            if (Status == StatusFilter.Unresolved && t.IsResolved) return false;
            if (Status == StatusFilter.Resolved && !t.IsResolved) return false;
            if (Read == ReadFilter.Unread && t.IsRead) return false;
            if (Read == ReadFilter.Read && !t.IsRead) return false;
            if (Verse > 0 && !t.Covers(Verse)) return false;
            if (TagName != null && (t.TagNames == null || !t.TagNames.Any(n => Same(n, TagName)))) return false;
            if (!string.IsNullOrEmpty(CreatedBy) && !Same(t.CreatedBy, CreatedBy)) return false;
            if (!string.IsNullOrEmpty(CommentedBy) && !t.Comments.Any(c => Same(c.Author, CommentedBy))) return false;
            if (AssignedTo != null)
            {
                if (AssignedTo == Nobody) { if (t.IsAssigned) return false; }
                else if (!Same(t.AssignedUser, AssignedTo)) return false;
            }
            if (From.HasValue || To.HasValue)
            {
                DateTimeOffset when = DateField == DateField.Created ? t.Created : t.LastDate;
                if (when == DateTimeOffset.MinValue) return false;
                DateTime day = when.LocalDateTime.Date;
                if (From.HasValue && day < From.Value.Date) return false;
                if (To.HasValue && day > To.Value.Date) return false;
            }
            if (!string.IsNullOrEmpty(Text))
            {
                string words = Text.Trim();
                if (words.Length > 0 && !Contains(t.SelectedText, words) && !t.Comments.Any(c => Contains(c.Text, words)))
                    return false;
            }
            return true;
        }

        public IList<NoteThread> Apply(IEnumerable<NoteThread> notes)
        {
            return notes.Where(Matches).ToList();
        }

        private static bool Same(string a, string b)
        {
            return string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool Contains(string text, string words)
        {
            return !string.IsNullOrEmpty(text) &&
                CultureInfo.InvariantCulture.CompareInfo.IndexOf(text, words, CompareOptions.IgnoreCase) >= 0;
        }
    }
}
