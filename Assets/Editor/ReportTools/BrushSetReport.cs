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
using System.Text;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

namespace TiltBrush
{
    public class BrushSetReport : Editor
    {
        const string kOutputRelative = "Support/Reports/BrushSetReport.txt";

        static readonly string[] kShaderFloatNames =
        {
            "_Shininess",
            "_Glossiness",
            "_Gloss",
            "_Metallic",
            "_Metal",
            "_SpecIntensity",
            "_Specular",
            "_Cutoff",
            "_Opacity",
            "_InvAlpha",
            "_EmissionGain",
            "_RimPower",
            "_TimeBlend"
        };

        static readonly string[] kShaderColorNames =
        {
            "_Color",
            "_SpecColor",
            "_EmissionColor",
            "_Emissive",
            "_RimColor",
            "_TintColor"
        };

        class DiskSet
        {
            public string id = "";
            public string name = "";
            public List<string> guids = new List<string>();
        }

        class DiskFormat
        {
            public List<DiskSet> sets = new List<DiskSet>();
        }

        class BrushRow
        {
            public string GuidString = "";
            public string DisplayName = "";
            public string DurableName = "";
            public string AssetPath = "";
            public string PrefabScript = "";
            public string ShaderName = "";
            public string Tags = "";
            public bool AudioReactive;
            public bool HasPrefab;
            public bool Nondeterministic;
            public string BlendMode = "";
            public float Opacity;
            public float EmissiveFactor;
            public float ColorLuminanceMin;
            public float ParticleRate;
            public bool RenderBackfaces;
            public bool BackIsInvisible;
            public bool UseBloomSwatch;
            public bool HasTimeBlend;
            public bool HasTimeOverride;
            public string ShaderFloats = "";
            public string ShaderColors = "";
            public string ButtonTexture = "";
            public int ButtonTextureWidth;
            public int ButtonTextureHeight;
            public string IconLuma = "";
            public bool MissingDescriptor;
        }

        [MenuItem("Advanced/Reports/Write Brush Set Report")]
        static void WriteReport()
        {
            string jsonPath = GetBrushSetsJsonPath();
            if (string.IsNullOrEmpty(jsonPath) || !File.Exists(jsonPath))
            {
                Debug.LogError("[BrushSetReport] File not found: " + jsonPath);
                return;
            }

            EnsureLocalizationReady();

            DiskFormat data = JsonConvert.DeserializeObject<DiskFormat>(File.ReadAllText(jsonPath));
            if (data == null || data.sets == null)
            {
                Debug.LogError("[BrushSetReport] Could not parse " + jsonPath);
                return;
            }

            Dictionary<string, BrushDescriptor> byGuid = BuildGuidLookup();
            StringBuilder human = new StringBuilder();
            StringBuilder machine = new StringBuilder();

            human.AppendLine("Brush Set Report");
            human.AppendLine("JSON: " + jsonPath);
            human.AppendLine("Written: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            human.AppendLine();

            machine.AppendLine("MACHINE");
            machine.AppendLine(
                "guid\tdurableName\tdisplayName\tprefabScript\tshader\ttags\t" +
                "audioReactive\thasPrefab\tnondeterministic\tblendMode\t" +
                "opacity\temissiveFactor\tcolorLuminanceMin\tparticleRate\t" +
                "renderBackfaces\tbackIsInvisible\tuseBloomSwatch\t" +
                "hasTimeBlend\thasTimeOverride\tshaderFloats\tshaderColors\t" +
                "buttonTexture\ttexW\ttexH\ticonLuma\tassetPath\tmissingDescriptor");

            for (int i = 0; i < data.sets.Count; ++i)
            {
                DiskSet set = data.sets[i];
                if (set == null || string.IsNullOrEmpty(set.id))
                {
                    continue;
                }

                string setName = string.IsNullOrEmpty(set.name) ? set.id : set.name;
                int count = set.guids == null ? 0 : set.guids.Count;

                human.AppendLine(set.id + "  " + setName);
                if (count == 0)
                {
                    human.AppendLine("(empty)");
                    human.AppendLine();
                    continue;
                }

                for (int g = 0; g < set.guids.Count; ++g)
                {
                    BrushRow row = ResolveRow(set.guids[g], byGuid);
                    human.AppendLine(row.DisplayName);
                    human.AppendLine(row.DurableName);
                    human.AppendLine(row.GuidString);
                    human.AppendLine();
                    AppendMachineLine(machine, row);
                }
            }

            human.AppendLine();
            human.Append(machine.ToString());

            string outPath = Path.Combine(Directory.GetCurrentDirectory(), kOutputRelative);
            string outDir = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(outDir))
            {
                Directory.CreateDirectory(outDir);
            }
            File.WriteAllText(outPath, human.ToString());
            Debug.Log("[BrushSetReport] Wrote " + outPath);
        }

