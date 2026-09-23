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

using System.IO;
using UnityEditor;
using UnityEngine;

namespace TiltBrush
{
    public class AdjustReflectionCubemap : EditorWindow
    {
        private const string kDefaultFolder = "Assets/Resources/Cubemaps";

        private Cubemap m_Source;
        private float m_Multiplier = 1f;
        private string m_OutputName = "threelight_reflection_adj";
        private Vector2 m_Scroll;

        [MenuItem("Advanced/Cubemaps/Adjust Reflection Cubemap")]
        static void OpenWindow()
        {
            AdjustReflectionCubemap window = GetWindow<AdjustReflectionCubemap>(
                false, "Adjust Cubemap");
            window.minSize = new Vector2(360f, 240f);
            window.Show();
        }

        void OnGUI()
        {
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);

            EditorGUILayout.LabelField(
                "Writes a new Cubemap. Does not overwrite the source.",
                EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space();

            m_Source = (Cubemap)EditorGUILayout.ObjectField(
                "Source Cubemap",
                m_Source,
                typeof(Cubemap),
                false);

            m_Multiplier = EditorGUILayout.Slider("RGB multiplier", m_Multiplier, 0.05f, 4f);
            EditorGUILayout.HelpBox(
                "Below 1 = darker. 1 = copy. Above 1 = brighter. " +
                "Output is RGBAHalf so values over 1 can stay HDR. " +
                "Directional lights on the Environment are unchanged.",
                MessageType.None);

            m_OutputName = EditorGUILayout.TextField("Output file name", m_OutputName);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(m_Source == null || string.IsNullOrEmpty(m_OutputName)))
            {
                if (GUILayout.Button("Write adjusted cubemap"))
                {
                    WriteAdjustedCopy();
                }
            }

            EditorGUILayout.EndScrollView();
        }

        void WriteAdjustedCopy()
        {
            if (m_Source == null)
            {
                Debug.LogError("[AdjustReflectionCubemap] No source cubemap.");
                return;
            }

            string folder = kDefaultFolder;
            if (!AssetDatabase.IsValidFolder(folder))
            {
                folder = "Assets";
            }

            string safeName = SanitizeFileName(m_OutputName);
            string destPath = folder + "/" + safeName + ".asset";
            destPath = AssetDatabase.GenerateUniqueAssetPath(destPath);

            string srcPath = AssetDatabase.GetAssetPath(m_Source);
            bool madeReadable = EnsureReadable(srcPath);

            Cubemap dest = null;
            try
            {
                dest = BuildAdjustedCubemap(m_Source, m_Multiplier);
                if (dest == null)
                {
                    Debug.LogError("[AdjustReflectionCubemap] Could not read pixels from " + srcPath +
                        ". Set the importer to Readable and Texture Shape Cube.");
                    return;
                }

                AssetDatabase.CreateAsset(dest, destPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                EditorGUIUtility.PingObject(dest);
                Debug.Log("[AdjustReflectionCubemap] Wrote " + destPath +
                    " multiplier=" + m_Multiplier);
            }
            finally
            {
                if (madeReadable)
                {
                    RestoreUnreadable(srcPath);
                }
            }
        }

        static Cubemap BuildAdjustedCubemap(Cubemap source, float multiplier)
        {
            int size = source.width;
            if (size < 16)
            {
                return null;
            }

            Cubemap dest = new Cubemap(size, TextureFormat.RGBAHalf, true);
            dest.name = source.name + "_adj";
            dest.wrapMode = TextureWrapMode.Clamp;
            dest.filterMode = FilterMode.Trilinear;

            CubemapFace[] faces =
            {
                CubemapFace.PositiveX,
                CubemapFace.NegativeX,
                CubemapFace.PositiveY,
                CubemapFace.NegativeY,
                CubemapFace.PositiveZ,
                CubemapFace.NegativeZ
            };

            for (int f = 0; f < faces.Length; ++f)
            {
                Color[] pixels;
                try
                {
                    pixels = source.GetPixels(faces[f], 0);
                }
                catch (System.Exception ex)
                {
                    Debug.LogError("[AdjustReflectionCubemap] GetPixels failed: " + ex.Message);
                    Object.DestroyImmediate(dest);
                    return null;
                }

                for (int i = 0; i < pixels.Length; ++i)
                {
                    pixels[i].r *= multiplier;
                    pixels[i].g *= multiplier;
                    pixels[i].b *= multiplier;
                }

                dest.SetPixels(pixels, faces[f], 0);
            }

            dest.Apply(true, false);
            return dest;
        }

        static bool EnsureReadable(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                return false;
            }

            if (importer.isReadable)
            {
                return false;
            }

            importer.isReadable = true;
            importer.SaveAndReimport();
            return true;
        }

        static void RestoreUnreadable(string assetPath)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            importer.isReadable = false;
            importer.SaveAndReimport();
        }

        static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "reflection_adj";
            }

            char[] invalid = Path.GetInvalidFileNameChars();
            char[] chars = name.Trim().ToCharArray();
            for (int i = 0; i < chars.Length; ++i)
            {
                for (int j = 0; j < invalid.Length; ++j)
                {
                    if (chars[i] == invalid[j] || chars[i] == ' ')
                    {
                        chars[i] = '_';
                        break;
                    }
                }
            }

            return new string(chars);
        }

    } // functions complete

} // Namespace Tilt Brush
