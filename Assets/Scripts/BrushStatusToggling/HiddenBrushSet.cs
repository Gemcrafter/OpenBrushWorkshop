// Copyright 2026 The Open Brush Authors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
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
using Newtonsoft.Json;
using UnityEngine;

namespace TiltBrush
{
    /// Palette / shelf membership store.
    ///
    /// Slots Palette_01 .. Palette_N (ceiling 64). Seed 1-37 empty on first run.
    /// Colors are fixed to slot id. Names come from JSON + BrushPaletteConfig.
    ///
    /// Permanent slots 1-2: closed membership. Stock identity is BrushDescriptor
    /// tags stock_basic / stock_advanced (see StampStockBrushTags).
    /// Custom slots 3+: exclusive membership. Stock may hold one copy on a custom
    /// slot; permanent stock home is never cleared via this API.
    ///
    /// UntetheredPlacement (persisted): when Sort is on, catalog order is
    ///   Front -> Stock, Untethered, Slotted  (UntetheredToFront)
    ///   Back  -> Slotted, Untethered, Stock  (UntetheredToBack)
    public static class HiddenBrushSet
    {
        public const string PaletteIdPrefix = "Palette_";
        public const string DefaultEditPaletteId = "Palette_03";
        public const string HiddenSetId = "Palette_07";
        public const string AllDisplaySetId = "All";

        public const string StockBasicTag = "stock_basic";
        public const string StockAdvancedTag = "stock_advanced";

        public const int PermanentSlotBasic = 1;
        public const int PermanentSlotAdvanced = 2;
        public const int FirstWritableSlot = 3;

        private const string kFileName = "BrushSets.json";
        private const string kPlayerPrefKey = "HiddenBrushes";
        private const string kConfigResourceName = "ScriptableObjects/BrushPaletteConfig";
        private const int kCurrentVersion = 6;
        private const int kMaxPaletteIndex = 64;
        private const int kSeedPaletteCount = 37;

        public enum DisplayMode
        {
            Working,
            All
        }

        /// Catalog bracket when SortEnabled (see BrushCatalog.BeginReload).
        public enum UntetheredPlacementMode
        {
            Front,
            Back
        }

        [Serializable]
        public class SetRecord
        {
            public string id = "";
            public string name = "";
            public string color = "";
            public string kind = "user";
            public int displayOrder = 0;
            public List<string> guids = new List<string>();
        }

        [Serializable]
        class DiskFormat
        {
            public int version = kCurrentVersion;
            public string activeDisplayMode = "Working";
            public string activeEditSetId = DefaultEditPaletteId;
            public string activeDisplaySetId = AllDisplaySetId;
            public bool sortEnabled = false;
            public int looseDisplayOrder = 0;
            public string untetheredPlacement = "Front";
            public bool concealCustom = false;
            public List<SetRecord> sets = new List<SetRecord>();
        }

        // Slot colors: 1-2 white; all other seeded ids use the main neon cycle.
        static readonly string kWhiteHex = "#FFFFFF";

        static readonly string[] kNeonCycle =
        {
            "#E53935",
            "#FB8C00",
            "#FDD835",
            "#43A047",
            "#1E88E5",
            "#AB47BC",
            "#8C40D9"
        };

        private static Dictionary<string, HashSet<Guid>> m_Sets =
            new Dictionary<string, HashSet<Guid>>(StringComparer.Ordinal);
        private static Dictionary<string, SetRecord> m_SetMeta =
            new Dictionary<string, SetRecord>(StringComparer.Ordinal);
        private static DisplayMode m_ActiveDisplayMode = DisplayMode.Working;
        private static string m_ActiveEditSetId = DefaultEditPaletteId;
        private static string m_ActiveDisplaySetId = AllDisplaySetId;
        private static bool m_SortEnabled = false;
        private static int m_LooseDisplayOrder = 0;
        private static UntetheredPlacementMode m_UntetheredPlacement = UntetheredPlacementMode.Front;
        private static bool m_ConcealCustom = false;
        private static bool m_Loaded;

        public static string FilePath
        {
            get
            {
                return Path.Combine(App.UserPath(), kFileName);
            }
        }

        public static bool IsLoaded
        {
            get
            {
                return m_Loaded;
            }
        }

        public static int Count
        {
            get
            {
                return GetSetGuids(HiddenSetId).Count();
            }
        }

