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
using System.Text;
using UnityEngine;

namespace TiltBrush
{
    [CreateAssetMenu(
        fileName = "BrushIconShotConfig",
        menuName = "Open Brush/Brush Icon Shot Config")]
    public class BrushIconShotConfig : ScriptableObject
    {
        public const string DefaultEnvironmentGuid = "580b4529-ac50-4fe9-b8d2-635765a25888";
        public const string DefaultEnvironmentName = "VideoBlack";

        [Serializable]
        public class Swatch
        {
            public string commonName = "Blue";
            public Color color = new Color(0.118f, 0.533f, 0.898f, 1f);
        }

        [Header("Environment")]
        [Tooltip("Guid of a catalog Environment asset. Inspector lists assets found in the project.")]
        public string environmentGuid = DefaultEnvironmentGuid;
        [Tooltip("Folder token. Usually the Environment asset name.")]
        public string environmentName = DefaultEnvironmentName;
        [Tooltip("Off = black camera clear, env lights only. On = sky / env backdrop.")]
        public bool useEnvironmentPlate = false;

        [Header("Capture")]
        public int captureSize = 4096;

        [Header("Stroke color")]
        public string shotColorName = "Blue";
        public Color shotColor = new Color(0.118f, 0.533f, 0.898f, 1f);
        public Swatch[] presets = new Swatch[0];

        public Swatch GetActiveSwatch()
        {
            Swatch swatch = new Swatch();
            swatch.commonName = string.IsNullOrEmpty(shotColorName) ? "Color" : shotColorName;
            swatch.color = shotColor;
            return swatch;
        }

        public string GetEnvironmentGuid()
        {
            if (string.IsNullOrEmpty(environmentGuid))
            {
                return DefaultEnvironmentGuid;
            }
            return environmentGuid;
        }

        public string GetEnvironmentName()
        {
            if (string.IsNullOrEmpty(environmentName))
            {
                return DefaultEnvironmentName;
            }
            return SanitizeFolderToken(environmentName);
        }

        public int GetCaptureSize()
        {
            if (captureSize < 128)
            {
                return 128;
            }
            return captureSize;
        }

        public string BuildOutputFolderName(Swatch swatch)
        {
            if (swatch == null)
            {
                swatch = DefaultBlue();
            }
            string name = GetEnvironmentName() + "_" + GetCaptureSize() + "_" + FolderToken(swatch);
            if (!useEnvironmentPlate)
            {
                name = name + "_BlackPlate";
            }
            return name;
        }

        public static Swatch DefaultBlue()
        {
            Swatch swatch = new Swatch();
            swatch.commonName = "Blue";
            swatch.color = new Color(0.118f, 0.533f, 0.898f, 1f);
            return swatch;
        }

        public static string HexRgb(Color color)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt(color.r * 255f), 0, 255);
            int g = Mathf.Clamp(Mathf.RoundToInt(color.g * 255f), 0, 255);
            int b = Mathf.Clamp(Mathf.RoundToInt(color.b * 255f), 0, 255);
            return r.ToString("X2") + g.ToString("X2") + b.ToString("X2");
        }

        public static string SanitizeFolderToken(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "Env";
            }
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < name.Length; ++i)
            {
                char c = name[i];
                if ((c >= 'A' && c <= 'Z') ||
                    (c >= 'a' && c <= 'z') ||
                    (c >= '0' && c <= '9') ||
                    c == '-' || c == '_')
                {
                    sb.Append(c);
                }
            }
            if (sb.Length == 0)
            {
                return "Env";
            }
            return sb.ToString();
        }

        public static string FolderToken(Swatch swatch)
        {
            if (swatch == null)
            {
                swatch = DefaultBlue();
            }
            return SanitizeFolderToken(swatch.commonName) + "-" + HexRgb(swatch.color);
        }

        public void EnsureDefaultPresets()
        {
            if (presets != null && presets.Length > 0)
            {
                return;
            }
            presets = DefaultPresetList();
        }

        public void AddCurrentColorAsPreset()
        {
            Swatch add = GetActiveSwatch();
            List<Swatch> list = new List<Swatch>();
            if (presets != null)
            {
                for (int i = 0; i < presets.Length; ++i)
                {
                    if (presets[i] != null)
                    {
                        list.Add(presets[i]);
                    }
                }
            }
            for (int i = 0; i < list.Count; ++i)
            {
                if (string.Equals(list[i].commonName, add.commonName, StringComparison.OrdinalIgnoreCase))
                {
                    list[i].color = add.color;
                    presets = list.ToArray();
                    return;
                }
            }
            list.Add(add);
            presets = list.ToArray();
        }

        public static Swatch[] DefaultPresetList()
        {
            return new Swatch[]
            {
                new Swatch { commonName = "Blue", color = new Color(0.118f, 0.533f, 0.898f, 1f) },
                new Swatch { commonName = "White", color = Color.white },
                new Swatch { commonName = "Red", color = new Color(0.898f, 0.224f, 0.208f, 1f) },
                new Swatch { commonName = "Green", color = new Color(0.263f, 0.627f, 0.278f, 1f) },
                new Swatch { commonName = "Yellow", color = new Color(0.992f, 0.847f, 0.208f, 1f) },
                new Swatch { commonName = "Magenta", color = new Color(0.671f, 0.278f, 0.737f, 1f) }
            };
        }

        void Reset()
        {
            environmentGuid = DefaultEnvironmentGuid;
            environmentName = DefaultEnvironmentName;
            useEnvironmentPlate = false;
            captureSize = 4096;
            shotColorName = "Blue";
            shotColor = new Color(0.118f, 0.533f, 0.898f, 1f);
            presets = DefaultPresetList();
        }
    } // functions complete
} // Namespace Tilt Brush
