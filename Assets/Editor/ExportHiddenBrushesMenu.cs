// Copyright 2026 The Open Brush Authors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.



// Copyright 2026 The Open Brush Authors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.


using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace TiltBrush
{
    /// Editor menu: export the hidden-brush GUID set to a readable text report
    /// (DurableName | Description | Guid | OK/MISSING) for Unity search and scripts.
    public static class ExportHiddenBrushesMenu
    {
        private const string kMenuPath = "Advanced/Export Hidden Brushes";
        private const string kBrushSetsFileName = "BrushSets.json";
        private const string kLegacyFileName = "HiddenBrushes.json";

        [MenuItem(kMenuPath)]
        public static void Export()
        {
            List<Guid> hiddenGuids = ReadHiddenGuids();
            if (hiddenGuids == null)
            {
                EditorUtility.DisplayDialog(
                    "Export Hidden Brushes",
                    "Could not read hidden-brush list.\n\nExpected JSON at the Open Brush user data path, or a live set in Play Mode.",
                    "OK");
                return;
            }

            if (hiddenGuids.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "Export Hidden Brushes",
                    "Hidden set is empty — nothing to export.",
                    "OK");
                return;
            }

            Dictionary<Guid, BrushDescriptor> lookup = BuildBrushLookup();
            string reportPath = WriteReport(hiddenGuids, lookup);

            EditorUtility.RevealInFinder(reportPath);
            Debug.Log($"Export Hidden Brushes: wrote {hiddenGuids.Count} entries to:\n{reportPath}");
        }

        [MenuItem("Advanced/Export Hidden Brushes (JSON)")]
        public static void ExportJson()
        {
            List<Guid> hiddenGuids = ReadHiddenGuids();
            if (hiddenGuids == null)
            {
                EditorUtility.DisplayDialog(
                    "Export Hidden Brushes (JSON)",
                    "Could not read hidden-brush list.\n\nExpected JSON at the Open Brush user data path, or a live set in Play Mode.",
                    "OK");
                return;
            }

            if (hiddenGuids.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "Export Hidden Brushes (JSON)",
                    "Hidden set is empty — nothing to export.",
                    "OK");
                return;
            }

            Dictionary<Guid, BrushDescriptor> lookup = BuildBrushLookup();
            string reportPath = WriteJsonReport(hiddenGuids, lookup);

            EditorUtility.RevealInFinder(reportPath);
            Debug.Log($"Export Hidden Brushes (JSON): wrote {hiddenGuids.Count} entries to:\n{reportPath}");
        }

        /// Prefer live HiddenBrushSet in Play Mode; otherwise read BrushSets.json
        /// then legacy HiddenBrushes.json from the user data folder.
        static List<Guid> ReadHiddenGuids()
        {
            if (Application.isPlaying)
            {
                HiddenBrushSet.PopulateFromDisk();
                return HiddenBrushSet.GetAll().ToList();
            }

            string brushSetsPath = GetUserDataPath(kBrushSetsFileName);
            if (File.Exists(brushSetsPath))
            {
                List<Guid> fromSets = ReadGuidsFromBrushSetsFile(brushSetsPath);
                if (fromSets != null)
                {
                    return fromSets;
                }
            }

            string legacyPath = GetUserDataPath(kLegacyFileName);
            if (File.Exists(legacyPath))
            {
                return ReadGuidsFromLegacyFile(legacyPath);
            }

            Debug.LogWarning(
                "Export Hidden Brushes: no BrushSets.json or HiddenBrushes.json under Open Brush user data.");
            return null;
        }

        static string GetUserDataPath(string fileName)
        {
            string personal = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Personal);
            if (string.IsNullOrEmpty(personal))
            {
                personal = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
            }

            if (Application.platform == RuntimePlatform.OSXEditor ||
                Application.platform == RuntimePlatform.LinuxEditor)
            {
                personal = Path.Combine(personal, "Documents");
            }

            return Path.Combine(personal, App.kAppFolderName, fileName);
        }

        static List<Guid> ReadGuidsFromBrushSetsFile(string path)
        {
            try
            {
                string text = File.ReadAllText(path, Encoding.UTF8);
                var data = JsonConvert.DeserializeObject<BrushSetsDiskFormat>(text);
                if (data?.sets == null)
                {
                    return new List<Guid>();
                }

                var list = new List<Guid>();
                foreach (var set in data.sets)
                {
                    if (set == null || set.guids == null)
                    {
                        continue;
                    }

                    if (!string.Equals(set.id, "hidden", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    foreach (string s in set.guids)
                    {
                        if (Guid.TryParse(s, out Guid guid))
                        {
                            list.Add(guid);
                        }
                    }
                }

                return list;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Export Hidden Brushes: failed to read BrushSets.json: {e.Message}");
                return null;
            }
        }

        static List<Guid> ReadGuidsFromLegacyFile(string path)
        {
            try
            {
                string text = File.ReadAllText(path, Encoding.UTF8);
                var data = JsonConvert.DeserializeObject<HiddenDiskFormat>(text);
                if (data?.hidden == null)
                {
                    return new List<Guid>();
                }

                var list = new List<Guid>();
                foreach (string s in data.hidden)
                {
                    if (Guid.TryParse(s, out Guid guid))
                    {
                        list.Add(guid);
                    }
                }

                return list;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Export Hidden Brushes: failed to read legacy JSON: {e.Message}");
                return null;
            }
        }

        static Dictionary<Guid, BrushDescriptor> BuildBrushLookup()
        {
            var map = new Dictionary<Guid, BrushDescriptor>();
            string[] assetGuids = AssetDatabase.FindAssets("t:BrushDescriptor");
            foreach (string assetGuid in assetGuids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(assetGuid);
                BrushDescriptor brush = AssetDatabase.LoadAssetAtPath<BrushDescriptor>(assetPath);
                if (brush == null)
                {
                    continue;
                }

                Guid id = brush.m_Guid;
                if (id == Guid.Empty)
                {
                    continue;
                }

                if (!map.ContainsKey(id))
                {
                    map.Add(id, brush);
                }
            }

            return map;
        }

        /// Prefer localized Description when available; never require it.
        /// Edit Mode often has no SelectedLocale — catch and fall back without failing the export.
        static string SafeBrushDescription(BrushDescriptor brush)
        {
            if (brush == null)
            {
                return "";
            }

            try
            {
                string desc = brush.Description;
                if (!string.IsNullOrEmpty(desc))
                {
                    return desc;
                }
            }
            catch
            {
                // Localization not ready in Edit Mode, etc.
            }

            if (!string.IsNullOrEmpty(brush.DurableName))
            {
                return brush.DurableName;
            }

            if (!string.IsNullOrEmpty(brush.name))
            {
                return brush.name;
            }

            return "";
        }

        static string SafeDurableName(BrushDescriptor brush)
        {
            if (brush == null)
            {
                return "";
            }

            if (!string.IsNullOrEmpty(brush.DurableName))
            {
                return brush.DurableName;
            }

            if (!string.IsNullOrEmpty(brush.name))
            {
                return brush.name;
            }

            return "";
        }

        static string WriteReport(List<Guid> hiddenGuids, Dictionary<Guid, BrushDescriptor> lookup)
        {
            string dir = Path.Combine(Application.dataPath, "ExportedBrushSets");
            Directory.CreateDirectory(dir);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string reportPath = Path.Combine(dir, $"HiddenBrushes_{stamp}.txt");

            var sb = new StringBuilder();
            sb.AppendLine("# DurableName | Description | Guid | Status");
            sb.AppendLine($"# Exported {DateTime.Now:yyyy-MM-dd HH:mm:ss}  count={hiddenGuids.Count}");
            sb.AppendLine();

            int ok = 0;
            int missing = 0;

            foreach (Guid guid in hiddenGuids.OrderBy(g => g.ToString()))
            {
                if (lookup.TryGetValue(guid, out BrushDescriptor brush))
                {
                    string durable = SafeDurableName(brush).Replace('|', '/');
                    string desc = SafeBrushDescription(brush).Replace('|', '/');
                    sb.AppendLine($"{durable} | {desc} | {guid} | OK");
                    ok++;
                }
                else
                {
                    sb.AppendLine($" |  | {guid} | MISSING");
                    missing++;
                }
            }

            sb.AppendLine();
            sb.AppendLine($"# Summary: OK={ok}  MISSING={missing}");

            File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
            return Path.GetFullPath(reportPath);
        }

        static string WriteJsonReport(List<Guid> hiddenGuids, Dictionary<Guid, BrushDescriptor> lookup)
        {
            string dir = Path.Combine(Application.dataPath, "ExportedBrushSets");
            Directory.CreateDirectory(dir);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string reportPath = Path.Combine(dir, $"HiddenBrushes_{stamp}.json");

            var rows = new List<ExportRow>();
            foreach (Guid guid in hiddenGuids.OrderBy(g => g.ToString()))
            {
                var row = new ExportRow();
                row.guid = guid.ToString();
                if (lookup.TryGetValue(guid, out BrushDescriptor brush))
                {
                    row.durableName = SafeDurableName(brush);
                    row.description = SafeBrushDescription(brush);
                    row.status = "OK";
                }
                else
                {
                    row.durableName = "";
                    row.description = "";
                    row.status = "MISSING";
                }

                rows.Add(row);
            }

            string json = JsonConvert.SerializeObject(rows, Formatting.Indented);
            File.WriteAllText(reportPath, json, Encoding.UTF8);
            return Path.GetFullPath(reportPath);
        }

        [Serializable]
        class HiddenDiskFormat
        {
            public int version = 1;
            public List<string> hidden = new List<string>();
        }

        [Serializable]
        class BrushSetsDiskFormat
        {
            public int version = 2;
            public string activeDisplayMode = "Working";
            public string activeEditSetId = "hidden";
            public List<BrushSetRecord> sets = new List<BrushSetRecord>();
        }

        [Serializable]
        class BrushSetRecord
        {
            public string id;
            public string name;
            public string color;
            public string kind;
            public List<string> guids = new List<string>();
        }
    }

    [Serializable]
    class ExportRow
    {
        public string durableName;
        public string description;
        public string guid;
        public string status;
    }
}