        public static bool SortEnabled
        {
            get
            {
                EnsureLoaded();
                return m_SortEnabled;
            }
            set
            {
                EnsureLoaded();
                if (m_SortEnabled == value)
                {
                    return;
                }
                m_SortEnabled = value;
                WriteToDisk();
            }
        }

        public static int LooseDisplayOrder
        {
            get
            {
                EnsureLoaded();
                return m_LooseDisplayOrder;
            }
            set
            {
                EnsureLoaded();
                if (m_LooseDisplayOrder == value)
                {
                    return;
                }
                m_LooseDisplayOrder = value;
                WriteToDisk();
            }
        }

        public static UntetheredPlacementMode UntetheredPlacement
        {
            get
            {
                EnsureLoaded();
                return m_UntetheredPlacement;
            }
            set
            {
                EnsureLoaded();
                if (m_UntetheredPlacement == value)
                {
                    return;
                }
                m_UntetheredPlacement = value;
                WriteToDisk();
            }
        }

        public static bool ConcealCustom
        {
            get
            {
                EnsureLoaded();
                return m_ConcealCustom;
            }
            set
            {
                EnsureLoaded();
                if (m_ConcealCustom == value)
                {
                    return;
                }
                m_ConcealCustom = value;
                WriteToDisk();
            }
        }

        public static DisplayMode ActiveDisplayMode
        {
            get
            {
                EnsureLoaded();
                return m_ActiveDisplayMode;
            }
            set
            {
                EnsureLoaded();
                if (m_ActiveDisplayMode == value)
                {
                    return;
                }
                m_ActiveDisplayMode = value;
                WriteToDisk();
            }
        }

        public static string ActiveEditSetId
        {
            get
            {
                EnsureLoaded();
                return m_ActiveEditSetId;
            }
            set
            {
                EnsureLoaded();
                string next = string.IsNullOrEmpty(value) ? DefaultEditPaletteId : value;
                if (m_ActiveEditSetId == next)
                {
                    return;
                }
                EnsureDefaultPalettes();
                if (!IsPaletteId(next))
                {
                    Debug.LogWarning(string.Format(
                        "HiddenBrushSet: invalid edit palette {0}; using {1}.",
                        next,
                        DefaultEditPaletteId));
                    next = DefaultEditPaletteId;
                }
                m_ActiveEditSetId = next;
                WriteToDisk();
            }
        }

