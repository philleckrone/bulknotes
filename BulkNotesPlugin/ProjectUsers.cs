using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace BulkNotes
{
    /// <summary>
    /// The people still active in a project, from its ProjectUserAccess.xml: every
    /// &lt;User UserName="..."&gt; whose &lt;Role&gt; is not "None" (the user's rule; Administrator,
    /// Consultant, TeamMember and Observer all count). A user with no Role is not active.
    /// </summary>
    public static class ProjectUsers
    {
        public const string FileName = "ProjectUserAccess.xml";

        /// <summary>The active users in file order, or null when the file is missing or unreadable.</summary>
        public static IList<string> Active(string projectFolder)
        {
            if (string.IsNullOrEmpty(projectFolder)) return null;
            string path = Path.Combine(projectFolder, FileName);
            if (!File.Exists(path)) return null;
            try
            {
                var doc = new XmlDocument();
                var settings = new XmlReaderSettings();
                settings.DtdProcessing = DtdProcessing.Ignore;
                settings.XmlResolver = null;
                using (XmlReader reader = XmlReader.Create(path, settings)) doc.Load(reader);
                var list = new List<string>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (XmlElement user in doc.DocumentElement.SelectNodes("User"))
                {
                    string name = user.GetAttribute("UserName").Trim();
                    XmlNode role = user.SelectSingleNode("Role");
                    string r = role != null ? role.InnerText.Trim() : "";
                    if (name.Length == 0 || r.Length == 0 || string.Equals(r, "None", StringComparison.OrdinalIgnoreCase)) continue;
                    if (seen.Add(name)) list.Add(name);
                }
                return list;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
