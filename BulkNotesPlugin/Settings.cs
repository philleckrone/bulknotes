using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;

namespace BulkNotes
{
    public enum Scope
    {
        Verse = 0,
        Chapter = 1,
        Book = 2,
        /// <summary>Every note of the project.</summary>
        Project = 3,
    }

    /// <summary>
    /// The user's remembered choices: the projects ticked, the scope, Follow Paratext, the status and read
    /// filter, the date field and whether the filters are hidden. The other filters start empty each time. Saved at once on every
    /// change, in %APPDATA%\ParatextBulkNotes\settings.xml (one file per user).
    /// </summary>
    public sealed class Settings
    {
        private readonly string path;
        /// <summary>The IDs of the projects ticked in the Projects chooser (1.4); before 1.4 only
        /// LastProject was saved, which is read into this list.</summary>
        public readonly List<string> Projects = new List<string>();
        public Scope Scope = Scope.Chapter;
        public bool Follow = true;
        public StatusFilter Status = StatusFilter.Unresolved;
        public ReadFilter Read = ReadFilter.Any;
        public DateField DateField = DateField.Created;
        /// <summary>The filter rows are hidden (only the summary line shows).</summary>
        public bool HideFilters;

        private Settings(string path)
        {
            this.path = path;
        }

        public static Settings Load(string path)
        {
            var s = new Settings(path);
            try
            {
                if (path == null || !File.Exists(path)) return s;
                var doc = new XmlDocument();
                doc.Load(path);
                XmlElement root = doc.DocumentElement;
                foreach (XmlNode n in root.SelectNodes("Project"))
                    if (n.InnerText.Trim().Length > 0) s.Projects.Add(n.InnerText.Trim());
                string last = Value(root, "LastProject");
                if (s.Projects.Count == 0 && last.Length > 0) s.Projects.Add(last);
                s.Scope = (Scope)Number(root, "Scope", (int)Scope.Chapter, 3);
                s.Follow = !string.Equals(Value(root, "Follow"), "false", StringComparison.OrdinalIgnoreCase);
                s.Status = (StatusFilter)Number(root, "Status", (int)StatusFilter.Unresolved, 2);
                s.Read = (ReadFilter)Number(root, "Read", (int)ReadFilter.Any, 2);
                s.DateField = (DateField)Number(root, "DateField", (int)DateField.Created, 1);
                s.HideFilters = string.Equals(Value(root, "HideFilters"), "true", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { }   // a damaged file gives the defaults
            return s;
        }

        private static string Value(XmlElement root, string name)
        {
            XmlNode n = root.SelectSingleNode(name);
            return n != null ? n.InnerText.Trim() : "";
        }

        private static int Number(XmlElement root, string name, int dflt, int max)
        {
            int n;
            return int.TryParse(Value(root, name), out n) && n >= 0 && n <= max ? n : dflt;
        }

        public void Save()
        {
            if (path == null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var xs = new XmlWriterSettings();
                xs.Indent = true;
                xs.Encoding = new UTF8Encoding(false);
                using (XmlWriter w = XmlWriter.Create(path, xs))
                {
                    w.WriteStartElement("BulkNotes");
                    foreach (string id in Projects) w.WriteElementString("Project", id);
                    w.WriteElementString("Scope", ((int)Scope).ToString());
                    w.WriteElementString("Follow", Follow ? "true" : "false");
                    w.WriteElementString("Status", ((int)Status).ToString());
                    w.WriteElementString("Read", ((int)Read).ToString());
                    w.WriteElementString("DateField", ((int)DateField).ToString());
                    w.WriteElementString("HideFilters", HideFilters ? "true" : "false");
                    w.WriteEndElement();
                }
            }
            catch (Exception) { }   // the choices are a convenience: never stop the tool over them
        }
    }
}