        static string GetBrushSetsJsonPath()
        {
            if (App.Instance != null)
            {
                return Path.Combine(App.UserPath(), "BrushSets.json");
            }
            return Path.Combine(App.DocumentsPath(), App.kAppFolderName, "BrushSets.json");
        }

        static void AppendMachineLine(StringBuilder machine, BrushRow row)
        {
            machine.Append(row.GuidString);
            machine.Append('\t');
            machine.Append(row.DurableName);
            machine.Append('\t');
            machine.Append(row.DisplayName);
            machine.Append('\t');
            machine.Append(row.PrefabScript);
            machine.Append('\t');
            machine.Append(row.ShaderName);
            machine.Append('\t');
            machine.Append(row.Tags);
            machine.Append('\t');
            machine.Append(row.AudioReactive ? "1" : "0");
            machine.Append('\t');
            machine.Append(row.HasPrefab ? "1" : "0");
            machine.Append('\t');
            machine.Append(row.Nondeterministic ? "1" : "0");
            machine.Append('\t');
            machine.Append(row.BlendMode);
            machine.Append('\t');
            machine.Append(row.Opacity.ToString("0.###"));
            machine.Append('\t');
            machine.Append(row.EmissiveFactor.ToString("0.###"));
            machine.Append('\t');
            machine.Append(row.ColorLuminanceMin.ToString("0.###"));
            machine.Append('\t');
            machine.Append(row.ParticleRate.ToString("0.###"));
            machine.Append('\t');
            machine.Append(row.RenderBackfaces ? "1" : "0");
            machine.Append('\t');
            machine.Append(row.BackIsInvisible ? "1" : "0");
            machine.Append('\t');
            machine.Append(row.UseBloomSwatch ? "1" : "0");
            machine.Append('\t');
            machine.Append(row.HasTimeBlend ? "1" : "0");
            machine.Append('\t');
            machine.Append(row.HasTimeOverride ? "1" : "0");
            machine.Append('\t');
            machine.Append(row.ShaderFloats);
            machine.Append('\t');
            machine.Append(row.ShaderColors);
            machine.Append('\t');
            machine.Append(row.ButtonTexture);
            machine.Append('\t');
            machine.Append(row.ButtonTextureWidth);
            machine.Append('\t');
            machine.Append(row.ButtonTextureHeight);
            machine.Append('\t');
            machine.Append(row.IconLuma);
            machine.Append('\t');
            machine.Append(row.AssetPath);
            machine.Append('\t');
            machine.Append(row.MissingDescriptor ? "1" : "0");
            machine.AppendLine();
        }

        static void EnsureLocalizationReady()
        {
            try
            {
                var init = LocalizationSettings.InitializationOperation;
                if (!init.IsDone)
                {
                    init.WaitForCompletion();
                }
            }
            catch
            {
            }
        }

        static string GetVisibleBrushName(BrushDescriptor descriptor)
        {
            if (descriptor == null)
            {
                return "";
            }

            try
            {
                LocalizedString loc = descriptor.m_LocalizedDescription;
                if (loc != null && !loc.IsEmpty)
                {
                    string localized = loc.GetLocalizedString();
                    if (!string.IsNullOrEmpty(localized))
                    {
                        return localized;
                    }
                }
            }
            catch
            {
            }

            if (!string.IsNullOrEmpty(descriptor.Description))
            {
                return descriptor.Description;
            }
            if (!string.IsNullOrEmpty(descriptor.DurableName))
            {
                return descriptor.DurableName;
            }
            return descriptor.name;
        }

        static Dictionary<string, BrushDescriptor> BuildGuidLookup()
        {
            Dictionary<string, BrushDescriptor> result =
                new Dictionary<string, BrushDescriptor>(StringComparer.OrdinalIgnoreCase);
            string[] guids = AssetDatabase.FindAssets("t:BrushDescriptor");
            for (int i = 0; i < guids.Length; ++i)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                BrushDescriptor descriptor = AssetDatabase.LoadAssetAtPath<BrushDescriptor>(path);
                if (descriptor == null)
                {
                    continue;
                }
                string key = descriptor.m_Guid.ToString();
                if (string.IsNullOrEmpty(key) || result.ContainsKey(key))
                {
                    continue;
                }
                result[key] = descriptor;
            }
            return result;
        }

