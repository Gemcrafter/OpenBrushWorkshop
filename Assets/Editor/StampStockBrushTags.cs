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

// One-shot editor utility: stamp stock_basic / stock_advanced on BrushDescriptors
// from Resources folder layout. Does not clear other tags.
//
// Menu: Open Brush / Brushes / Stamp Stock Basic-Advanced Tags
//
// Basic:    Assets/Resources/Brushes/Basic  (Resources path Brushes/Basic)
// Advanced: Assets/Resources/X/Brushes      (Resources path X/Brushes)

using System;
using UnityEditor;
using UnityEngine;

namespace TiltBrush
{
    public static class StampStockBrushTags
    {
        public const string StockBasicTag = "stock_basic";
        public const string StockAdvancedTag = "stock_advanced";

        const string kBasicPathMarker = "/Brushes/Basic/";
        const string kAdvancedPathMarker = "/X/Brushes/";

        [MenuItem("Open Brush/Brushes/Stamp Stock Basic-Advanced Tags")]
        public static void Stamp()
        {
            string[] guids = AssetDatabase.FindAssets("t:BrushDescriptor");
            int basicCount = 0;
            int advancedCount = 0;
            int skippedCount = 0;
            int alreadyBasic = 0;
            int alreadyAdvanced = 0;

            for (int i = 0; i < guids.Length; ++i)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(path))
                {
                    skippedCount++;
                    continue;
                }

                BrushDescriptor brush = AssetDatabase.LoadAssetAtPath<BrushDescriptor>(path);
                if (brush == null)
                {
                    skippedCount++;
                    continue;
                }

                // Normalize for simple contains checks
                string normalized = path.Replace('\\', '/');

                bool isBasic = normalized.IndexOf(kBasicPathMarker, StringComparison.OrdinalIgnoreCase) >= 0;
                bool isAdvanced = normalized.IndexOf(kAdvancedPathMarker, StringComparison.OrdinalIgnoreCase) >= 0;

                if (!isBasic && !isAdvanced)
                {
                    skippedCount++;
                    continue;
                }

                if (brush.m_Tags == null)
                {
                    brush.m_Tags = new System.Collections.Generic.List<string>();
                }

                bool dirty = false;

                if (isBasic)
                {
                    if (!brush.m_Tags.Contains(StockBasicTag))
                    {
                        brush.m_Tags.Add(StockBasicTag);
                        dirty = true;
                        basicCount++;
                    }
                    else
                    {
                        alreadyBasic++;
                    }
                }

                if (isAdvanced)
                {
                    if (!brush.m_Tags.Contains(StockAdvancedTag))
                    {
                        brush.m_Tags.Add(StockAdvancedTag);
                        dirty = true;
                        advancedCount++;
                    }
                    else
                    {
                        alreadyAdvanced++;
                    }
                }

                if (dirty)
                {
                    EditorUtility.SetDirty(brush);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(string.Format(
                "StampStockBrushTags: added {0} stock_basic, {1} stock_advanced. " +
                "Already had tag: basic {2}, advanced {3}. Skipped (not in Basic/X paths): {4}.",
                basicCount,
                advancedCount,
                alreadyBasic,
                alreadyAdvanced,
                skippedCount));
        }
    }
}