        public static string ActiveDisplaySetId
        {
            get
            {
                EnsureLoaded();
                return m_ActiveDisplaySetId;
            }
            set
            {
                EnsureLoaded();
                string next = string.IsNullOrEmpty(value) ? AllDisplaySetId : value;
                if (string.Equals(m_ActiveDisplaySetId, next, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
                if (!string.Equals(next, AllDisplaySetId, StringComparison.OrdinalIgnoreCase) &&
                    !m_Sets.ContainsKey(next))
                {
                    Debug.LogWarning(string.Format(
                        "HiddenBrushSet: unknown display set {0}; falling back to All.",
                        next));
                    next = AllDisplaySetId;
                }
                m_ActiveDisplaySetId = next;
                WriteToDisk();
            }
        }

        public static bool IsDisplayingAllSets()
        {
            EnsureLoaded();
            return string.Equals(
                m_ActiveDisplaySetId,
                AllDisplaySetId,
                StringComparison.OrdinalIgnoreCase);
        }

        public static void SetDisplaySetToAll()
        {
            ActiveDisplaySetId = AllDisplaySetId;
        }

        public static void SetDisplaySetToHidden()
        {
            ActiveDisplaySetId = HiddenSetId;
        }

        public static void PopulateFromDisk()
        {
            m_Sets.Clear();
            m_SetMeta.Clear();
            m_ActiveDisplayMode = DisplayMode.Working;
            m_ActiveEditSetId = DefaultEditPaletteId;
            m_ActiveDisplaySetId = AllDisplaySetId;
            m_SortEnabled = false;
            m_LooseDisplayOrder = 0;
            m_UntetheredPlacement = UntetheredPlacementMode.Front;
            m_ConcealCustom = false;
            m_Loaded = true;

            EnsureDefaultPalettes();

            if (File.Exists(FilePath))
            {
                TryLoadBrushSetsFile(FilePath);
            }

            EnsureDefaultPalettes();
            ApplyConfigOverrides();

            if (string.IsNullOrEmpty(m_ActiveEditSetId) || !IsPaletteId(m_ActiveEditSetId))
            {
                m_ActiveEditSetId = DefaultEditPaletteId;
            }
            if (IsPermanentSlot(m_ActiveEditSetId))
            {
                m_ActiveEditSetId = DefaultEditPaletteId;
            }
        }

        // --- Stock helpers -------------------------------------------------

        public static bool IsStockBrush(BrushDescriptor brush)
        {
            if (brush == null || brush.m_Tags == null)
            {
                return false;
            }
            return brush.m_Tags.Contains(StockBasicTag) ||
                   brush.m_Tags.Contains(StockAdvancedTag);
        }

        public static bool IsStockBrush(Guid guid)
        {
            BrushDescriptor brush = BrushCatalog.m_Instance != null
                ? BrushCatalog.m_Instance.GetBrush(guid)
                : null;
            return IsStockBrush(brush);
        }

        public static bool IsStockBasic(BrushDescriptor brush)
        {
            return brush != null && brush.m_Tags != null && brush.m_Tags.Contains(StockBasicTag);
        }

        public static bool IsStockAdvanced(BrushDescriptor brush)
        {
            return brush != null && brush.m_Tags != null && brush.m_Tags.Contains(StockAdvancedTag);
        }

        public static bool IsPermanentSlotIndex(int index)
        {
            return index == PermanentSlotBasic || index == PermanentSlotAdvanced;
        }

        public static bool IsPermanentSlot(string paletteId)
        {
            return IsPermanentSlotIndex(ParsePaletteIndex(paletteId));
        }

        public static bool IsWritablePalette(string paletteId)
        {
            int index = ParsePaletteIndex(paletteId);
            return index >= FirstWritableSlot && index <= kMaxPaletteIndex;
        }

        /// Virtual home slot for stock with no custom copy: 1 basic, 2 advanced.
        public static string GetStockHomePaletteId(BrushDescriptor brush)
        {
            if (IsStockAdvanced(brush))
            {
                return FormatPaletteId(PermanentSlotAdvanced);
            }
            if (IsStockBasic(brush))
            {
                return FormatPaletteId(PermanentSlotBasic);
            }
            return null;
        }

        // --- Legacy thin wrappers ------------------------------------------

        public static bool IsHidden(Guid guid)
        {
            return IsInSet(HiddenSetId, guid);
        }

        public static void Toggle(Guid guid)
        {
            ToggleInActivePalette(guid);
        }

        public static void SetHidden(Guid guid, bool hidden)
        {
            if (IsPermanentSlot(HiddenSetId))
            {
                return;
            }
            if (hidden)
            {
                AssignToPalette(guid, HiddenSetId);
            }
            else if (IsInSet(HiddenSetId, guid))
            {
                AssignToPalette(guid, null);
            }
        }

        public static IEnumerable<Guid> GetAll()
        {
            return GetSetGuids(HiddenSetId);
        }

        public static void Clear()
        {
            EnsureLoaded();
            foreach (var kvp in m_Sets)
            {
                if (IsPermanentSlot(kvp.Key))
                {
                    continue;
                }
                kvp.Value.Clear();
            }
            WriteToDisk();
            PlayerPrefs.DeleteKey(kPlayerPrefKey);
        }

        public static bool IsInSet(string setId, Guid guid)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(setId) || !m_Sets.TryGetValue(setId, out HashSet<Guid> set))
            {
                return false;
            }
            return set.Contains(guid);
        }

        public static void ToggleInSet(string setId, Guid guid)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(setId))
            {
                setId = m_ActiveEditSetId;
            }
            if (!IsWritablePalette(setId))
            {
                return;
            }
            EnsureDefaultPalettes();

