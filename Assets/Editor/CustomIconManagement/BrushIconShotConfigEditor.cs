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
using UnityEditor;
using UnityEngine;

namespace TiltBrush
{
    [CustomEditor(typeof(BrushIconShotConfig))]
    public class BrushIconShotConfigEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            BrushIconShotConfig config = (BrushIconShotConfig)target;
            config.EnsureDefaultPresets();
            serializedObject.Update();

            DrawEnvironmentPopup(config);

            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("useEnvironmentPlate"),
                new GUIContent("Use environment backdrop"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("captureSize"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Stroke color", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("shotColorName"),
                new GUIContent("Color name"));
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("shotColor"),
                new GUIContent("Color"));

            serializedObject.ApplyModifiedProperties();

            DrawPresetBar(config);
            if (GUILayout.Button("Add current color as preset"))
            {
                Undo.RecordObject(config, "Add shot color preset");
                config.AddCurrentColorAsPreset();
                EditorUtility.SetDirty(config);
            }

            BrushIconShotConfig.Swatch swatch = config.GetActiveSwatch();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Next folder", config.BuildOutputFolderName(swatch));
        }

        static void DrawPresetBar(BrushIconShotConfig config)
        {
            if (config.presets == null)
            {
                return;
            }
            const int kPerRow = 3;
            int drawn = 0;
            for (int i = 0; i < config.presets.Length; ++i)
            {
                BrushIconShotConfig.Swatch preset = config.presets[i];
                if (preset == null || string.IsNullOrEmpty(preset.commonName))
                {
                    continue;
                }
                if (drawn % kPerRow == 0)
                {
                    if (drawn > 0)
                    {
                        EditorGUILayout.EndHorizontal();
                    }
                    EditorGUILayout.BeginHorizontal();
                }
                if (GUILayout.Button(preset.commonName))
                {
                    Undo.RecordObject(config, "Set shot color preset");
                    config.shotColorName = preset.commonName;
                    config.shotColor = preset.color;
                    EditorUtility.SetDirty(config);
                }
                drawn++;
            }
            if (drawn > 0)
            {
                EditorGUILayout.EndHorizontal();
            }
        }

        static void DrawEnvironmentPopup(BrushIconShotConfig config)
        {
            List<Environment> envs = new List<Environment>();
            List<string> labels = new List<string>();
            string[] assetGuids = AssetDatabase.FindAssets("t:Environment");
            for (int i = 0; i < assetGuids.Length; ++i)
            {
                string path = AssetDatabase.GUIDToAssetPath(assetGuids[i]);
                Environment env = AssetDatabase.LoadAssetAtPath<Environment>(path);
                if (env == null)
                {
                    continue;
                }
                envs.Add(env);
                labels.Add(env.name);
            }

            if (envs.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No Environment assets found. HiRez will fall back to VideoBlack.",
                    MessageType.Warning);
                return;
            }

            int current = 0;
            string currentGuid = config.GetEnvironmentGuid();
            for (int i = 0; i < envs.Count; ++i)
            {
                if (string.Equals(envs[i].m_Guid.ToString(), currentGuid, StringComparison.OrdinalIgnoreCase))
                {
                    current = i;
                    break;
                }
            }

            int next = EditorGUILayout.Popup("Environment", current, labels.ToArray());
            Environment selected = envs[next];
            if (selected != null)
            {
                string guidString = selected.m_Guid.ToString();
                string folderName = selected.name;
                if (next != current ||
                    config.environmentGuid != guidString ||
                    config.environmentName != folderName)
                {
                    Undo.RecordObject(config, "Set icon shot environment");
                    config.environmentGuid = guidString;
                    config.environmentName = folderName;
                    EditorUtility.SetDirty(config);
                }
            }
        }
    } // functions complete

} // Namespace Tilt Brush