/*

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace TiltBrush
{
    /// Editor menu: export the hidden-brush GUID set to a readable text report
    /// (DurableName | Description | Guid | OK/MISSING) for Unity search and scripts.
    public static class ExportHiddenBrushesMenu
    {
        private const string kMenuPath = "Advanced/Export Hidden Brushes";
        private const string kBrushSetsFileName = "BrushSets.json";
        private const string kLegacyFileName = "HiddenBrushes.json";

        [MenuItem(kMenuPath)]
        public static void Export()
        {
            List<Guid> hiddenGuids = ReadHiddenGuids();
            if (hiddenGuids == null)
            {
                EditorUtility.DisplayDialog(
                    "Export Hidden Brushes",
                    "Could not read hidden-brush list.\n\nExpected JSON at the Open Brush user data path, or a live set in Play Mode.",
                    "OK");
                return;
            }

            if (hiddenGuids.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "Export Hidden Brushes",
                    "Hidden set is empty — nothing to export.",
                    "OK");
                return;
            }

            Dictionary<Guid, BrushDescriptor> lookup = BuildBrushLookup();
            string reportPath = WriteReport(hiddenGuids, lookup);

            EditorUtility.RevealInFinder(reportPath);
            Debug.Log($"Export Hidden Brushes: wrote {hiddenGuids.Count} entries to:\n{reportPath}");
        }

        [MenuItem("Advanced/Export Hidden Brushes (JSON)")]
        public static void ExportJson()
        {
            List<Guid> hiddenGuids = ReadHiddenGuids();
            if (hiddenGuids == null)
            {
                EditorUtility.DisplayDialog(
                    "Export Hidden Brushes (JSON)",
                    "Could not read hidden-brush list.\n\nExpected JSON at the Open Brush user data path, or a live set in Play Mode.",
                    "OK");
                return;
            }

            if (hiddenGuids.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "Export Hidden Brushes (JSON)",
                    "Hidden set is empty — nothing to export.",
                    "OK");
                return;
            }

            Dictionary<Guid, BrushDescriptor> lookup = BuildBrushLookup();
            string reportPath = WriteJsonReport(hiddenGuids, lookup);

            EditorUtility.RevealInFinder(reportPath);
            Debug.Log($"Export Hidden Brushes (JSON): wrote {hiddenGuids.Count} entries to:\n{reportPath}");
        }

        /// Prefer live HiddenBrushSet in Play Mode; otherwise read BrushSets.json
        /// then legacy HiddenBrushes.json from the user data folder.
        static List<Guid> ReadHiddenGuids()
        {
            if (Application.isPlaying)
            {
                HiddenBrushSet.PopulateFromDisk();
                return HiddenBrushSet.GetAll().ToList();
            }

            string brushSetsPath = GetUserDataPath(kBrushSetsFileName);
            if (File.Exists(brushSetsPath))
            {
                List<Guid> fromSets = ReadGuidsFromBrushSetsFile(brushSetsPath);
                if (fromSets != null)
                {
                    return fromSets;
                }
            }

            string legacyPath = GetUserDataPath(kLegacyFileName);
            if (File.Exists(legacyPath))
            {
                return ReadGuidsFromLegacyFile(legacyPath);
            }

            Debug.LogWarning(
                "Export Hidden Brushes: no BrushSets.json or HiddenBrushes.json under Open Brush user data.");
            return null;
        }

        static string GetUserDataPath(string fileName)
        {
            string personal = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Personal);
            if (string.IsNullOrEmpty(personal))
            {
                personal = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
            }

            if (Application.platform == RuntimePlatform.OSXEditor ||
                Application.platform == RuntimePlatform.LinuxEditor)
            {
                personal = Path.Combine(personal, "Documents");
            }

            return Path.Combine(personal, App.kAppFolderName, fileName);
        }

        static List<Guid> ReadGuidsFromBrushSetsFile(string path)
        {
            try
            {
                string text = File.ReadAllText(path, Encoding.UTF8);
                var data = JsonConvert.DeserializeObject<BrushSetsDiskFormat>(text);
                if (data?.sets == null)
                {
                    return new List<Guid>();
                }

                var list = new List<Guid>();
                foreach (var set in data.sets)
                {
                    if (set == null || set.guids == null)
                    {
                        continue;
                    }

                    if (!string.Equals(set.id, "hidden", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    foreach (string s in set.guids)
                    {
                        if (Guid.TryParse(s, out Guid guid))
                        {
                            list.Add(guid);
                        }
                    }
                }

                return list;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Export Hidden Brushes: failed to read BrushSets.json: {e.Message}");
                return null;
            }
        }

        static List<Guid> ReadGuidsFromLegacyFile(string path)
        {
            try
            {
                string text = File.ReadAllText(path, Encoding.UTF8);
                var data = JsonConvert.DeserializeObject<HiddenDiskFormat>(text);
                if (data?.hidden == null)
                {
                    return new List<Guid>();
                }

                var list = new List<Guid>();
                foreach (string s in data.hidden)
                {
                    if (Guid.TryParse(s, out Guid guid))
                    {
                        list.Add(guid);
                    }
                }

                return list;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Export Hidden Brushes: failed to read legacy JSON: {e.Message}");
                return null;
            }
        }

        static Dictionary<Guid, BrushDescriptor> BuildBrushLookup()
        {
            var map = new Dictionary<Guid, BrushDescriptor>();
            string[] assetGuids = AssetDatabase.FindAssets("t:BrushDescriptor");
            foreach (string assetGuid in assetGuids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(assetGuid);
                BrushDescriptor brush = AssetDatabase.LoadAssetAtPath<BrushDescriptor>(assetPath);
                if (brush == null)
                {
                    continue;
                }

                Guid id = brush.m_Guid;
                if (id == Guid.Empty)
                {
                    continue;
                }

                if (!map.ContainsKey(id))
                {
                    map.Add(id, brush);
                }
            }

            return map;
        }

        static string WriteReport(List<Guid> hiddenGuids, Dictionary<Guid, BrushDescriptor> lookup)
        {
            string dir = Path.Combine(Application.dataPath, "ExportedBrushSets");
            Directory.CreateDirectory(dir);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string reportPath = Path.Combine(dir, $"HiddenBrushes_{stamp}.txt");

            var sb = new StringBuilder();
            sb.AppendLine("# DurableName | Description | Guid | Status");
            sb.AppendLine($"# Exported {DateTime.Now:yyyy-MM-dd HH:mm:ss}  count={hiddenGuids.Count}");
            sb.AppendLine();

            int ok = 0;
            int missing = 0;

            foreach (Guid guid in hiddenGuids.OrderBy(g => g.ToString()))
            {
                if (lookup.TryGetValue(guid, out BrushDescriptor brush))
                {
                    string durable = brush.DurableName ?? "";
                    string desc = brush.Description ?? "";
                    durable = durable.Replace('|', '/');
                    desc = desc.Replace('|', '/');
                    sb.AppendLine($"{durable} | {desc} | {guid} | OK");
                    ok++;
                }
                else
                {
                    sb.AppendLine($" |  | {guid} | MISSING");
                    missing++;
                }
            }

            sb.AppendLine();
            sb.AppendLine($"# Summary: OK={ok}  MISSING={missing}");

            File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
            return Path.GetFullPath(reportPath);
        }

        static string WriteJsonReport(List<Guid> hiddenGuids, Dictionary<Guid, BrushDescriptor> lookup)
        {
            string dir = Path.Combine(Application.dataPath, "ExportedBrushSets");
            Directory.CreateDirectory(dir);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string reportPath = Path.Combine(dir, $"HiddenBrushes_{stamp}.json");

            var rows = new List<ExportRow>();
            foreach (Guid guid in hiddenGuids.OrderBy(g => g.ToString()))
            {
                var row = new ExportRow();
                row.guid = guid.ToString();
                if (lookup.TryGetValue(guid, out BrushDescriptor brush))
                {
                    row.durableName = brush.DurableName ?? "";
                    row.description = brush.Description ?? "";
                    row.status = "OK";
                }
                else
                {
                    row.durableName = "";
                    row.description = "";
                    row.status = "MISSING";
                }

                rows.Add(row);
            }

            string json = JsonConvert.SerializeObject(rows, Formatting.Indented);
            File.WriteAllText(reportPath, json, Encoding.UTF8);
            return Path.GetFullPath(reportPath);
        }

        [Serializable]
        class HiddenDiskFormat
        {
            public int version = 1;
            public List<string> hidden = new List<string>();
        }

        [Serializable]
        class BrushSetsDiskFormat
        {
            public int version = 2;
            public string activeDisplayMode = "Working";
            public string activeEditSetId = "hidden";
            public List<BrushSetRecord> sets = new List<BrushSetRecord>();
        }

        [Serializable]
        class BrushSetRecord
        {
            public string id;
            public string name;
            public string color;
            public string kind;
            public List<string> guids = new List<string>();
        }
    }

    [Serializable]
    class ExportRow
    {
        public string durableName;
        public string description;
        public string guid;
        public string status;
    }
}

*/