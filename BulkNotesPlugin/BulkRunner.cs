using System;
using System.Collections.Generic;
using System.Linq;

namespace BulkNotes
{
    public enum BulkAction
    {
        Resolve,
        Assign,
        /// <summary>Marks the notes read for the current user (1.5).</summary>
        MarkRead,
        /// <summary>Marks the notes unread for the current user (1.6).</summary>
        MarkUnread,
    }

    public sealed class BulkReport
    {
        /// <summary>The project these notes belong to (RunAll gives one report per project).</summary>
        public INoteProject Project;
        public BulkAction Action;
        /// <summary>For Assign: the user ("" = nobody).</summary>
        public string User;
        /// <summary>The notes changed (references).</summary>
        public readonly List<string> Done = new List<string>();
        /// <summary>Keys of the notes changed, to check them afterwards.</summary>
        public readonly List<string> DoneKeys = new List<string>();
        /// <summary>Notes that needed no change (already resolved, or already assigned to that user).</summary>
        public readonly List<string> Skipped = new List<string>();
        /// <summary>"LUK 11:42: message" for each note Paratext refused.</summary>
        public readonly List<string> Failed = new List<string>();
        /// <summary>Notes Paratext accepted, but which read back unchanged.</summary>
        public readonly List<string> NotConfirmed = new List<string>();
        /// <summary>Assign: the user is not active in this project, so its notes were left as they
        /// are (the user's rule, 1.4).</summary>
        public readonly List<string> NotActive = new List<string>();
        /// <summary>The notes could not be locked: nothing was changed.</summary>
        public bool NoLock;
        /// <summary>MarkRead/MarkUnread: this Paratext does not let the tool change the read status; nothing was changed.</summary>
        public bool NoMarkRead;
        /// <summary>Resolve/Assign: how many of the notes changed could not then be marked read (1.6).</summary>
        public int ReadNotMarked;
        /// <summary>The changes were not read back (Check was not run).</summary>
        public bool Checked;
    }

    /// <summary>
    /// Resolves or assigns notes under one lock on the project's notes, then (Check) compares the
    /// notes read back with what was asked, because Paratext can accept a change and not keep it.
    /// </summary>
    public static class BulkRunner
    {
        public static BulkReport Run(INoteProject project, BulkAction action, IList<NoteThread> notes,
            string user, string comment, Strings s)
        {
            var report = new BulkReport { Action = action, User = user ?? "" };
            comment = (comment ?? "").Trim();
            var todo = new List<NoteThread>();
            foreach (NoteThread t in notes)
            {
                if (NeedsNoChange(t, action, report.User)) report.Skipped.Add(t.RangeReference);
                else todo.Add(t);
            }
            if (todo.Count == 0) return report;
            if (action == BulkAction.MarkRead || action == BulkAction.MarkUnread)
            {
                // No lock: the read status is the user's own, outside the project.
                bool read = action == BulkAction.MarkRead;
                if (!project.CanChangeRead(todo[0], read))
                {
                    report.NoMarkRead = true;
                    return report;
                }
                foreach (NoteThread t in todo)
                {
                    try
                    {
                        project.SetRead(t, read);
                        report.Done.Add(t.RangeReference);
                        report.DoneKeys.Add(t.Key);
                    }
                    catch (Exception ex)
                    {
                        report.Failed.Add(t.RangeReference + ": " + Message(ex));
                    }
                }
                return report;
            }
            var changed = new List<NoteThread>();
            using (INoteWriter writer = project.OpenWriter())
            {
                if (writer == null)
                {
                    report.NoLock = true;
                    return report;
                }
                foreach (NoteThread t in todo)
                {
                    try
                    {
                        if (action == BulkAction.Resolve)
                            writer.Resolve(t, comment);
                        else
                            // Paratext refuses a comment without text, so an assignment with no
                            // comment of the user's says what it does.
                            writer.Assign(t, report.User, comment.Length > 0 ? comment : AssignText(s, report.User));
                        report.Done.Add(t.RangeReference);
                        report.DoneKeys.Add(t.Key);
                        changed.Add(t);
                    }
                    catch (Exception ex)
                    {
                        report.Failed.Add(t.RangeReference + ": " + Message(ex));
                    }
                }
                if (report.Done.Count > 0) writer.SendNotifications();
            }
            MarkChangedRead(project, changed, report);
            return report;
        }

        /// <summary>
        /// A note the user resolved or assigned has been read (the user's rule, 1.6), so it is marked
        /// read afterwards, the new comment included. Only notes changed here: notes that needed no
        /// change keep their read status. A Paratext that does not allow it is reported, not stopped on.
        /// </summary>
        private static void MarkChangedRead(INoteProject project, List<NoteThread> changed, BulkReport report)
        {
            if (changed.Count == 0) return;
            bool can;
            try
            {
                can = project.CanChangeRead(changed[0], true);
            }
            catch (Exception)
            {
                can = false;
            }
            if (!can)
            {
                report.ReadNotMarked = changed.Count;
                return;
            }
            foreach (NoteThread t in changed)
            {
                try
                {
                    project.SetRead(t, true);
                }
                catch (Exception)
                {
                    report.ReadNotMarked++;
                }
            }
        }