            if (IsInSet(setId, guid))
            {
                AssignToPalette(guid, null);
            }
            else
            {
                AssignToPalette(guid, setId);
            }
        }

        /// Selection-mode click against ActiveEditSetId.
        /// Permanent slots: no-op. Stock: toggle custom copy only.
        public static void ToggleInActivePalette(Guid guid)
        {
            EnsureLoaded();
            string active = m_ActiveEditSetId;
            if (string.IsNullOrEmpty(active) || !IsPaletteId(active))
            {
                active = DefaultEditPaletteId;
                m_ActiveEditSetId = active;
            }
            if (!IsWritablePalette(active))
            {
                return;
            }

            if (IsInSet(active, guid))
            {
                AssignToPalette(guid, null);
            }
            else
            {
                AssignToPalette(guid, active);
            }
        }

        /// Exclusive assign among writable (custom) slots only.
        /// paletteId null/empty => remove from all custom slots (untethered / stock home only).
        /// Never writes permanent slots 1-2.
        public static void AssignToPalette(Guid guid, string paletteId)
        {
            EnsureLoaded();
            EnsureDefaultPalettes();

            if (!string.IsNullOrEmpty(paletteId) && !IsWritablePalette(paletteId))
            {
                Debug.LogWarning(string.Format(
                    "HiddenBrushSet: AssignToPalette refused non-writable id {0}",
                    paletteId));
                return;
            }

            bool changed = false;

            List<string> keys = m_Sets.Keys.ToList();
            for (int i = 0; i < keys.Count; ++i)
            {
                string id = keys[i];
                if (!IsWritablePalette(id))
                {
                    continue;
                }
                if (m_Sets[id].Remove(guid))
                {
                    changed = true;
                }
            }

            if (!string.IsNullOrEmpty(paletteId))
            {
                if (m_Sets[paletteId].Add(guid))
                {
                    changed = true;
                }
            }

            if (changed)
            {
                WriteToDisk();
            }
        }

        /// Custom-slot membership only. Null if untethered or stock-without-copy.
        public static string GetPaletteIdForBrush(Guid guid)
        {
            EnsureLoaded();
            foreach (var kvp in m_Sets)
            {
                if (!IsWritablePalette(kvp.Key))
                {
                    continue;
                }
                if (kvp.Value.Contains(guid))
                {
                    return kvp.Key;
                }
            }
            return null;
        }

        /// Slot used for tint: custom copy if any, else stock home 1/2, else null.
        public static string GetTintPaletteIdForBrush(Guid guid)
        {
            string custom = GetPaletteIdForBrush(guid);
            if (!string.IsNullOrEmpty(custom))
            {
                return custom;
            }
            BrushDescriptor brush = BrushCatalog.m_Instance != null
                ? BrushCatalog.m_Instance.GetBrush(guid)
                : null;
            return GetStockHomePaletteId(brush);
        }

        public static string GetPaletteColor(string paletteId)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(paletteId))
            {
                return "";
            }
            if (m_SetMeta.TryGetValue(paletteId, out SetRecord meta) &&
                !string.IsNullOrEmpty(meta.color))
            {
                return meta.color;
            }
            return GetDefaultColorForId(paletteId);
        }

        public static string GetDisplayName(string paletteId)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(paletteId))
            {
                return "";
            }
            if (m_SetMeta.TryGetValue(paletteId, out SetRecord meta) &&
                !string.IsNullOrEmpty(meta.name))
            {
                return meta.name;
            }
            return paletteId;
        }

        public static int GetDisplayOrder(string paletteId)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(paletteId) || !IsPaletteId(paletteId))
            {
                return m_LooseDisplayOrder;
            }
            if (m_SetMeta.TryGetValue(paletteId, out SetRecord meta))
            {
                return meta.displayOrder;
            }
            return 0;
        }

        public static IEnumerable<Guid> GetSetGuids(string setId)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(setId) || !m_Sets.TryGetValue(setId, out HashSet<Guid> set))
            {
                return Array.Empty<Guid>();
            }
            return set;
        }

        public static bool IsPaletteId(string id)
        {
            int index = ParsePaletteIndex(id);
            return index >= 1 && index <= kMaxPaletteIndex;
        }

        public static int ParsePaletteIndex(string id)
        {
            if (string.IsNullOrEmpty(id) || !id.StartsWith(PaletteIdPrefix, StringComparison.Ordinal))
            {
                return -1;
            }
            string num = id.Substring(PaletteIdPrefix.Length);
            int index;
            if (!int.TryParse(num, out index))
            {
                return -1;
            }
            return index;
        }

        public static string FormatPaletteId(int index)
        {
            return string.Format("{0}{1:00}", PaletteIdPrefix, index);
        }

        static void EnsureLoaded()
        {
            if (!m_Loaded)
            {
                PopulateFromDisk();
            }
        }

        static string GetDefaultColorForIndex(int index)
        {
            if (index == 1 || index == 2)
            {
                return kWhiteHex;
            }
            if (index >= 3 && index <= kMaxPaletteIndex)
            {
                return kNeonCycle[(index - 3) % kNeonCycle.Length];
            }
            return "";
        }

        static string GetDefaultColorForId(string paletteId)
        {
            int index = ParsePaletteIndex(paletteId);
            if (index < 1)
            {
                return "";
            }
            return GetDefaultColorForIndex(index);
        }

        static void EnsureDefaultPalettes()
        {
            for (int i = 1; i <= kSeedPaletteCount; ++i)
            {
                string id = FormatPaletteId(i);
                string hex = GetDefaultColorForIndex(i);
                EnsureSetExists(id, id, "user", hex, 0);
                if (string.IsNullOrEmpty(m_SetMeta[id].color))
                {
                    m_SetMeta[id].color = hex;
                }
            }
        }

        static void EnsureSetExists(string id, string name, string kind, string color, int displayOrder)
        {
            if (!m_Sets.ContainsKey(id))
            {
                m_Sets[id] = new HashSet<Guid>();
            }
            if (!m_SetMeta.ContainsKey(id))
            {
                m_SetMeta[id] = new SetRecord
                {
                    id = id,
                    name = name,
                    kind = kind,
                    color = color,
                    displayOrder = displayOrder,
                    guids = new List<string>()
                };
            }
        }

        static void ApplyConfigOverrides()
        {
            BrushPaletteConfig config = null;
            try
            {
                config = Resources.Load<BrushPaletteConfig>(kConfigResourceName);
            }
            catch
            {
                config = null;
            }
            if (config == null || config.palettes == null)
            {
                return;
            }

            for (int i = 0; i < config.palettes.Count; ++i)
            {
                BrushPaletteConfig.Entry entry = config.palettes[i];
                if (entry == null || string.IsNullOrEmpty(entry.id) || !IsPaletteId(entry.id))
                {
                    continue;
                }
                EnsureDefaultPalettes();
                if (!m_SetMeta.ContainsKey(entry.id))
                {
                    EnsureSetExists(entry.id, entry.id, "user", GetDefaultColorForId(entry.id), 0);
                }

                if (!string.IsNullOrEmpty(entry.displayName))
                {
                    m_SetMeta[entry.id].name = entry.displayName;
                }
                m_SetMeta[entry.id].displayOrder = entry.displayOrder;
                if (entry.overrideColor)
                {
                    m_SetMeta[entry.id].color = BrushPaletteConfig.ColorToHex(entry.colorPicker);
                }
            }
        }

        static bool TryLoadBrushSetsFile(string path)
        {
            try
            {
                string text = File.ReadAllText(path, System.Text.Encoding.UTF8);
                var data = JsonConvert.DeserializeObject<DiskFormat>(text);
                if (data == null)
                {
                    return false;
                }

                if (!string.IsNullOrEmpty(data.activeEditSetId) &&
                    !IsPermanentSlot(data.activeEditSetId))
                {
                    m_ActiveEditSetId = data.activeEditSetId;
                }

                m_SortEnabled = data.sortEnabled;
                m_LooseDisplayOrder = data.looseDisplayOrder;
                m_ConcealCustom = data.concealCustom;

                if (string.Equals(data.untetheredPlacement, "Back", StringComparison.OrdinalIgnoreCase))
                {
                    m_UntetheredPlacement = UntetheredPlacementMode.Back;
                }
                else
                {
                    m_UntetheredPlacement = UntetheredPlacementMode.Front;
                }

                if (string.Equals(data.activeDisplayMode, "All", StringComparison.OrdinalIgnoreCase))
                {
                    m_ActiveDisplayMode = DisplayMode.All;
                }
                else
                {
                    m_ActiveDisplayMode = DisplayMode.Working;
                }

                if (!string.IsNullOrEmpty(data.activeDisplaySetId))
                {
                    m_ActiveDisplaySetId = data.activeDisplaySetId;
                }
                else
                {
                    m_ActiveDisplaySetId = AllDisplaySetId;
                }

                if (data.sets != null)
                {
                    foreach (SetRecord rec in data.sets)
                    {
                        if (rec == null || string.IsNullOrEmpty(rec.id))
                        {
                            continue;
                        }
                        if (!IsPaletteId(rec.id))
                        {
                            continue;
                        }
                        if (IsPermanentSlot(rec.id))
                        {
                            EnsureSetExists(rec.id, rec.name ?? rec.id, rec.kind ?? "user",
                                rec.color ?? "", rec.displayOrder);
                            if (!string.IsNullOrEmpty(rec.name))
                            {
                                m_SetMeta[rec.id].name = rec.name;
                            }
                            if (!string.IsNullOrEmpty(rec.color))
                            {
                                m_SetMeta[rec.id].color = rec.color;
                            }
                            m_SetMeta[rec.id].displayOrder = rec.displayOrder;
                            continue;
                        }

                        EnsureSetExists(
                            rec.id,
                            rec.name ?? rec.id,
                            rec.kind ?? "user",
                            rec.color ?? "",
                            rec.displayOrder);
                        m_SetMeta[rec.id].name = string.IsNullOrEmpty(rec.name) ? rec.id : rec.name;
                        m_SetMeta[rec.id].kind = string.IsNullOrEmpty(rec.kind) ? "user" : rec.kind;
                        m_SetMeta[rec.id].displayOrder = rec.displayOrder;
                        if (!string.IsNullOrEmpty(rec.color))
                        {
                            m_SetMeta[rec.id].color = rec.color;
                        }
                        if (rec.guids == null)
                        {
                            continue;
                        }
                        foreach (string s in rec.guids)
                        {
                            if (Guid.TryParse(s, out Guid guid))
                            {
                                m_Sets[rec.id].Add(guid);
                            }
                        }
                    }
                }

                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning(string.Format(
                    "HiddenBrushSet: failed to read {0}: {1}",
                    path,
                    e.Message));
                return false;
            }
        }

        static void WriteToDisk()
        {
            EnsureDefaultPalettes();

            var data = new DiskFormat
            {
                version = kCurrentVersion,
                activeDisplayMode = m_ActiveDisplayMode == DisplayMode.All ? "All" : "Working",
                activeEditSetId = m_ActiveEditSetId,
                activeDisplaySetId = m_ActiveDisplaySetId,
                sortEnabled = m_SortEnabled,
                looseDisplayOrder = m_LooseDisplayOrder,
                untetheredPlacement = m_UntetheredPlacement == UntetheredPlacementMode.Back
                    ? "Back"
                    : "Front",
                concealCustom = m_ConcealCustom,
                sets = new List<SetRecord>()
            };

            foreach (var kvp in m_Sets.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                if (!IsPaletteId(kvp.Key))
                {
                    continue;
                }

                SetRecord meta;
                if (!m_SetMeta.TryGetValue(kvp.Key, out meta))
                {
                    meta = new SetRecord
                    {
                        id = kvp.Key,
                        name = kvp.Key,
                        kind = "user",
                        color = "",
                        displayOrder = 0
                    };
                }

                List<string> guidList;
                if (IsPermanentSlot(kvp.Key))
                {
                    guidList = new List<string>();
                }
                else
                {
                    guidList = kvp.Value
                        .Select(g => g.ToString())
                        .OrderBy(s => s, StringComparer.Ordinal)
                        .ToList();
                }

                var rec = new SetRecord
                {
                    id = kvp.Key,
                    name = meta.name,
                    color = meta.color,
                    kind = meta.kind,
                    displayOrder = meta.displayOrder,
                    guids = guidList
                };
                data.sets.Add(rec);
            }

            string path = FilePath;
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                string json = JsonConvert.SerializeObject(data, Formatting.Indented);
                File.WriteAllText(path, json, System.Text.Encoding.UTF8);
            }
            catch (Exception e)
            {
                Debug.LogWarning(string.Format(
                    "HiddenBrushSet: failed to write {0}: {1}",
                    path,
                    e.Message));
            }

            try
            {
                IEnumerable<Guid> hidden = GetSetGuids(HiddenSetId);
                string joined = string.Join("|", hidden.Select(g => g.ToString()));
                PlayerPrefs.SetString(kPlayerPrefKey, joined);
                PlayerPrefs.Save();
            }
            catch
            {
            }
        }

        public static bool TryParseHexColor(string hex, out Color color)
        {
            color = Color.white;
            if (string.IsNullOrEmpty(hex))
            {
                return false;
            }

            string s = hex.Trim();
            if (s.StartsWith("#"))
            {
                s = s.Substring(1);
            }

            if (s.Length != 6)
            {
                return false;
            }

            byte r;
            byte g;
            byte b;
            if (!byte.TryParse(s.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out r) ||
                !byte.TryParse(s.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out g) ||
                !byte.TryParse(s.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out b))
            {
                return false;
            }

            color = new Color(r / 255f, g / 255f, b / 255f, 1f);
            return true;
        }
    }
}