        static BrushRow ResolveRow(string guidString, Dictionary<string, BrushDescriptor> byGuid)
        {
            BrushRow row = new BrushRow();
            row.GuidString = guidString ?? "";
            BrushDescriptor descriptor;
            if (string.IsNullOrEmpty(guidString) || !byGuid.TryGetValue(guidString, out descriptor))
            {
                row.DisplayName = "(missing descriptor)";
                row.DurableName = "";
                row.MissingDescriptor = true;
                return row;
            }

            row.DisplayName = GetVisibleBrushName(descriptor);
            row.DurableName = descriptor.DurableName;
            row.AssetPath = AssetDatabase.GetAssetPath(descriptor);
            row.AudioReactive = descriptor.m_AudioReactive;
            row.HasPrefab = descriptor.m_BrushPrefab != null;
            row.Nondeterministic = descriptor.m_Nondeterministic;
            row.BlendMode = descriptor.m_BlendMode.ToString();
            row.Opacity = descriptor.m_Opacity;
            row.EmissiveFactor = descriptor.m_EmissiveFactor;
            row.ColorLuminanceMin = descriptor.m_ColorLuminanceMin;
            row.ParticleRate = descriptor.m_ParticleRate;
            row.RenderBackfaces = descriptor.m_RenderBackfaces;
            row.BackIsInvisible = descriptor.m_BackIsInvisible;
            row.UseBloomSwatch = descriptor.m_UseBloomSwatchOnColorPicker;

            if (descriptor.m_Tags != null && descriptor.m_Tags.Count > 0)
            {
                row.Tags = string.Join(",", descriptor.m_Tags.ToArray());
            }

            if (descriptor.m_BrushPrefab != null)
            {
                BaseBrushScript brushScript = descriptor.m_BrushPrefab.GetComponent<BaseBrushScript>();
                if (brushScript != null)
                {
                    row.PrefabScript = brushScript.GetType().Name;
                }
            }

            Material mat = descriptor.Material;
            if (mat != null && mat.shader != null)
            {
                row.ShaderName = mat.shader.name;
                row.HasTimeBlend = mat.HasProperty("_TimeBlend");
                row.HasTimeOverride = mat.HasProperty("_TimeOverrideValue");
                row.ShaderFloats = CollectShaderFloats(mat);
                row.ShaderColors = CollectShaderColors(mat);
            }

            if (descriptor.m_ButtonTexture != null)
            {
                row.ButtonTexture = descriptor.m_ButtonTexture.name;
                row.ButtonTextureWidth = descriptor.m_ButtonTexture.width;
                row.ButtonTextureHeight = descriptor.m_ButtonTexture.height;
                row.IconLuma = SampleIconLuma(descriptor.m_ButtonTexture);
            }

            return row;
        }

        static string CollectShaderFloats(Material mat)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < kShaderFloatNames.Length; ++i)
            {
                string name = kShaderFloatNames[i];
                if (!mat.HasProperty(name))
                {
                    continue;
                }
                if (sb.Length > 0)
                {
                    sb.Append(';');
                }
                sb.Append(name);
                sb.Append('=');
                sb.Append(mat.GetFloat(name).ToString("0.###"));
            }
            return sb.ToString();
        }

        static string CollectShaderColors(Material mat)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < kShaderColorNames.Length; ++i)
            {
                string name = kShaderColorNames[i];
                if (!mat.HasProperty(name))
                {
                    continue;
                }
                Color c = mat.GetColor(name);
                if (sb.Length > 0)
                {
                    sb.Append(';');
                }
                sb.Append(name);
                sb.Append('=');
                sb.Append(c.r.ToString("0.##"));
                sb.Append(',');
                sb.Append(c.g.ToString("0.##"));
                sb.Append(',');
                sb.Append(c.b.ToString("0.##"));
                sb.Append(',');
                sb.Append(c.a.ToString("0.##"));
            }
            return sb.ToString();
        }

        static string SampleIconLuma(Texture2D tex)
        {
            if (tex == null)
            {
                return "";
            }
            try
            {
                Color32[] pixels = tex.GetPixels32();
                if (pixels == null || pixels.Length == 0)
                {
                    return "unreadable";
                }
                double sum = 0;
                int nearBlack = 0;
                for (int i = 0; i < pixels.Length; ++i)
                {
                    float lum = (0.299f * pixels[i].r + 0.587f * pixels[i].g + 0.114f * pixels[i].b) / 255f;
                    sum += lum;
                    if (lum < 0.04f)
                    {
                        nearBlack++;
                    }
                }
                double avg = sum / pixels.Length;
                double blackFrac = (double)nearBlack / pixels.Length;
                return avg.ToString("0.000") + "/" + blackFrac.ToString("0.000");
            }
            catch
            {
                return "unreadable";
            }
        }
    }
}