        /// <summary>
        /// Runs the action on notes of several projects (the user's request, 1.4): one lock and one
        /// report per project, in the order the projects first appear. Assign leaves a project's
        /// notes alone when the user is not active there (Team and nobody are valid everywhere).
        /// readBack (may be null) reads a project's notes again, resolved ones included, for Check.
        /// </summary>
        public static List<BulkReport> RunAll(BulkAction action, IList<NoteThread> notes, string user, string comment,
            Strings s, Func<INoteProject, IList<NoteThread>> readBack)
        {
            var reports = new List<BulkReport>();
            var projects = new List<INoteProject>();
            foreach (NoteThread t in notes)
                if (!projects.Contains(t.Project)) projects.Add(t.Project);
            foreach (INoteProject p in projects)
            {
                List<NoteThread> mine = notes.Where(t => t.Project == p).ToList();
                BulkReport r;
                if (action == BulkAction.Assign && !UserActiveIn(p, user))
                {
                    r = new BulkReport { Action = action, User = user ?? "" };
                    r.NotActive.AddRange(mine.Select(t => t.RangeReference));
                }
                else
                {
                    r = Run(p, action, mine, user, comment, s);
                    if (r.Done.Count > 0 && readBack != null) Check(r, readBack(p));
                }
                r.Project = p;
                reports.Add(r);
            }
            return reports;
        }

        /// <summary>Team and nobody can be assigned in any project; a person only where active.</summary>
        public static bool UserActiveIn(INoteProject p, string user)
        {
            if (string.IsNullOrEmpty(user) || user == Strings.Team) return true;
            return p.Users.Any(u => string.Equals(u, user, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Every report, one block per project.</summary>
        public static string DescribeAll(IList<BulkReport> reports, Strings s)
        {
            return string.Join(Environment.NewLine + Environment.NewLine,
                reports.Select(r => Describe(r, r.Project, s)).ToArray());
        }

        public static bool NeedsNoChange(NoteThread t, BulkAction action, string user)
        {
            if (action == BulkAction.Resolve) return t.IsResolved;
            if (action == BulkAction.MarkRead) return t.IsRead;
            if (action == BulkAction.MarkUnread) return !t.IsRead;
            return string.Equals(t.AssignedUser ?? "", user ?? "", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The comment added by Assign when the user typed none.</summary>
        public static string AssignText(Strings s, string user)
        {
            return string.IsNullOrEmpty(user) ? s.UnassignedComment : string.Format(s.AssignedComment, UserText(s, user));
        }

        /// <summary>Compares the notes read again after Run (resolved ones included) with what was asked.</summary>
        public static void Check(BulkReport report, IList<NoteThread> after)
        {
            var byKey = new Dictionary<string, NoteThread>();
            foreach (NoteThread t in after)
                if (t.Key != null) byKey[t.Key] = t;
            for (int i = 0; i < report.DoneKeys.Count; i++)
            {
                NoteThread t;
                bool ok = byKey.TryGetValue(report.DoneKeys[i], out t) && NeedsNoChange(t, report.Action, report.User);
                if (!ok) report.NotConfirmed.Add(report.Done[i]);
            }
            report.Checked = true;
        }

        /// <summary>The report as the window shows it.</summary>
        public static string Describe(BulkReport r, INoteProject project, Strings s)
        {
            var lines = new List<string>();
            if (r.NoLock)
            {
                lines.Add(string.Format(s.ResNoLock, project.ShortName));
                return string.Join(Environment.NewLine, lines.ToArray());
            }
            if (r.NoMarkRead)
            {
                lines.Add(string.Format(s.ResNoMarkRead, project.ShortName));
                return string.Join(Environment.NewLine, lines.ToArray());
            }
            if (r.NotActive.Count > 0)
            {
                lines.Add(string.Format(s.ResNotActive, r.NotActive.Count, UserText(s, r.User), project.ShortName));
                foreach (string d in r.NotActive) lines.Add("  " + d);
                return string.Join(Environment.NewLine, lines.ToArray());
            }
            int confirmed = r.Done.Count - r.NotConfirmed.Count;
            if (r.Action == BulkAction.Resolve)
                lines.Add(string.Format(s.ResResolved, confirmed, project.ShortName));
            else if (r.Action == BulkAction.MarkRead)
                lines.Add(string.Format(s.ResMarkedRead, confirmed, project.ShortName));
            else if (r.Action == BulkAction.MarkUnread)
                lines.Add(string.Format(s.ResMarkedUnread, confirmed, project.ShortName));
            else
                lines.Add(string.Format(s.ResAssigned, confirmed, project.ShortName, UserText(s, r.User)));
            foreach (string d in r.Done.Where(d => !r.NotConfirmed.Contains(d)))
                lines.Add("  " + d);
            if (r.Skipped.Count > 0)
            {
                lines.Add(string.Format(r.Action == BulkAction.Resolve ? s.ResSkippedResolved
                    : r.Action == BulkAction.MarkRead ? s.ResSkippedRead
                    : r.Action == BulkAction.MarkUnread ? s.ResSkippedUnread : s.ResSkippedAssigned, r.Skipped.Count));
                foreach (string d in r.Skipped) lines.Add("  " + d);
            }
            if (r.Failed.Count > 0)
            {
                lines.Add(string.Format(s.ResFailed, r.Failed.Count));
                foreach (string d in r.Failed) lines.Add("  " + d);
            }
            if (r.NotConfirmed.Count > 0)
            {
                lines.Add(string.Format(s.ResNotConfirmed, r.NotConfirmed.Count));
                foreach (string d in r.NotConfirmed) lines.Add("  " + d);
            }
            if (r.ReadNotMarked > 0) lines.Add(string.Format(s.ResAutoReadFailed, r.ReadNotMarked));
            return string.Join(Environment.NewLine, lines.ToArray());
        }

        /// <summary>The user as shown: a name, "Team" or "(nobody)".</summary>
        public static string UserText(Strings s, string user)
        {
            if (string.IsNullOrEmpty(user)) return s.Nobody;
            return user == Strings.Team ? s.TeamText : user;
        }

        private static string Message(Exception ex)
        {
            while (ex.InnerException != null && ex.InnerException.Message.Length > 0) ex = ex.InnerException;
            return ex.Message;
        }
    }
}
