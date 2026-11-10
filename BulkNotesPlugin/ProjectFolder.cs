using System;
using System.IO;
using System.Xml;
using Microsoft.Win32;

namespace BulkNotes
{
    /// <summary>
    /// Finds a project's folder, which the plugin API does not give: Settings_Directory under
    /// SOFTWARE\Paratext\8 (both registry views, then the current user), then projectsDir\ShortName
    /// when its Settings.xml Guid matches the project ID, otherwise the subfolder whose Guid matches.
    /// Same as the AutoCorrect and Project Settings plugins.
    /// </summary>
    internal static class ProjectFolder
    {
        public static string Find(string shortName, string projectId)
        {
            return Resolve(ProjectsDirectory(), shortName, projectId);
        }

        private static string ProjectsDirectory()
        {
            foreach (RegistryView view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            {
                string dir = ReadSettingsDirectory(RegistryHive.LocalMachine, view);
                if (dir != null) return dir;
            }
            return ReadSettingsDirectory(RegistryHive.CurrentUser, RegistryView.Default);
        }

        private static string ReadSettingsDirectory(RegistryHive hive, RegistryView view)
        {
            try
            {
                using (RegistryKey root = RegistryKey.OpenBaseKey(hive, view))
                using (RegistryKey key = root.OpenSubKey(@"SOFTWARE\Paratext\8"))
                {
                    if (key == null) return null;
                    string dir = key.GetValue("Settings_Directory") as string;
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        return dir.TrimEnd('\\');
                }
            }
            catch (Exception) { }
            return null;
        }

        private static string Resolve(string projectsDir, string shortName, string projectId)
        {
            if (string.IsNullOrEmpty(projectsDir) || !Directory.Exists(projectsDir)) return null;
            string byName = null;
            if (!string.IsNullOrEmpty(shortName))
            {
                string candidate = Path.Combine(projectsDir, shortName);
                string settings = Path.Combine(candidate, "Settings.xml");
                if (File.Exists(settings))
                {
                    byName = candidate;
                    string guid = ReadGuid(settings);
                    if (guid == null || string.IsNullOrEmpty(projectId) || SameId(guid, projectId))
                        return candidate;
                }
            }
            if (string.IsNullOrEmpty(projectId)) return byName;
            try
            {
                foreach (string dir in Directory.GetDirectories(projectsDir))
                {
                    string settings = Path.Combine(dir, "Settings.xml");
                    if (File.Exists(settings) && SameId(ReadGuid(settings), projectId))
                        return dir;
                }
            }
            catch (Exception) { }
            return byName;
        }

        private static bool SameId(string a, string b)
        {
            return a != null && b != null && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadGuid(string settingsPath)
        {
            try
            {
                var xmlSettings = new XmlReaderSettings();
                xmlSettings.DtdProcessing = DtdProcessing.Ignore;
                using (XmlReader reader = XmlReader.Create(settingsPath, xmlSettings))
                {
                    if (reader.ReadToFollowing("Guid"))
                    {
                        string guid = reader.ReadElementContentAsString();
                        return string.IsNullOrEmpty(guid) ? null : guid.Trim();
                    }
                }
            }
            catch (Exception) { }
            return null;
        }
    }
}
