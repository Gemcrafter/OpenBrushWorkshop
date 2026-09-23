// Copyright 2024 The Open Brush Authors
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

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TiltBrush
{
    public class AssignHiRezIcons : Editor
    {
        private const string kIconFolder = "Assets/Resources/BrushIcons/VideoBlack";
        private const string kFilePrefix = "brush-";
        private const string kFileExtension = ".png";

        [MenuItem("Open Brush/Screenshots/Assign Hi-Rez Brush Icons")]
        static void AssignIcons()
        {
            List<string> pngPaths = FindIconFiles();
            if (pngPaths.Count == 0)
            {
                Debug.LogError("[AssignHiRezIcons] No PNG files found in " + kIconFolder);
                return;
            }

            int targetSize = BrushPaletteConfig.GetBrushCellIconSize();
            ConfigureImportSettings(pngPaths, targetSize);

            Dictionary<string, BrushDescriptor> descriptorsByName = BuildDescriptorLookup();
            int assignedCount = 0;
            int skippedCount = 0;

            foreach (string pngPath in pngPaths)
            {
                string durableName = DurableNameFromPath(pngPath);
                BrushDescriptor descriptor;
                if (!descriptorsByName.TryGetValue(durableName, out descriptor))
                {
                    Debug.LogError("[AssignHiRezIcons] SKIP no matching BrushDescriptor for " +
                        durableName + " (" + pngPath + ")");
                    skippedCount++;
                    continue;
                }

                Texture2D icon = AssetDatabase.LoadAssetAtPath<Texture2D>(pngPath);
                if (icon == null)
                {
                    Debug.LogError("[AssignHiRezIcons] SKIP could not load texture at " + pngPath);
                    skippedCount++;
                    continue;
                }

                descriptor.m_ButtonTexture = icon;
                EditorUtility.SetDirty(descriptor);
                assignedCount++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[AssignHiRezIcons] Target resolution " + targetSize +
                " from BrushPaletteConfig. Assigned " + assignedCount + ", skipped " + skippedCount);
        }

        private static List<string> FindIconFiles()
        {
            List<string> result = new List<string>();
            if (!Directory.Exists(kIconFolder))
            {
                Debug.LogError("[AssignHiRezIcons] Folder not found: " + kIconFolder);
                return result;
            }

            string[] files = Directory.GetFiles(kIconFolder, "*" + kFileExtension, SearchOption.TopDirectoryOnly);
            foreach (string file in files)
            {
                result.Add(file.Replace("\\", "/"));
            }
            return result;
        }

        private static void ConfigureImportSettings(List<string> pngPaths, int targetSize)
        {
            if (targetSize < 32)
            {
                targetSize = 128;
            }

            foreach (string pngPath in pngPaths)
            {
                TextureImporter importer = AssetImporter.GetAtPath(pngPath) as TextureImporter;
                if (importer == null)
                {
                    Debug.LogError("[AssignHiRezIcons] SKIP no TextureImporter for " + pngPath);
                    continue;
                }

                importer.isReadable = true;
                importer.maxTextureSize = targetSize;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Bilinear;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
        }


        private static Dictionary<string, BrushDescriptor> BuildDescriptorLookup()
        {
            Dictionary<string, BrushDescriptor> result = new Dictionary<string, BrushDescriptor>();
            string[] guids = AssetDatabase.FindAssets("t:BrushDescriptor");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                BrushDescriptor descriptor = AssetDatabase.LoadAssetAtPath<BrushDescriptor>(path);
                if (descriptor == null)
                {
                    continue;
                }
                if (descriptor.m_HiddenInGui)
                {
                    continue;
                }
                if (string.IsNullOrEmpty(descriptor.DurableName))
                {
                    continue;
                }
                if (result.ContainsKey(descriptor.DurableName))
                {
                    Debug.LogError("[AssignHiRezIcons] Duplicate DurableName " +
                        descriptor.DurableName + " at " + path);
                    continue;
                }
                result[descriptor.DurableName] = descriptor;
            }
            return result;
        }

        private static string DurableNameFromPath(string pngPath)
        {
            string fileName = Path.GetFileNameWithoutExtension(pngPath);
            if (fileName.StartsWith(kFilePrefix))
            {
                return fileName.Substring(kFilePrefix.Length);
            }
            return fileName;
        }
    }
}




