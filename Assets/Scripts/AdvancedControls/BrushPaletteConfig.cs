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
using UnityEngine;

namespace TiltBrush
{
    /// Editable palette labels, display order, and optional color overrides.
    /// Create via Assets > Create > Open Brush > Brush Palette Config.
    /// Place the asset in a Resources folder as BrushPaletteConfig
    /// so Resources.Load can find it.
    ///
    /// Use the Color field + overrideColor checkbox to pick tints in the
    /// Inspector (same idea as Mirror panel color fields). Hex is derived
    /// at merge time when overrideColor is true.
    [CreateAssetMenu(
        fileName = "BrushPaletteConfig",
        menuName = "Open Brush/Brush Palette Config")]
    public class BrushPaletteConfig : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            [Tooltip("Must match a real palette id, e.g. Palette_01 or Palette_09")]
            public string id = "";

            [Tooltip("Label shown on the Curation panel. Leave blank to keep existing name.")]
            public string displayName = "";

            [Tooltip("Lower value appears earlier after catalog reload when Sort is on.")]
            public int displayOrder = 0;

            [Tooltip("When enabled, colorPicker is applied as the palette tint.")]
            public bool overrideColor = false;

            [Tooltip("Inspector color picker. Only used when overrideColor is true.")]
            public Color colorPicker = Color.white;

            [Tooltip("One icon for this Palette_NN. Fills badge + button Texture / On / Off.")]
            public Texture2D slotIcon;
        }

        public List<Entry> palettes = new List<Entry>();

        [Header("Brush cell set badge")]
        [Tooltip("1 = default corner size. 1.2 = slightly larger. Saved on this asset, not on a play-mode clone.")]
        public float setBadgeScale = 1f;

        [Header("Brush cell icons")]
        [Tooltip("If off, BrushTypeButton uses raw m_ButtonTexture. Other panels stay on the shared atlas.")]
        public bool brushCellUseAtlas = true;

        [Tooltip("Importer max size for assigner menus. Use 128, 256, 512, or 1024.")]
        public int brushCellIconSize = 128;

        [Tooltip("Multiplies the whole photo after plate key. Leave white.")]
        public Color brushCellPhotoColor = Color.white;

        [Tooltip("Replaces near-black pixels on the cell. Does not scale the stroke.")]
        public Color brushCellPlateColor = Color.black;

        [Tooltip("Scales only non-plate pixels. 0.75 matches old PanelButton dim.")]
        [Range(0.1f, 1.5f)]
        public float brushCellStrokeGain = 0.75f;

        [Tooltip("Luminance below this becomes plate. Raise if gray still shows in the field.")]
        [Range(0.01f, 0.4f)]
        public float brushCellPlateThreshold = 0.08f;

        public static BrushPaletteConfig LoadAsset()
        {
            return Resources.Load<BrushPaletteConfig>("ScriptableObjects/BrushPaletteConfig");
        }

        public static Texture2D GetSlotIcon(string paletteId)
        {
            if (string.IsNullOrEmpty(paletteId))
            {
                return null;
            }
            BrushPaletteConfig config = LoadAsset();
            if (config == null || config.palettes == null)
            {
                return null;
            }
            for (int i = 0; i < config.palettes.Count; ++i)
            {
                Entry entry = config.palettes[i];
                if (entry == null || entry.slotIcon == null)
                {
                    continue;
                }
                if (string.Equals(entry.id, paletteId, StringComparison.OrdinalIgnoreCase))
                {
                    return entry.slotIcon;
                }
            }
            return null;
        }

        public static bool GetBrushCellUseAtlas()
        {
            BrushPaletteConfig config = LoadAsset();
            if (config == null)
            {
                return true;
            }
            return config.brushCellUseAtlas;
        }

        public static int GetBrushCellIconSize()
        {
            BrushPaletteConfig config = LoadAsset();
            if (config == null || config.brushCellIconSize < 32)
            {
                return 128;
            }
            return config.brushCellIconSize;
        }

        public static Color GetBrushCellPhotoColor()
        {
            BrushPaletteConfig config = LoadAsset();
            if (config == null)
            {
                return Color.white;
            }
            return config.brushCellPhotoColor;
        }

        public static Color GetBrushCellPlateColor()
        {
            BrushPaletteConfig config = LoadAsset();
            if (config == null)
            {
                return Color.black;
            }
            return config.brushCellPlateColor;
        }

        public static float GetBrushCellStrokeGain()
        {
            BrushPaletteConfig config = LoadAsset();
            if (config == null)
            {
                return 0.75f;
            }
            return config.brushCellStrokeGain;
        }

        public static float GetBrushCellPlateThreshold()
        {
            BrushPaletteConfig config = LoadAsset();
            if (config == null)
            {
                return 0.08f;
            }
            return Mathf.Max(0.01f, config.brushCellPlateThreshold);
        }

        public static string ColorToHex(Color c)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255);
            int g = Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255);
            int b = Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255);
            return string.Format("#{0:X2}{1:X2}{2:X2}", r, g, b);
        }
    }
}



