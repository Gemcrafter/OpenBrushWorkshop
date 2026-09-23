
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace TiltBrush
{
    public class UiScreenshotterHiRez : Editor
    {
        private const int kScreenshotSupersampling = 1;
        private const int kScreenshotMsaaSamples = 4;
        private const int kMinKeptKnots = 8;
        private const int kDenseKnots = 24;
        private const int kHullKnots = 16;
        private const float kFrameLength = 1.0f;
        private const float kReadableSize = 0.40f;
        private const float kSolidMinLengthMeters = 0.002f;
        private const float kSolidAspectRatio = 0.2f;
        private const float kParticleSpawnMeters = 0.0025f;
        private const float kPrintTiltDeg = 35f;
        private const float kSwoopAmplitudeFactor = 0.28f;
        private const float kFrameMarginFactor = 1.25f;
        private const float kHullFrameMarginFactor = 2.0f;
        private const float kParticleFrameMarginFactor = 3.2f;
        private const float kCaptureFov = 35f;
        private const float kMinIconLuma = 0.008f;
        private const int kSettleMs = 400;
        private const int kParticleSettleMs = 50;

        // Identity matches original UiScreenshotter (TrTransform.T only).
        // LookRotation(+X,+Y) put QuadStrip nSurface on Y -- camera looks +Z -- edge-on hairline.
        static readonly Quaternion kPointerFacing = Quaternion.identity;
        static readonly Quaternion kPointerFacingFlip =
            Quaternion.AngleAxis(180f, Vector3.up) * Quaternion.identity;
        static readonly Quaternion kPointerFacingPrint =
            Quaternion.AngleAxis(kPrintTiltDeg, Vector3.forward) * Quaternion.identity;
        private const string kScreenshotOutputDirectory = "Support/Screenshots";

        static readonly float[] kTimeSamples = { 0.2f, 0.35f, 0.5f, 0.8f, 1.0f };

        private static bool IsPlaying()
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("You can only run this whilst in Play Mode");
                return false;
            }
            return true;
        }

        private static float ComputeCameraDistance(float length, float margin)
        {
            float largestDimension = Mathf.Max(length, length * kSwoopAmplitudeFactor * 2f);
            float frameWidth = largestDimension * margin;
            float halfAngleRad = kCaptureFov * Mathf.Deg2Rad / 2f;
            return (frameWidth / 2f) / Mathf.Tan(halfAngleRad);
        }

        private static TrTransform MakePathPoint(Vector3 pos, Quaternion facing)
        {
            TrTransform xf = TrTransform.T(pos);
            xf.rotation = facing;
            xf.scale = 1f;
            return xf;
        }

        private static List<TrTransform> GenerateStraightPath(
            float length, int segments, Quaternion facing)
        {
            var path = new List<TrTransform>();
            if (segments < 1)
            {
                segments = 1;
            }
            float step = length / segments;
            for (int n = 0; n <= segments; ++n)
            {
                path.Add(MakePathPoint(new Vector3(n * step, 0, 0), facing));
            }
            return path;
        }

        private static List<TrTransform> GenerateSwoopPath(
            float length, int segments, float amplitude, Quaternion facing)
        {
            var path = new List<TrTransform>();
            if (segments < 1)
            {
                segments = 1;
            }
            float step = length / segments;
            for (int n = 0; n <= segments; ++n)
            {
                float t = n / (float)segments;
                float y = amplitude * Mathf.Sin(Mathf.PI * t);
                path.Add(MakePathPoint(new Vector3(n * step, y, 0), facing));
            }
            return path;
        }

        private static List<TrTransform> GenerateHullPath(
            float length, int segments, Quaternion facing)
        {
            var path = new List<TrTransform>();
            if (segments < 8)
            {
                segments = 8;
            }
            float half = length * 0.5f;
            for (int n = 0; n <= segments; ++n)
            {
                float t = n / (float)segments;
                float ang = t * Mathf.PI * 2f;
                float x = half + Mathf.Cos(ang) * half;
                float y = Mathf.Sin(ang) * half;
                float z = Mathf.Sin(ang * 2f) * half * 0.35f;
                path.Add(MakePathPoint(new Vector3(x, y, z), facing));
            }
            return path;
        }

        static List<TrTransform> OffsetPath(List<TrTransform> path, Vector3 offset)
        {
            var copy = new List<TrTransform>();
            if (path == null)
            {
                return copy;
            }
            for (int i = 0; i < path.Count; ++i)
            {
                TrTransform xf = path[i];
                xf.translation += offset;
                copy.Add(xf);
            }
            return copy;
        }

        static bool WantsMultiStroke(BrushDescriptor brush)
        {
            return PrefabHas<MidpointPlusLifetimeSprayBrush>(brush);
        }

        static List<TrTransform> GenerateVerticalPath(
            float length, int segments, Quaternion facing)
        {
            var path = new List<TrTransform>();
            if (segments < 1)
            {
                segments = 1;
            }
            float step = length / segments;
            float half = length * 0.5f;
            for (int n = 0; n <= segments; ++n)
            {
                path.Add(MakePathPoint(new Vector3(half, n * step - half, 0f), facing));
            }
            return path;
        }

        static List<IEnumerable<TrTransform>> BuildStrokeGroups(
            List<TrTransform> path,
            float length,
            int segments,
            Quaternion facing,
            float size,
            bool multi)
        {
            var groups = new List<IEnumerable<TrTransform>>();
            groups.Add(path);
            if (!multi || path == null)
            {
                return groups;
            }
            float off = size * 0.35f;
            groups.Add(OffsetPath(path, new Vector3(0f, off, 0f)));
            groups.Add(OffsetPath(path, new Vector3(0f, -off, 0f)));
            groups.Add(GenerateVerticalPath(length, segments, facing));
            return groups;
        }

        static string ShaderName(BrushDescriptor brush)
        {
            if (brush == null || brush.Material == null || brush.Material.shader == null)
            {
                return "";
            }
            return brush.Material.shader.name;
        }

        static string PrefabBrushType(BrushDescriptor brush)
        {
            if (brush == null || brush.m_BrushPrefab == null)
            {
                return "none";
            }
            BaseBrushScript[] scripts =
                brush.m_BrushPrefab.GetComponentsInChildren<BaseBrushScript>(true);
            if (scripts == null || scripts.Length == 0)
            {
                return "none";
            }
            return scripts[0].GetType().Name;
        }

        static bool PrefabTypeContains(BrushDescriptor brush, string token)
        {
            string typeName = PrefabBrushType(brush);
            return typeName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool ShaderNameContains(BrushDescriptor brush, string token)
        {
            string shaderName = ShaderName(brush);
            return shaderName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool ShouldSkipBrush(BrushDescriptor brush)
        {
            if (brush == null)
            {
                return true;
            }
            if (brush.m_BrushPrefab == null)
            {
                return true;
            }
            if (PrefabBrushType(brush) == "none")
            {
                return true;
            }
            if (brush.m_BrushPrefab.GetComponentInChildren<Renderer>(true) == null)
            {
                return true;
            }
            if (ShaderNameContains(brush, "Template") ||
                ShaderNameContains(brush, "EnvironmentDiffuse"))
            {
                return true;
            }
            return false;
        }

        static bool PrefabHas<T>(BrushDescriptor brush) where T : Component
        {
            if (brush == null || brush.m_BrushPrefab == null)
            {
                return false;
            }
            return brush.m_BrushPrefab.GetComponent<T>() != null ||
                brush.m_BrushPrefab.GetComponentInChildren<T>(true) != null;
        }

        static bool IsHullBrush(BrushDescriptor brush)
        {
            return PrefabTypeContains(brush, "Hull") || ShaderNameContains(brush, "Hull");
        }

        static bool IsPaintStripBrush(BrushDescriptor brush)
        {
            if (PrefabHas<QuadStripBrush>(brush) ||
                PrefabHas<FlatGeometryBrush>(brush) ||
                PrefabHas<ThickGeometryBrush>(brush))
            {
                return true;
            }
            string typeName = PrefabBrushType(brush);
            return typeName.IndexOf("QuadStrip", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("GeometryBrush", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static float ParticleCloudRadius(BrushDescriptor brush, float size)
        {
            if (brush == null)
            {
                return 0f;
            }
            float rangeX = brush.m_BrushSizeRange.x;
            if (rangeX < 0.0001f)
            {
                rangeX = 0.0001f;
            }
            return size * brush.m_ParticleSpeed / rangeX;
        }

        static float IconBrushSize(BrushDescriptor brush)
        {
            Vector2 range = brush.m_BrushSizeRange;
            float lo = range.x;
            float hi = range.y;
            if (hi < lo)
            {
                hi = lo;
            }
            if (hi <= 0.0001f)
            {
                return kReadableSize;
            }
            float size = Mathf.Max(lo, kReadableSize);
            if (size > hi)
            {
                size = hi;
            }
            return size;
        }

        static float IconSpawnInterval(BrushDescriptor brush, float size, bool isSpray, bool isParticle)
        {
            float metersToUnits = App.METERS_TO_UNITS;
            if (isSpray)
            {
                float rate = brush.m_SprayRateMultiplier;
                if (rate < 0.0001f)
                {
                    rate = 1f;
                }
                return size / rate;
            }
            if (isParticle)
            {
                float rate = brush.m_ParticleRate;
                if (rate < 0.0001f)
                {
                    rate = 1f;
                }
                return (kParticleSpawnMeters * metersToUnits) / rate;
            }
            return (kSolidMinLengthMeters * metersToUnits) + (size * kSolidAspectRatio);
        }

        [MenuItem("Open Brush/Screenshots/Generate Brush Screenshots (HiRez)")]
        static void GenerateBrushScreenShotsHiRez()
        {
            if (!IsPlaying())
            {
                return;
            }
            DelayedGenerateBrushScreenShotsHiRez();
        }

        private static Environment GetConfiguredEnvironment(BrushIconShotConfig config)
        {
            string guidString = config != null
                ? config.GetEnvironmentGuid()
                : BrushIconShotConfig.DefaultEnvironmentGuid;
            Guid guid;
            try
            {
                guid = Guid.Parse(guidString);
            }
            catch
            {
                Debug.LogError("[BrushIconShotHiRez] Bad environment guid " + guidString);
                return null;
            }
            Environment env = EnvironmentCatalog.m_Instance.GetEnvironment(guid);
            if (env == null)
            {
                Debug.LogError("[BrushIconShotHiRez] Environment not in catalog: " + guidString);
            }
            return env;
        }

        private static bool ApplyIconShotEnvironment(Environment env, string folderName)
        {
            if (env == null)
            {
                return false;
            }
            SceneSettings.m_Instance.SetDesiredPreset(env,
                keepSceneTransform: true, forceTransition: true, hasCustomLights: false, skipFade: true);
            Debug.Log("[BrushIconShotHiRez] Requested environment " + folderName);
            return true;
        }

        async static Task<bool> WaitForEnvironment(Environment env, string folderName)
        {
            for (int i = 0; i < 20; ++i)
            {
                Environment current = SceneSettings.m_Instance.CurrentEnvironment;
                if (current == env)
                {
                    Debug.Log("[BrushIconShotHiRez] Current environment is " + folderName);
                    return true;
                }
                await Task.Delay(100);
            }
            Environment still = SceneSettings.m_Instance.CurrentEnvironment;
            string name = still != null ? still.name : "null";
            Debug.LogError("[BrushIconShotHiRez] Environment did not become " +
                folderName + ". Still " + name);
            return false;
        }

        async static void DelayedGenerateBrushScreenShotsHiRez()
        {
            await Task.Delay(3000);

            BrushIconShotConfig config = LoadShotConfig();
            BrushIconShotConfig.Swatch swatch = config != null
                ? config.GetActiveSwatch()
                : BrushIconShotConfig.DefaultBlue();
            bool useEnvironmentPlate = config != null && config.useEnvironmentPlate;
            int captureSize = config != null ? config.GetCaptureSize() : 4096;
            string envFolderName = config != null
                ? config.GetEnvironmentName()
                : BrushIconShotConfig.DefaultEnvironmentName;

            Environment env = GetConfiguredEnvironment(config);
            if (!ApplyIconShotEnvironment(env, envFolderName))
            {
                return;
            }
            if (!await WaitForEnvironment(env, envFolderName))
            {
                return;
            }

            ApiMethods.ViewOnly();
            PanelManager.m_Instance.HideAllPanels();

            if (App.BrushColor != null)
            {
                App.BrushColor.CurrentColor = swatch.color;
            }

            Camera cam = CreateCaptureCamera(useEnvironmentPlate);
            Light keyLight = CreateCaptureKeyLight(cam);
            Color previousAmbient = RenderSettings.ambientLight;
            float previousAmbientIntensity = RenderSettings.ambientIntensity;
            UnityEngine.Rendering.AmbientMode previousAmbientMode = RenderSettings.ambientMode;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.35f, 0.35f, 0.35f, 1f);
            RenderSettings.ambientIntensity = 1f;

            bool wasForceDeterministicBirthTimeForExport = App.Config.m_ForceDeterministicBirthTimeForExport;
            App.Config.m_ForceDeterministicBirthTimeForExport = true;

            string outputSubdir = config != null
                ? config.BuildOutputFolderName(swatch)
                : envFolderName + "_" + captureSize + "_" + BrushIconShotConfig.FolderToken(swatch) + "_BlackPlate";

            Debug.Log("[BrushIconShotHiRez] " + envFolderName +
                " color " + swatch.commonName + " #" + BrushIconShotConfig.HexRgb(swatch.color) +
                " plate " + (useEnvironmentPlate ? "Environment" : "Black") +
                " -> " + outputSubdir);

            try
            {
                foreach (var brush in BrushCatalog.m_Instance.AllBrushes)
                {
                    List<Stroke> strokes = null;
                    var batchManager = App.Scene.ActiveCanvas.BatchManager;
                    bool wasOneStrokePerBatch = batchManager.OneStrokePerBatch;
                    batchManager.OneStrokePerBatch = true;

                    try
                    {
                        if (brush == null)
                        {
                            Debug.LogError("[BrushIconShotHiRez] SKIP null brush");
                            continue;
                        }
                        if (ShouldSkipBrush(brush))
                        {
                            Debug.Log("[BrushIconShotHiRez] SKIP template " + brush.DurableName);
                            continue;
                        }
                        if (brush.m_BrushPrefab == null)
                        {
                            Debug.LogError("[BrushIconShotHiRez] SKIP null prefab " + brush.DurableName + " " + brush.m_Guid);
                            continue;
                        }

                        bool isSpray = PrefabHas<SprayBrush>(brush) ||
                            PrefabHas<MidpointPlusLifetimeSprayBrush>(brush);
                        bool isParticle = PrefabHas<GeniusParticlesBrush>(brush);
                        bool isPrint = PrefabHas<Square3DPrintBrush>(brush) ||
                            PrefabHas<PrintableBrush>(brush);
                        bool isHull = IsHullBrush(brush);
                        bool isStrip = IsPaintStripBrush(brush);
                        string prefabType = PrefabBrushType(brush);
                        string shaderName = ShaderName(brush);
                        string blendName = brush.m_BlendMode.ToString();
                        bool backfaces = brush.m_RenderBackfaces;

                        float size = IconBrushSize(brush);
                        float interval = IconSpawnInterval(brush, size, isSpray, isParticle);
                        if (interval < 0.001f)
                        {
                            interval = 0.001f;
                        }

                        float length = kFrameLength;
                        int minKnots = kMinKeptKnots;
                        if (isSpray || isParticle)
                        {
                            minKnots = kDenseKnots;
                        }
                        if (isHull)
                        {
                            minKnots = kHullKnots;
                        }
                        int segments = Mathf.Max(minKnots, Mathf.CeilToInt(length / interval));
                        float margin = kFrameMarginFactor;
                        int settleMs = kSettleMs;
                        bool startFlipped = ShaderNameContains(brush, "SingleSided") &&
                            !backfaces &&
                            !isPrint &&
                            !isHull;
                        Quaternion facing = kPointerFacing;
                        if (isPrint)
                        {
                            facing = kPointerFacingPrint;
                        }
                        else if (startFlipped)
                        {
                            facing = kPointerFacingFlip;
                        }
                        float particleRadius = 0f;
                        float particleFrame = length;
                        if (isParticle)
                        {
                            settleMs = kParticleSettleMs;
                            particleRadius = ParticleCloudRadius(brush, size);
                            float scatter = Mathf.Min(particleRadius, length * 0.5f);
                            particleFrame = length + 2f * scatter;
                            margin = kFrameMarginFactor;
                        }
                        if (isHull)
                        {
                            margin = kHullFrameMarginFactor;
                        }

                        float cameraFrame = isParticle ? particleFrame : length;
                        float cameraDistance = ComputeCameraDistance(cameraFrame, margin);
                        Vector3 origin = new Vector3(-(length / 2f), 100, cameraDistance);
                        PlaceCaptureRig(cam, keyLight, cameraDistance);

                        List<TrTransform> path;
                        if (isHull)
                        {
                            path = GenerateHullPath(length, segments, facing);
                        }
                        else if (!isSpray && !isPrint && UnityEngine.Random.value > 0.5f)
                        {
                            path = GenerateSwoopPath(
                                length, segments, length * kSwoopAmplitudeFactor, facing);
                        }
                        else
                        {
                            path = GenerateStraightPath(length, segments, facing);
                        }

                        bool multi = WantsMultiStroke(brush) && !isParticle && !isHull && !isPrint;
                        PointerManager.m_Instance.SetBrushForAllPointers(brush);
                        PointerManager.m_Instance.MainPointer.BrushSizeAbsolute = size;
                        await Task.Delay(100);
                        if (isParticle)
                        {
                            App.Config.m_ForceDeterministicBirthTimeForExport = false;
                        }
                        strokes = DrawStrokes.DrawNestedTrList(
                            BuildStrokeGroups(path, length, segments, facing, size, multi),
                            TrTransform.T(origin));
                        if (isParticle)
                        {
                            App.Config.m_ForceDeterministicBirthTimeForExport = true;
                        }
                        batchManager.FlushMeshUpdates();
                        if (IsAdditiveBrush(brush) || multi)
                        {
                            BoostIconMaterials(strokes);
                        }
                        await Task.Delay(settleMs);

                        int vertCount = CountStrokeVerts(strokes);
                        int cpCount = CountStrokeControlPoints(strokes);
                        string texName = "";
                        if (brush.Material != null && brush.Material.mainTexture != null)
                        {
                            texName = brush.Material.mainTexture.name;
                        }
                        Debug.LogWarning("[BrushIconShotHiRez] DBG " + brush.DurableName +
                            " guid=" + brush.m_Guid +
                            " prefab=" + prefabType +
                            " shader=" + shaderName +
                            " blend=" + blendName +
                            " backfaces=" + backfaces +
                            " tex=" + texName +
                            " size=" + size.ToString("0.000") +
                            " rate=" + brush.m_ParticleRate.ToString("0.000") +
                            " speed=" + brush.m_ParticleSpeed.ToString("0.000") +
                            " rangeX=" + brush.m_BrushSizeRange.x.ToString("0.000") +
                            " radius=" + particleRadius.ToString("0.000") +
                            " frame=" + cameraFrame.ToString("0.000") +
                            " interval=" + interval.ToString("0.000") +
                            " len=" + length.ToString("0.000") +
                            " segs=" + segments +
                            " cps=" + cpCount +
                            " verts=" + vertCount +
                            " spray=" + isSpray +
                            " particle=" + isParticle +
                            " print=" + isPrint +
                            " hull=" + isHull +
                            " strip=" + isStrip +
                            " multi=" + multi +
                            " camDist=" + cameraDistance.ToString("0.000"));

                        string fileName = "brush-" + brush.DurableName + ".png";
                        float bestLuma = CaptureTimeSamples(
                            cam,
                            strokes,
                            fileName,
                            captureSize,
                            outputSubdir,
                            useEnvironmentPlate);

                        if (bestLuma < kMinIconLuma && !isPrint && !isHull && !startFlipped)
                        {
                            if (strokes != null)
                            {
                                DeleteStrokes(strokes);
                                strokes = null;
                            }
                            facing = kPointerFacingFlip;
                            path = isSpray || isParticle
                                ? GenerateStraightPath(length, segments, facing)
                                : GenerateSwoopPath(
                                    length, segments, length * kSwoopAmplitudeFactor, facing);
                            if (isParticle)
                            {
                                App.Config.m_ForceDeterministicBirthTimeForExport = false;
                            }
                            strokes = DrawStrokes.DrawNestedTrList(
                                BuildStrokeGroups(path, length, segments, facing, size, multi),
                                TrTransform.T(origin));
                            if (isParticle)
                            {
                                App.Config.m_ForceDeterministicBirthTimeForExport = true;
                            }
                            batchManager.FlushMeshUpdates();
                            if (IsAdditiveBrush(brush) || multi)
                            {
                                BoostIconMaterials(strokes);
                            }
                            await Task.Delay(settleMs);
                            vertCount = CountStrokeVerts(strokes);
                            cpCount = CountStrokeControlPoints(strokes);
                            float flipLuma = CaptureTimeSamples(
                                cam,
                                strokes,
                                fileName,
                                captureSize,
                                outputSubdir,
                                useEnvironmentPlate);
                            Debug.LogWarning("[BrushIconShotHiRez] FLIP " + brush.DurableName +
                                " prefab=" + prefabType +
                                " shader=" + shaderName +
                                " first=" + bestLuma.ToString("0.000") +
                                " flip=" + flipLuma.ToString("0.000") +
                                " verts=" + vertCount);
                            if (flipLuma > bestLuma)
                            {
                                bestLuma = flipLuma;
                            }
                        }

                        if (bestLuma < kMinIconLuma)
                        {
                            Debug.LogWarning("[BrushIconShotHiRez] LOW LUMA " +
                                brush.DurableName + " " + brush.m_Guid +
                                " prefab=" + prefabType +
                                " shader=" + shaderName +
                                " blend=" + blendName +
                                " backfaces=" + backfaces +
                                " luma=" + bestLuma.ToString("0.000") +
                                " verts=" + vertCount +
                                " cps=" + cpCount);
                        }
                    }
                    catch (Exception e)
                    {
                        string name = brush != null ? brush.DurableName : "null";
                        string guid = brush != null ? brush.m_Guid.ToString() : "null";
                        Debug.LogError("[BrushIconShotHiRez] SKIP " + name + " " + guid + " : " + e.Message);
                    }
                    finally
                    {
                        if (strokes != null)
                        {
                            DeleteStrokes(strokes);
                        }
                        var restoreBatchManager = App.Scene.ActiveCanvas.BatchManager;
                        restoreBatchManager.OneStrokePerBatch = wasOneStrokePerBatch;
                    }
                }
            }
            finally
            {
                App.Config.m_ForceDeterministicBirthTimeForExport = wasForceDeterministicBirthTimeForExport;
                RenderSettings.ambientLight = previousAmbient;
                RenderSettings.ambientIntensity = previousAmbientIntensity;
                RenderSettings.ambientMode = previousAmbientMode;
                if (keyLight != null)
                {
                    Destroy(keyLight.gameObject);
                }
                if (cam != null)
                {
                    Destroy(cam.gameObject);
                }
            }

            Debug.Log("[BrushIconShotHiRez] Done. Output: " + kScreenshotOutputDirectory + "/" + outputSubdir);
            EditorApplication.isPlaying = false;
        }

        static BrushIconShotConfig LoadShotConfig()
        {
            string[] guids = AssetDatabase.FindAssets("t:BrushIconShotConfig");
            if (guids == null || guids.Length == 0)
            {
                Debug.LogWarning("[BrushIconShotHiRez] No BrushIconShotConfig asset. Using VideoBlack + Blue.");
                return null;
            }
            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            BrushIconShotConfig config = AssetDatabase.LoadAssetAtPath<BrushIconShotConfig>(path);
            if (config == null)
            {
                Debug.LogWarning("[BrushIconShotHiRez] Failed to load " + path);
            }
            return config;
        }

        private static Camera CreateCaptureCamera(bool useEnvironmentPlate)
        {
            GameObject go = new GameObject("BrushIconCaptureCamera");
            Camera cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.stereoTargetEye = StereoTargetEyeMask.None;
            ApplyPlateToCamera(cam, useEnvironmentPlate);
            cam.fieldOfView = kCaptureFov;
            cam.aspect = 1f;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 100f;
            cam.allowHDR = true;
            cam.allowMSAA = true;
            cam.cullingMask = ~0;
            cam.transform.position = new Vector3(0, 100, 0);
            cam.transform.rotation = Quaternion.identity;
            return cam;
        }

        static void PlaceCaptureRig(Camera cam, Light keyLight, float cameraDistance)
        {
            TrTransform canvasPose = App.Scene.ActiveCanvas != null
                ? App.Scene.ActiveCanvas.Pose
                : TrTransform.identity;
            Vector3 camCanvas = new Vector3(0f, 100f, 0f);
            Vector3 worldPos = canvasPose * camCanvas;
            Quaternion worldRot = canvasPose.rotation;
            cam.transform.position = worldPos;
            cam.transform.rotation = worldRot;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = Mathf.Max(100f, cameraDistance * 3f + 10f);
            if (keyLight != null)
            {
                keyLight.transform.position = worldPos;
                keyLight.transform.rotation = worldRot;
            }
        }

        static int CountStrokeVerts(IEnumerable<Stroke> strokes)
        {
            if (strokes == null)
            {
                return 0;
            }
            int total = 0;
            foreach (var stroke in strokes)
            {
                if (stroke == null)
                {
                    continue;
                }
                if (stroke.m_Type == Stroke.Type.BrushStroke && stroke.m_Object != null)
                {
                    BaseBrushScript script = stroke.m_Object.GetComponent<BaseBrushScript>();
                    if (script != null)
                    {
                        total += script.GetNumUsedVerts();
                    }
                }
                else if (stroke.m_Type == Stroke.Type.BatchedBrushStroke &&
                    stroke.m_BatchSubset != null)
                {
                    // Batched path: 1 means geo exists, exact subset vert count is on BatchSubset.
                    total += 1;
                }
            }
            return total;
        }

        static int CountStrokeControlPoints(IEnumerable<Stroke> strokes)
        {
            if (strokes == null)
            {
                return 0;
            }
            int total = 0;
            foreach (var stroke in strokes)
            {
                if (stroke != null && stroke.m_ControlPoints != null)
                {
                    total += stroke.m_ControlPoints.Length;
                }
            }
            return total;
        }

        static Light CreateCaptureKeyLight(Camera cam)
        {
            GameObject go = new GameObject("BrushIconKeyLight");
            Light light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = Color.white;
            light.intensity = 1.25f;
            light.shadows = LightShadows.None;
            light.cookie = null;
            go.transform.position = cam.transform.position;
            go.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            return light;
        }

        static void ApplyPlateToCamera(Camera cam, bool useEnvironmentPlate)
        {
            if (useEnvironmentPlate)
            {
                cam.clearFlags = CameraClearFlags.Skybox;
                Environment env = SceneSettings.m_Instance != null
                    ? SceneSettings.m_Instance.CurrentEnvironment
                    : null;
                if (env != null)
                {
                    cam.backgroundColor = env.m_RenderSettings.m_ClearColor;
                }
            }
            else
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
            }
        }

        static bool IsAdditiveBrush(BrushDescriptor brush)
        {
            if (brush == null)
            {
                return false;
            }
            return brush.m_BlendMode == ExportableMaterialBlendMode.AdditiveBlend ||
                brush.m_UseBloomSwatchOnColorPicker;
        }

        static void BoostIconMaterials(IEnumerable<Stroke> strokes)
        {
            if (strokes == null)
            {
                return;
            }
            foreach (var stroke in strokes)
            {
                try
                {
                    if (stroke.m_BatchSubset == null ||
                        stroke.m_BatchSubset.m_ParentBatch == null)
                    {
                        continue;
                    }
                    var material = stroke.m_BatchSubset.m_ParentBatch.InstantiatedMaterial;
                    if (material == null)
                    {
                        continue;
                    }
                    if (material.HasFloat("_EmissionGain"))
                    {
                        float gain = material.GetFloat("_EmissionGain");
                        if (gain < 0.8f)
                        {
                            stroke.SetShaderFloat("_EmissionGain", 0.8f);
                        }
                    }
                    if (material.HasFloat("_Opacity"))
                    {
                        stroke.SetShaderFloat("_Opacity", 1f);
                    }
                    // AUDIO_REACTIVE left off. Dummy beat shifted Hypercolor pink
                    // and zeroed DanceFloor (fmod(accum,1) == 0).
                }
                catch (StrokeShaderModifierException)
                {
                }
                catch (Exception)
                {
                }
            }
        }

        private static void SetFixedShaderTime(IEnumerable<Stroke> strokes, float time)
        {
            if (strokes == null)
            {
                return;
            }
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

        static float CaptureTimeSamples(
            Camera cam,
            IEnumerable<Stroke> strokes,
            string fileName,
            int captureSize,
            string outputSubdir,
            bool useEnvironmentPlate)
        {
            var flush = App.Scene.ActiveCanvas.BatchManager;
            float bestLuma = -1f;
            for (int t = 0; t < kTimeSamples.Length; ++t)
            {
                SetFixedShaderTime(strokes, kTimeSamples[t]);
                flush.FlushMeshUpdates();
                bool writePng = (bestLuma < 0f);
                float luma = SaveCurrentView(
                    cam,
                    fileName,
                    captureSize,
                    captureSize,
                    outputSubdir,
                    useEnvironmentPlate,
                    writePng);
                if (luma > bestLuma)
                {
                    bestLuma = luma;
                    if (!writePng)
                    {
                        SaveCurrentView(
                            cam,
                            fileName,
                            captureSize,
                            captureSize,
                            outputSubdir,
                            useEnvironmentPlate,
                            true);
                    }
                }
                if (luma >= kMinIconLuma)
                {
                    break;
                }
            }
            return bestLuma;
        }

        static float SaveCurrentView(
            Camera cameraToCapture,
            string fileName,
            int resWidth,
            int resHeight,
            string relativeSubdir,
            bool useEnvironmentPlate,
            bool writePng = true)
        {
            int renderWidth = resWidth * kScreenshotSupersampling;
            int renderHeight = resHeight * kScreenshotSupersampling;
            RenderTexture rt = new RenderTexture(renderWidth, renderHeight, 24, RenderTextureFormat.ARGBHalf)
            {
                antiAliasing = kScreenshotMsaaSamples,
                filterMode = FilterMode.Bilinear
            };
            RenderTexture downsampledRt = new RenderTexture(resWidth, resHeight, 0, RenderTextureFormat.ARGBHalf)
            {
                filterMode = FilterMode.Bilinear
            };
            Texture2D screenShot = null;
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = cameraToCapture.targetTexture;
            bool previousAllowMsaa = cameraToCapture.allowMSAA;
            float avgLuma = 0f;
            try
            {
                ApplyPlateToCamera(cameraToCapture, useEnvironmentPlate);
                cameraToCapture.allowMSAA = true;
                cameraToCapture.allowHDR = true;
                cameraToCapture.targetTexture = rt;
                screenShot = new Texture2D(resWidth, resHeight, TextureFormat.RGB24, false);
                Shader.EnableKeyword("HDR_SIMPLE");
                cameraToCapture.Render();
                Shader.DisableKeyword("HDR_SIMPLE");
                Graphics.Blit(rt, downsampledRt);
                RenderTexture.active = downsampledRt;
                screenShot.ReadPixels(new Rect(0, 0, resWidth, resHeight), 0, 0);
                screenShot.Apply(false, false);

                Color32[] pixels = screenShot.GetPixels32();
                double sum = 0;
                for (int i = 0; i < pixels.Length; ++i)
                {
                    sum += (0.299 * pixels[i].r + 0.587 * pixels[i].g + 0.114 * pixels[i].b) / 255.0;
                }
                avgLuma = pixels.Length > 0 ? (float)(sum / pixels.Length) : 0f;

                if (writePng)
                {
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
            }
            finally
            {
                Shader.DisableKeyword("HDR_SIMPLE");
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
            return avgLuma;
        }
    } // functions complete

} // Namespace Tilt Brush