// Draft Version
/* 
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TiltBrush
{
    public class AssignHiRezIcons : Editor
    {
        // -------------------------------------------------------------
        // IMPORTANT
        // Change this to test different icon resolutions (256, 512, 1024).
        private const int kTargetResolution = 1024;
        // -------------------------------------------------------------

        private const string kIconFolder = "Assets/Resources/BrushIcons/VideoBlack";
        private const string kFilePrefix = "brush-";
        private const string kFileExtension = ".png";

        [MenuItem("Open Brush/Screenshots/Assign Hi-Rez Brush Icons")]
        static void AssignIcons()
        {
            List<string> pngPaths = FindIconFiles();
            if (pngPaths.Count == 0)
            {
                Debug.LogError("[AssignHiRezIcons] No PNG files found in " + kIconFolder);
                return;
            }

            ConfigureImportSettings(pngPaths);

            Dictionary<string, BrushDescriptor> descriptorsByName = BuildDescriptorLookup();
            int assignedCount = 0;
            int skippedCount = 0;

            foreach (string pngPath in pngPaths)
            {
                string durableName = DurableNameFromPath(pngPath);
                BrushDescriptor descriptor;
                if (!descriptorsByName.TryGetValue(durableName, out descriptor))
                {
                    Debug.LogError("[AssignHiRezIcons] SKIP no matching BrushDescriptor for " +
                        durableName + " (" + pngPath + ")");
                    skippedCount++;
                    continue;
                }

                Texture2D icon = AssetDatabase.LoadAssetAtPath<Texture2D>(pngPath);
                if (icon == null)
                {
                    Debug.LogError("[AssignHiRezIcons] SKIP could not load texture at " + pngPath);
                    skippedCount++;
                    continue;
                }

                descriptor.m_ButtonTexture = icon;
                EditorUtility.SetDirty(descriptor);
                assignedCount++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[AssignHiRezIcons] Target resolution " + kTargetResolution +
                ". Assigned " + assignedCount + ", skipped " + skippedCount);
        }

        private static List<string> FindIconFiles()
        {
            List<string> result = new List<string>();
            if (!Directory.Exists(kIconFolder))
            {
                Debug.LogError("[AssignHiRezIcons] Folder not found: " + kIconFolder);
                return result;
            }

            string[] files = Directory.GetFiles(kIconFolder, "*" + kFileExtension, SearchOption.TopDirectoryOnly);
            foreach (string file in files)
            {
                result.Add(file.Replace("\\", "/"));
            }
            return result;
        }

        private static void ConfigureImportSettings(List<string> pngPaths)
        {
            foreach (string pngPath in pngPaths)
            {
                TextureImporter importer = AssetImporter.GetAtPath(pngPath) as TextureImporter;
                if (importer == null)
                {
                    Debug.LogError("[AssignHiRezIcons] SKIP no TextureImporter for " + pngPath);
                    continue;
                }

                int sourceWidth;
                int sourceHeight;
                importer.GetSourceTextureWidthAndHeight(out sourceWidth, out sourceHeight);

                bool alreadyMatchesTarget = sourceWidth == kTargetResolution && sourceHeight == kTargetResolution;
                if (alreadyMatchesTarget)
                {
                    Debug.Log("[AssignHiRezIcons] " + Path.GetFileName(pngPath) +
                        " is already " + kTargetResolution + "x" + kTargetResolution + ", skipping resize");
                }
                else
                {
                    importer.maxTextureSize = kTargetResolution;
                }

                importer.isReadable = true;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
            }
        }

        private static Dictionary<string, BrushDescriptor> BuildDescriptorLookup()
        {
            Dictionary<string, BrushDescriptor> result = new Dictionary<string, BrushDescriptor>();
            string[] guids = AssetDatabase.FindAssets("t:BrushDescriptor");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                BrushDescriptor descriptor = AssetDatabase.LoadAssetAtPath<BrushDescriptor>(path);
                if (descriptor == null)
                {
                    continue;
                }
                if (descriptor.m_HiddenInGui)
                {
                    continue;
                }
                if (string.IsNullOrEmpty(descriptor.DurableName))
                {
                    continue;
                }
                if (result.ContainsKey(descriptor.DurableName))
                {
                    Debug.LogError("[AssignHiRezIcons] Duplicate DurableName " +
                        descriptor.DurableName + " at " + path);
                    continue;
                }
                result[descriptor.DurableName] = descriptor;
            }
            return result;
        }



        private static string DurableNameFromPath(string pngPath)
        {
            string fileName = Path.GetFileNameWithoutExtension(pngPath);
            if (fileName.StartsWith(kFilePrefix))
            {
                return fileName.Substring(kFilePrefix.Length);
            }
            return fileName;
        }
    }
} // namespace TiltBrush
*/