/*
using System;
using System.Collections.Generic;
using UnityEngine;

namespace TiltBrush
{
    /// Editable palette labels, display order, and optional color overrides.
    /// Create via Assets > Create > Open Brush > Brush Palette Config.
    /// Place the asset in a Resources folder as BrushPaletteConfig
    /// so Resources.Load can find it.
    ///
    /// Use the Color field + overrideColor checkbox to pick tints in the
    /// Inspector (same idea as Mirror panel color fields). Hex is derived
    /// at merge time when overrideColor is true.
    [CreateAssetMenu(
        fileName = "BrushPaletteConfig",
        menuName = "Open Brush/Brush Palette Config")]
    public class BrushPaletteConfig : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            [Tooltip("Must match a real palette id, e.g. Palette_01 or Palette_09")]
            public string id = "";

            [Tooltip("Label shown on the Curation panel. Leave blank to keep existing name.")]
            public string displayName = "";

            [Tooltip("Lower value appears earlier after catalog reload when Sort is on.")]
            public int displayOrder = 0;

            [Tooltip("When enabled, colorPicker is applied as the palette tint.")]
            public bool overrideColor = false;

            [Tooltip("Inspector color picker. Only used when overrideColor is true.")]
            public Color colorPicker = Color.white;

            [Tooltip("One icon for this Palette_NN. Fills badge + button Texture / On / Off.")]
            public Texture2D slotIcon;
        }

        public List<Entry> palettes = new List<Entry>();

        [Header("Brush cell set badge")]
        [Tooltip("1 = default corner size. 1.2 = slightly larger. Saved on this asset, not on a play-mode clone.")]
        public float setBadgeScale = 1f;
        [Header("Brush cell icons")]
        [Tooltip("If off, BrushTypeButton uses raw m_ButtonTexture. Other panels stay on the shared atlas.")]
        public bool brushCellUseAtlas = true;

        [Tooltip("Importer max size for assigner menus. Use 128, 256, 512, or 1024.")]
        public int brushCellIconSize = 128;

        /// Convert a Color to #RRGGBB (no alpha) for HiddenBrushSet meta.
        public static int GetBrushCellIconSize()
        {
            BrushPaletteConfig config = LoadAsset();
            if (config == null || config.brushCellIconSize < 32)
            {
                return 128;
            }
            return config.brushCellIconSize;
        }

        public static bool GetBrushCellUseAtlas()
        {
            BrushPaletteConfig config = LoadAsset();
            if (config == null)
            {
                return true;
            }
            return config.brushCellUseAtlas;
        }



        public static BrushPaletteConfig LoadAsset()
        {
            return Resources.Load<BrushPaletteConfig>("ScriptableObjects/BrushPaletteConfig");
        }

        public static Texture2D GetSlotIcon(string paletteId)
        {
            if (string.IsNullOrEmpty(paletteId))
            {
                return null;
            }
            BrushPaletteConfig config = LoadAsset();
            if (config == null || config.palettes == null)
            {
                return null;
            }
            for (int i = 0; i < config.palettes.Count; ++i)
            {
                Entry entry = config.palettes[i];
                if (entry == null || entry.slotIcon == null)
                {
                    continue;
                }
                if (string.Equals(entry.id, paletteId, StringComparison.OrdinalIgnoreCase))
                {
                    return entry.slotIcon;
                }
            }
            return null;
        }

        public static string ColorToHex(Color c)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255);
            int g = Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255);
            int b = Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255);
            return string.Format("#{0:X2}{1:X2}{2:X2}", r, g, b);
        }
    }
}
*/