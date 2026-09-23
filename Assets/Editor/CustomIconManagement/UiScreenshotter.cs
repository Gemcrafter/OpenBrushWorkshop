// Copyright 2023 The Tilt Brush Authors
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
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace TiltBrush
{
    public class UiScreenshotter : Editor
    {
        private const float kBrushScreenshotTime = 0.5f;
        private const int kScreenshotSupersampling = 2;
        private const int kScreenshotMsaaSamples = 4;
        private const string kScreenshotOutputDirectory = "Support/Screenshots";
        private const float kBrushScreenshotFov = 35f;
        private const int kBrushScreenshotSize = 1024;

        private struct IconShotPlate
        {
            public string FolderName;
            public string GuidString;
        }

        private static readonly IconShotPlate kPlateVideoBlack = new IconShotPlate
        {
            FolderName = "VideoBlack",
            GuidString = "580b4529-ac50-4fe9-b8d2-635765a25888"
        };

        private static readonly IconShotPlate kPlateBlack = new IconShotPlate
        {
            FolderName = "Black",
            GuidString = "580b4529-ac50-4fe9-b8d2-635765a14893"
        };

        private static readonly IconShotPlate kPlateMidnightBlack = new IconShotPlate
        {
            FolderName = "MidnightBlack",
            GuidString = "580b4529-ac50-4fe9-b8d2-635765a14877"
        };

        private static readonly IconShotPlate kPlateDeepSpace = new IconShotPlate
        {
            FolderName = "DeepSpace",
            GuidString = "96cf6f36-47b6-44f4-bdbf-63be2ddac910"
        };

        private static readonly IconShotPlate kDefaultBrushIconPlate = kPlateVideoBlack;

        private static bool IsPlaying()
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("You can only run this whilst in Play Mode");
                return false;
            }
            return true;
        }

        [MenuItem("Open Brush/Screenshots/Generate Brush Screenshots")]
        static void GenerateBrushScreenShots()
        {
            GenerateBrushScreenShotsForPlate(kDefaultBrushIconPlate);
        }

        [MenuItem("Open Brush/Screenshots/Generate Brush Screenshots (MidnightBlack)")]
        static void GenerateBrushScreenShotsMidnightBlack()
        {
            GenerateBrushScreenShotsForPlate(kPlateMidnightBlack);
        }

        [MenuItem("Open Brush/Screenshots/Generate Brush Screenshots (Black)")]
        static void GenerateBrushScreenShotsBlack()
        {
            GenerateBrushScreenShotsForPlate(kPlateBlack);
        }

        [MenuItem("Open Brush/Screenshots/Generate Brush Screenshots (DeepSpace)")]
        static void GenerateBrushScreenShotsDeepSpace()
        {
            GenerateBrushScreenShotsForPlate(kPlateDeepSpace);
        }

        static void GenerateBrushScreenShotsForPlate(IconShotPlate plate)
        {
            if (!IsPlaying()) return;
            if (!SetupIconShotEnvironment(plate)) return;
            DelayedGenerateBrushScreenShots(plate);
        }

        [MenuItem("Open Brush/Screenshots/Generate Environment Screenshots")]
        static void GenerateEnvironmentScreenshots()
        {
            if (!IsPlaying()) return;
            DelayedGenerateEnvironmentScreenshots();
        }

        [MenuItem("Open Brush/Screenshots/Generate Panel Screenshots")]
        static void GeneratePanelScreenshots()
        {
            if (!IsPlaying()) return;

            SetupBlackEnvironment();

            foreach (BasePanel.PanelType panelType in (BasePanel.PanelType[])Enum.GetValues(typeof(BasePanel.PanelType)))
            {
                if (!PanelManager.m_Instance.IsPanelOpen(panelType))
                {
                    PanelManager.m_Instance.OpenPanel(panelType, TrTransform.T(new Vector3(0, 50, 2)));
                }
            }
            DelayedGeneratePanelScreenshots();
        }

        private static void SetupBlackEnvironment()
        {
            var blackGuid = Guid.Parse(kPlateBlack.GuidString);
            var env = EnvironmentCatalog.m_Instance.GetEnvironment(blackGuid);
            if (env == null)
            {
                Debug.LogError("[BrushIconShot] Environment not found: " + kPlateBlack.FolderName + " " + kPlateBlack.GuidString);
                return;
            }
            SceneSettings.m_Instance.SetDesiredPreset(env,
                keepSceneTransform: true, forceTransition: false, hasCustomLights: false, skipFade: true);
        }

        private static bool SetupIconShotEnvironment(IconShotPlate plate)
        {
            var guid = Guid.Parse(plate.GuidString);
            var env = EnvironmentCatalog.m_Instance.GetEnvironment(guid);
            if (env == null)
            {
                Debug.LogError("[BrushIconShot] Environment not found: " + plate.FolderName + " " + plate.GuidString);
                return false;
            }
            SceneSettings.m_Instance.SetDesiredPreset(env,
                keepSceneTransform: true, forceTransition: false, hasCustomLights: false, skipFade: true);
            Debug.Log("[BrushIconShot] Plate " + plate.FolderName);
            return true;
        }

        async static void DelayedGenerateBrushScreenShots(IconShotPlate plate)
        {
            await Task.Delay(3000);
            ApiMethods.ViewOnly();
            PanelManager.m_Instance.HideAllPanels();

            var cam = InitScreenshotCamera();
            cam.fieldOfView = kBrushScreenshotFov;
            cam.aspect = 1f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;

            var path = new List<TrTransform>();
            var origin = new Vector3(-1.25f, 100, 4);
            for (float i = 0; i < 3; i += 0.1f)
            {
                path.Add(TrTransform.T(new Vector3(i, Mathf.Sin(i * 5f) * (1 - i / 3), 0)));
            }

            var batchManager = App.Scene.ActiveCanvas.BatchManager;
            bool wasOneStrokePerBatch = batchManager.OneStrokePerBatch;
            bool wasForceDeterministicBirthTimeForExport = App.Config.m_ForceDeterministicBirthTimeForExport;
            batchManager.OneStrokePerBatch = true;
            App.Config.m_ForceDeterministicBirthTimeForExport = true;

            try
            {
                foreach (var brush in BrushCatalog.m_Instance.AllBrushes)
                {
                    List<Stroke> strokes = null;
                    try
                    {
                        if (brush == null)
                        {
                            Debug.LogError("[BrushIconShot] SKIP null brush");
                            continue;
                        }
                        if (brush.m_BrushPrefab == null)
                        {
                            Debug.LogError("[BrushIconShot] SKIP null prefab " + brush.DurableName + " " + brush.m_Guid);
                            continue;
                        }

                        PointerManager.m_Instance.SetBrushForAllPointers(brush);
                        await Task.Delay(100);
                        strokes = DrawStrokes.DrawNestedTrList(
                            new List<IEnumerable<TrTransform>> { path },
                            TrTransform.T(origin));
                        SetFixedShaderTime(strokes, kBrushScreenshotTime);
                        batchManager.FlushMeshUpdates();
                        SaveCurrentView(cam, "brush-" + brush.DurableName + ".png",
                            kBrushScreenshotSize, kBrushScreenshotSize, plate.FolderName);
                    }
                    catch (Exception e)
                    {
                        string name = brush != null ? brush.DurableName : "null";
                        string guid = brush != null ? brush.m_Guid.ToString() : "null";
                        Debug.LogError("[BrushIconShot] SKIP " + name + " " + guid + " : " + e.Message);
                    }
                    finally
                    {
                        if (strokes != null)
                        {
                            DeleteStrokes(strokes);
                        }
                    }
                }
            }
            finally
            {
                App.Config.m_ForceDeterministicBirthTimeForExport = wasForceDeterministicBirthTimeForExport;
                batchManager.OneStrokePerBatch = wasOneStrokePerBatch;
            }
        }

        async static void DelayedGeneratePanelScreenshots()
        {
            await Task.Delay(3000);

            var cam = InitScreenshotCamera();

            int count = ((BasePanel.PanelType[])Enum.GetValues(typeof(BasePanel.PanelType))).Length;
            Debug.Log($"Starting {count} panel screenshots");
            for (var i = 0; i < count; i++)
            {
                var panelType = ((BasePanel.PanelType[])Enum.GetValues(typeof(BasePanel.PanelType)))[i];
                Debug.Log($"Screenshot {i}: {panelType}");
                TrTransform panelTr = TrTransform.T(new Vector3(-1.25f, 100, 4));
                if (PanelManager.m_Instance.IsPanelOpen(panelType))
                {
                    BasePanel panel = PanelManager.m_Instance.GetPanelByType(panelType);
                    panel.PanelGazeActive(true);
                    await Task.Delay(500);
                    var originalTransform = TrTransform.FromTransform(panel.transform);
                    panelTr.ToTransform(panel.transform);
                    panel.ResetReticleOffset();
                    SaveCurrentView(cam, $"panel-{panelType}.png", 1600, 1600);

                    // Try to open popups
                    FieldInfo fieldInfo = typeof(BasePanel).GetField("m_PanelPopUpMap", BindingFlags.NonPublic | BindingFlags.Instance);
                    PopupMapKey[] popupMap = (PopupMapKey[])fieldInfo?.GetValue(panel);
                    if (popupMap != null)
                    {
                        foreach (var popup in popupMap)
                        {
                            var btn = panel.GetComponentsInChildren<OptionButton>()
                                .FirstOrDefault(x => x.m_Command == popup.m_Command);
                            if (btn == null)
                            {
                                Debug.LogWarning($"No button found for {popup.m_Command}");
                                continue;
                            }
                            Debug.Log($"Screenshop popup for {popup.m_Command}");
                            GameObject go = Instantiate(popup.m_PopUpPrefab,
                                btn.transform.position + new Vector3(.5f, 0, -0.25f), btn.transform.rotation);
                            go.transform.localScale = Vector3.one * 5;
                            var activePopUp = go.GetComponent<PopUpWindow>();
                            activePopUp.Init(panel.gameObject, "");
                            try
                            {
                                activePopUp.SetPopupCommandParameters(btn.m_CommandParam, btn.m_CommandParam2);
                            }
                            catch (NullReferenceException e) { }
                            SaveCurrentView(cam, $"panel-{panelType}_{btn.m_Command}.png", 1600, 1600);
                            go.transform.position = new Vector3(-100, 0, 0);
                            Destroy(go);
                        }
                    }
                    originalTransform.ToTransform(panel.transform);
                }
            }
        }

        async static void DelayedGenerateEnvironmentScreenshots()
        {
            ApiMethods.ViewOnly();
            PanelManager.m_Instance.HideAllPanels();
            await Task.Delay(1000);
            var cam = Camera.main;
            cam.transform.position = new Vector3(0, 10, -5);
            cam.transform.rotation = Quaternion.identity;
            cam.fieldOfView = 110;
            cam.aspect = 1;
            foreach (var env in EnvironmentCatalog.m_Instance.AllEnvironments)
            {
                SceneSettings.m_Instance.SetDesiredPreset(env,
                    keepSceneTransform: true, forceTransition: false, hasCustomLights: false, skipFade: true);
                await Task.Delay(1000);
                SaveCurrentView(cam, $"environment-{env.Description}.png", 1024, 1024);
            }
        }


        private static Camera InitScreenshotCamera()
        {
            var cam = Camera.main;
            cam.transform.position = new Vector3(0, 100, 0);
            cam.transform.rotation = Quaternion.identity;
            return cam;
        }

        private static void SetFixedShaderTime(IEnumerable<Stroke> strokes, float time)
        {
            Vector4 timeValue = new Vector4(time / 20f, time, time * 2f, time * 3f);
            foreach (var stroke in strokes)
            {
                try
                {
                    var material = stroke.m_BatchSubset.m_ParentBatch.InstantiatedMaterial;
                    if (!material.HasFloat("_TimeBlend") ||
                        !material.HasVector("_TimeOverrideValue"))
                    {
                        continue;
                    }
                    stroke.SetShaderFloat("_TimeBlend", 1f);
                    stroke.SetShaderVector(
                        "_TimeOverrideValue",
                        timeValue.x,
                        timeValue.y,
                        timeValue.z,
                        timeValue.w);
                }
                catch (StrokeShaderModifierException)
                {
                    // Static brushes do not expose the time override properties.
                }
            }
        }

        private static void DeleteStrokes(IEnumerable<Stroke> strokes)
        {
            foreach (var stroke in strokes)
            {
                SketchMemoryScript.m_Instance.RemoveMemoryObject(stroke);
                stroke.Uncreate();
            }
        }

        static void SaveCurrentView(Camera cameraToCapture, string fileName, int resWidth, int resHeight, string relativeSubdir = null)
        {
            int renderWidth = resWidth * kScreenshotSupersampling;
            int renderHeight = resHeight * kScreenshotSupersampling;
            RenderTexture rt = new RenderTexture(renderWidth, renderHeight, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = kScreenshotMsaaSamples,
                filterMode = FilterMode.Bilinear
            };
            RenderTexture downsampledRt = new RenderTexture(resWidth, resHeight, 0, RenderTextureFormat.ARGB32)
            {
                filterMode = FilterMode.Bilinear
            };
            Texture2D screenShot = null;
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = cameraToCapture.targetTexture;
            bool previousAllowMsaa = cameraToCapture.allowMSAA;
            try
            {
                cameraToCapture.allowMSAA = true;
                cameraToCapture.targetTexture = rt;
                screenShot = new Texture2D(resWidth, resHeight, TextureFormat.RGB24, false);
                cameraToCapture.Render();
                Graphics.Blit(rt, downsampledRt);
                RenderTexture.active = downsampledRt;
                screenShot.ReadPixels(new Rect(0, 0, resWidth, resHeight), 0, 0);
                byte[] bytes = screenShot.EncodeToPNG();
                string outputDirectory = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    kScreenshotOutputDirectory);
                if (!string.IsNullOrEmpty(relativeSubdir))
                {
                    outputDirectory = Path.Combine(outputDirectory, relativeSubdir);
                }
                Directory.CreateDirectory(outputDirectory);
                string filePath = Path.Combine(outputDirectory, fileName);
                File.WriteAllBytes(filePath, bytes);
            }
            finally
            {
                cameraToCapture.targetTexture = previousTarget;
                cameraToCapture.allowMSAA = previousAllowMsaa;
                RenderTexture.active = previousActive;
                if (screenShot != null)
                {
                    Destroy(screenShot);
                }
                Destroy(rt);
                Destroy(downsampledRt);
            }
        }


    } // Functions Complete

} // Namespace Tiltbrush
