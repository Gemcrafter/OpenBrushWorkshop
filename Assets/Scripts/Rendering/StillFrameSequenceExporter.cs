// Copyright 2025 The Open Brush Authors
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

///  LINE 251 approximately controls the ffmpeg format
///  something like ffmpeg ... -c:v libx264 -pix_fmt yuv420p ... (search libx264)
///  we want to explore increasing versatilty
/// currently caleld by Offline47 in CameraPathCaptureRig
/// Real coding happens here
/// VideoRecorderUtils.GeneratePreviewVideoFromFrames(...), which is called from Offline47

using System.IO;
using UnityEngine;

namespace TiltBrush
{
    public class StillFrameSequenceExporter : MonoBehaviour
    {
        private string m_FilePath;
        private string m_BaseFileName;
        private string m_DirectoryPath;
        private int m_FrameCount;
        private float m_FPS;
        private bool m_IsCapturing;
        private bool m_IsSaving;
        private ScreenshotManager m_ScreenshotManager;
        private float m_LastCaptureTime;
        private float m_FrameInterval;

        // TEMP DIAGNOSTIC — REMOVE AFTER INVESTIGATION (ShouldCapture boundary trace)
        private int m_ShouldCaptureDiagCallCount = 0;

        public string FilePath => m_FilePath;

        // StillFrameSequenceExporter.IsCapturing
        public bool IsCapturing => m_IsCapturing;


        // TIMING DIAGNOSTICS
        // Update 1008 - Analytics tracking (still-frame only)
        private int m_CaptureAttempts;
        private int m_CaptureSuccesses;
        private float m_TotalCaptureTime;
        private float m_MaxCaptureTime;
        private System.Diagnostics.Stopwatch m_CaptureStopwatch;


        // Update 1006 StillFrameSequenceExporter.FrameCount
        // Purpose: Expose the actual number of frames written so VideoRecorderUtils
        // can use it as the authoritative source for stopping at the exact target count.
        public int FrameCount => m_FrameCount;

        // Determine whether to create a frames folder (applies only to online recording)
        private bool m_AppendFramesSuffix = true;

        // Sidecar data information
        private int m_TargetWidth;
        private int m_TargetHeight;

        // Exposed so callers can log the actual resolution a frame was captured at
        // without re-deriving the override/OfflineResolution/Resolution priority themselves.
        // Refreshed on every CaptureFrame() call (not just at StartCapture()), so these
        // always reflect the resolution actually used for the most recent captured frame.
        public int TargetWidth => m_TargetWidth;
        public int TargetHeight => m_TargetHeight;


        private void Awake()
        {
            m_ScreenshotManager = GetComponent<ScreenshotManager>();
            if (m_ScreenshotManager == null)
            {
                Debug.LogError("StillFrameSequenceExporter requires a ScreenshotManager component");
            }
        }

        public bool StartCapture(string filePath, float fps, bool appendFramesSuffix = true)
        {
            if (m_IsCapturing)
            {
                return true;
            }

            m_FilePath = filePath;
            m_BaseFileName = Path.GetFileNameWithoutExtension(filePath);
            m_AppendFramesSuffix = appendFramesSuffix;

            string baseDir = Path.GetDirectoryName(filePath);
            if (appendFramesSuffix)
            {
                m_DirectoryPath = Path.Combine(baseDir, m_BaseFileName + "_frames");
            }
            else
            {
                m_DirectoryPath = filePath;
            }

            m_FPS = fps;
            m_FrameCount = 0;
            m_FrameInterval = 1.0f / fps;
            m_LastCaptureTime = 0f;

            // === Determine actual capture resolution (priority order matches CaptureFrame) ===
            if (CameraPathCaptureRig.m_Instance != null &&
                CameraPathCaptureRig.m_Instance.captureWidthOverride > 0 &&
                CameraPathCaptureRig.m_Instance.captureHeightOverride > 0)
            {
                m_TargetWidth = CameraPathCaptureRig.m_Instance.captureWidthOverride;
                m_TargetHeight = CameraPathCaptureRig.m_Instance.captureHeightOverride;
            }
            else if (App.UserConfig.Video.OfflineResolution > 0)
            {
                m_TargetWidth = App.UserConfig.Video.OfflineResolution;
                m_TargetHeight = (App.UserConfig.Video.OfflineResolution * 9) / 16;
            }
            else
            {
                m_TargetWidth = App.UserConfig.Video.Resolution;
                m_TargetHeight = (App.UserConfig.Video.Resolution * 9) / 16;
            }

            if (!FileUtils.InitializeDirectoryWithUserError(
                m_DirectoryPath,
                "Failed to start still frame sequence capture"))
            {
                return false;
            }

            m_IsCapturing = true;
            m_IsSaving = false;
            m_CaptureAttempts = 0;
            m_CaptureSuccesses = 0;
            m_TotalCaptureTime = 0f;
            m_MaxCaptureTime = 0f;
            m_CaptureStopwatch = new System.Diagnostics.Stopwatch();

            CreateMetadataFile();
            return true;
        }


        // Should Fix Rounding error that is discarding frames
        public bool ShouldCapture(float currentTime)
        {
            if (!m_IsCapturing)
            {
                return false;
            }

            // Epsilon absorbs the double->float representation error that was
            // causing roughly every other calibration sample to be rejected.
            // 1e-5 is ~0.06% of one frame at 60 fps - large enough for the
            // observed 1e-9..1e-8 noise, far too small to accept a genuinely
            // early frame.
            const float kCaptureEpsilon = 1e-5f;

            bool result = currentTime + kCaptureEpsilon >= m_LastCaptureTime + m_FrameInterval;

            // TEMP DIAGNOSTIC — REMOVE AFTER INVESTIGATION
            if (m_ShouldCaptureDiagCallCount < 10)
            {
                m_ShouldCaptureDiagCallCount++;
                float threshold = m_LastCaptureTime + m_FrameInterval;
                float diff = currentTime - threshold;
                Debug.LogError(
                    $"[ShouldCaptureDiag] call={m_ShouldCaptureDiagCallCount} " +
                    $"currentTime(float)={currentTime:F10} " +
                    $"m_LastCaptureTime(float)={m_LastCaptureTime:F10} " +
                    $"m_FrameInterval(float)={m_FrameInterval:F10} " +
                    $"threshold=lastCapture+interval(float)={threshold:F10} " +
                    $"diff=currentTime-threshold(float)={diff:F10} " +
                    $"result={result}");
            }
            // END TEMP DIAGNOSTIC

            return result;
        }

        /*
        public bool ShouldCapture(float currentTime)
        {
            if (!m_IsCapturing)
            {
                return false;
            }

            bool result = currentTime >= m_LastCaptureTime + m_FrameInterval;

            // TEMP DIAGNOSTIC — REMOVE AFTER INVESTIGATION
            if (m_ShouldCaptureDiagCallCount < 10)
            {
                m_ShouldCaptureDiagCallCount++;
                float threshold = m_LastCaptureTime + m_FrameInterval;
                float diff = currentTime - threshold;
                Debug.LogError(
                    $"[ShouldCaptureDiag] call={m_ShouldCaptureDiagCallCount} " +
                    $"currentTime(float)={currentTime:F10} " +
                    $"m_LastCaptureTime(float)={m_LastCaptureTime:F10} " +
                    $"m_FrameInterval(float)={m_FrameInterval:F10} " +
                    $"threshold=lastCapture+interval(float)={threshold:F10} " +
                    $"diff=currentTime-threshold(float)={diff:F10} " +
                    $"result={result}");
            }
            // END TEMP DIAGNOSTIC

            return result;
        }
        */



        // Update 3000 StillFrameSequenceExporter.CaptureFrame
        // Replaced hard-coded use of App.UserConfig.Video.Resolution with prioritized resolution selection.
        // Priority order:
        //   1. CameraPathCaptureRig.captureWidthOverride / captureHeightOverride (if both > 0)
        //   2. App.UserConfig.Video.OfflineResolution (from Open Brush.cfg)
        //   3. App.UserConfig.Video.Resolution (original fallback)
        // Added clear Debug.LogError on first frame so it is obvious which resolution source is active.
        // This enables proper 4K (or other custom resolution) testing during still-frame recording.

        // === UPDATED SIGNATURE WITH OPTIONAL OVERRIDE PARAMETER ===
        // Option to Run Pure Black Frames at end of sequence on second pass approach

        public void CaptureFrame(float currentTime, bool forcePureBlack = false)
        {
            m_CaptureAttempts++;

            if (m_ScreenshotManager == null)
            {
                Debug.LogError("[StillFrame] ERROR: m_ScreenshotManager is NULL inside CaptureFrame!");
                return;
            }

            if (!m_IsCapturing || !ShouldCapture(currentTime))
            {
                return;
            }

            m_CaptureStopwatch.Restart();
            m_LastCaptureTime = currentTime;

            string frameFileName = string.Format($"{m_BaseFileName}_frame_{m_FrameCount + 1:D6}.{FilenameExtension}");
            string frameFilePath = Path.Combine(m_DirectoryPath, frameFileName);

            int targetWidth;
            int targetHeight;

            if (CameraPathCaptureRig.m_Instance != null &&
                CameraPathCaptureRig.m_Instance.captureWidthOverride > 0 &&
                CameraPathCaptureRig.m_Instance.captureHeightOverride > 0)
            {
                targetWidth = CameraPathCaptureRig.m_Instance.captureWidthOverride;
                targetHeight = CameraPathCaptureRig.m_Instance.captureHeightOverride;
            }
            else if (App.UserConfig.Video.OfflineResolution > 0)
            {
                targetWidth = App.UserConfig.Video.OfflineResolution;
                targetHeight = (App.UserConfig.Video.OfflineResolution * 9) / 16;
            }
            else
            {
                targetWidth = App.UserConfig.Video.Resolution;
                targetHeight = (App.UserConfig.Video.Resolution * 9) / 16;
            }

            // Keep m_TargetWidth/m_TargetHeight (and the public TargetWidth/TargetHeight
            // accessors) in sync with what this specific frame actually used, rather than
            // only reflecting whatever was true back when StartCapture() ran.
            m_TargetWidth = targetWidth;
            m_TargetHeight = targetHeight;

            RenderTexture renderTexture = m_ScreenshotManager.CreateTemporaryTargetForSave(targetWidth, targetHeight);

            try
            {
                // === NATIVE ANTI-ALIASED BLACK FRAME BUFFER INJECTION ===
                if (forcePureBlack)
                {
                    // Activate the temporary target render texture
                    RenderTexture activeTarget = RenderTexture.active;
                    RenderTexture.active = renderTexture;

                    // Clear the active buffer context completely to pure, solid black (0,0,0,1)
                    // This completely bypasses your drawing lines and strokes, 
                    // but preserves your target's exact color space, depth parameters, and anti-aliasing profile!
                    GL.Clear(true, true, Color.black);

                    // Restore original active rendering state context
                    RenderTexture.active = activeTarget;

                    // Process the zeroed-out image texture into a standard PNG sequence file
                    byte[] frameData = ScreenshotManager.SaveToMemory(renderTexture, UsePng);
                    File.WriteAllBytes(frameFilePath, frameData);
                }
                else
                {
                    // Standard baseline camera workspace viewport snapshot path
                    m_ScreenshotManager.RenderToTexture(renderTexture);
                    byte[] frameData = ScreenshotManager.SaveToMemory(renderTexture, UsePng);
                    File.WriteAllBytes(frameFilePath, frameData);
                }

                m_FrameCount++;
                m_CaptureSuccesses++;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to capture frame {m_FrameCount}: {e.Message}");
            }
            finally
            {
                RenderTexture.ReleaseTemporary(renderTexture);
                m_CaptureStopwatch.Stop();
                float captureTime = (float)m_CaptureStopwatch.Elapsed.TotalMilliseconds;
                m_TotalCaptureTime += captureTime;
                if (captureTime > m_MaxCaptureTime) m_MaxCaptureTime = captureTime;
            }
        }





        public string FilenameExtension => UsePng ? "png" : "jpg";
        public bool UsePng => App.UserConfig.Video.UsePngForFrameSequence;

        public void StopCapture(bool save)
        {
            if (!m_IsCapturing) return;

            m_IsCapturing = false;

            if (save)
            {
                m_IsSaving = true;
                UpdateMetadataFile();
                m_IsSaving = false;

                // Update 1008 - Clean summary analytics (still-frame capture only)
                if (m_CaptureAttempts > 0)
                {
                    float avgTime = m_CaptureSuccesses > 0 ? m_TotalCaptureTime / m_CaptureSuccesses : 0f;
                    float skipRate = (float)(m_CaptureAttempts - m_CaptureSuccesses) / m_CaptureAttempts * 100f;

                    Debug.LogError("[StillFrame] Capture Analytics Summary\n" +
                        "Total capture attempts: " + m_CaptureAttempts + "\n" +
                        "Successful frames written: " + m_CaptureSuccesses + "\n" +
                        "Skipped frames: " + (m_CaptureAttempts - m_CaptureSuccesses) + " (" + skipRate.ToString("F1") + "%)\n" +
                        "Average time per capture: " + avgTime.ToString("F2") + " ms\n" +
                        "Max time per capture: " + m_MaxCaptureTime.ToString("F2") + " ms");
                }
            }
            else
            {
                DeleteFrameSequence();
            }
        }


        private void CreateMetadataFile()
        {
            string baseDir = Path.GetDirectoryName(m_FilePath);
            string suffix = m_AppendFramesSuffix ? "_sequence.txt" : "_Offline_sequence.txt";
            string metadataPath = Path.Combine(baseDir, m_BaseFileName + suffix);

            try
            {
                using (StreamWriter writer = new StreamWriter(metadataPath))
                {
                    writer.WriteLine("Open Brush Camera Path Frame Sequence");
                    writer.WriteLine($"Base Name: {m_BaseFileName}");
                    writer.WriteLine($"Frame Rate: {m_FPS} fps");
                    writer.WriteLine($"Format: {FilenameExtension}");
                    writer.WriteLine($"Resolution: {m_TargetWidth}x{m_TargetHeight}");
                    writer.WriteLine($"Start Time: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    writer.WriteLine("Status: Recording");
                    writer.WriteLine("");

                    if (m_AppendFramesSuffix)
                    {
                        writer.WriteLine("To convert to video, use a tool like ffmpeg:");
                        writer.WriteLine($"ffmpeg -r {m_FPS} -i \"{m_BaseFileName}_frame_%06d.{FilenameExtension}\" -c:v libx264 -pix_fmt yuv420p \"../{m_BaseFileName}.mp4\"");
                    }
                    else
                    {
                        writer.WriteLine("Offline second pass - frames are in this folder.");
                        writer.WriteLine($"ffmpeg -r {m_FPS} -i \"{m_BaseFileName}_frame_%06d.{FilenameExtension}\" -c:v libx264 -pix_fmt yuv420p \"{m_BaseFileName}.mp4\"");
                    }

                    writer.WriteLine("");
                    writer.WriteLine("(Run this command from inside the frames folder, or adjust paths accordingly)");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Failed to create metadata file: {e.Message}");
            }
        }


        // This version deliberately overshoots so animation is preserved in audioreactive
        // Update 1007 StillFrameSequenceExporter.DuplicateLastFrames
        // Purpose: Duplicate the last frame the specified number of times to reach the exact target count.
        // Optimized with a live hardware render fallback for two-pass offline runs to prevent audio-reactive freezes.
        public void DuplicateLastFrames(int count)
        {
            if (count <= 0 || m_FrameCount == 0) return;

            // 1. OFFLINE RENDERING FALLBACK GATE
            // If we are running an offline two-pass sequence, do not perform raw disk file cloning.
            // Force the system to execute a live camera viewport snapshot pass so that your un-clamped
            // audio textures and rolling wave properties continue animating seamlessly to the final frame.
            if (VisualizerManager.m_Instance != null && VisualizerManager.m_Instance.IsPlaybackModeActive)
            {
                if (m_ScreenshotManager == null)
                {
                    Debug.LogError("[StillFrame] ERROR: m_ScreenshotManager is NULL inside DuplicateLastFrames fallback pass!");
                    return;
                }

                // Render the exact number of required catch-up frames as unique graphic iterations
                for (int i = 0; i < count; i++)
                {
                    string newFrameName = string.Format($"{m_BaseFileName}_frame_{m_FrameCount + 1:D6}.{FilenameExtension}");
                    string newFramePath = Path.Combine(m_DirectoryPath, newFrameName);

                    // Pull current dimensions from our active settings allocations
                    RenderTexture renderTexture = m_ScreenshotManager.CreateTemporaryTargetForSave(m_TargetWidth, m_TargetHeight);
                    try
                    {
                        // Drive a live rendering pass instead of cloning an existing file matrix
                        m_ScreenshotManager.RenderToTexture(renderTexture);
                        byte[] frameData = ScreenshotManager.SaveToMemory(renderTexture, UsePng);
                        File.WriteAllBytes(newFramePath, frameData);

                        m_FrameCount++;
                        m_CaptureSuccesses++;
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"Failed fallback capture frame during catch-up sequence {m_FrameCount}: {e.Message}");
                    }
                    finally
                    {
                        RenderTexture.ReleaseTemporary(renderTexture);
                    }
                }

                UpdateMetadataFile();
                return;
            }

            // 2. STANDARD IN-GAME RECORDING PATH (UNTOUCHED)
            // Maintains your original, performance-optimized binary file cloning for standard baseline recordings.
            string lastFrameName = string.Format($"{m_BaseFileName}_frame_{m_FrameCount:D6}.{FilenameExtension}");
            string lastFramePath = Path.Combine(m_DirectoryPath, lastFrameName);
            if (!File.Exists(lastFramePath)) return;

            byte[] lastFrameData = File.ReadAllBytes(lastFramePath);
            for (int i = 0; i < count; i++)
            {
                m_FrameCount++;
                string newFrameName = string.Format($"{m_BaseFileName}_frame_{m_FrameCount:D6}.{FilenameExtension}");
                string newFramePath = Path.Combine(m_DirectoryPath, newFrameName);
                File.WriteAllBytes(newFramePath, lastFrameData);
            }

            UpdateMetadataFile();
        }



        // Update 1007 StillFrameSequenceExporter.RemoveLastFrames
        // Purpose: Remove the last N frames if we exceeded the target count.
        public void RemoveLastFrames(int count)
        {
            if (count <= 0) return;

            for (int i = 0; i < count; i++)
            {
                if (m_FrameCount <= 0) break;

                string frameName = string.Format($"{m_BaseFileName}_frame_{m_FrameCount:D6}.{FilenameExtension}");
                string framePath = Path.Combine(m_DirectoryPath, frameName);

                if (File.Exists(framePath))
                {
                    File.Delete(framePath);
                }

                m_FrameCount--;
            }

            UpdateMetadataFile();
        }

        private void UpdateMetadataFile()
        {
            string baseDir = Path.GetDirectoryName(m_FilePath);
            string suffix = m_AppendFramesSuffix ? "_sequence.txt" : "_Offline_sequence.txt";
            string metadataPath = Path.Combine(baseDir, m_BaseFileName + suffix);

            try
            {
                string content = File.ReadAllText(metadataPath);
                content = content.Replace("Status: Recording", $"Status: Complete ({m_FrameCount} frames)");
                content = content.Replace("Start Time:", $"End Time: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}\nStart Time:");
                File.WriteAllText(metadataPath, content);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Failed to update metadata file: {e.Message}");
            }
        }


        private void DeleteFrameSequence()
        {
            try
            {
                // Delete all frame files
                for (int i = 1; i <= m_FrameCount; i++)
                {
                    string frameFileName = string.Format($"{m_BaseFileName}_frame_{i:D6}.{FilenameExtension}");
                    string frameFilePath = Path.Combine(m_DirectoryPath, frameFileName);
                    if (File.Exists(frameFilePath))
                    {
                        File.Delete(frameFilePath);
                    }
                }

                // Delete the correct metadata file based on pass type
                string baseDir = Path.GetDirectoryName(m_FilePath);
                string suffix = m_AppendFramesSuffix ? "_sequence.txt" : "_Offline_sequence.txt";
                string metadataPath = Path.Combine(baseDir, m_BaseFileName + suffix);

                if (File.Exists(metadataPath))
                {
                    File.Delete(metadataPath);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Failed to delete frame sequence: {e.Message}");
            }
        }

        /// <summary>
        /// Explicitly sets the ScreenshotManager reference.
        /// This is used by the offline capture path (CameraPathCaptureRig) to guarantee
        /// the correct ScreenshotManager is used, bypassing any Awake()/GetComponent timing issues.
        /// Normal recording paths are unaffected.
        /// </summary>
        public void SetScreenshotManager(ScreenshotManager manager)
        {
            if (manager != null)
            {
                m_ScreenshotManager = manager;
                Debug.LogError("[StillFrameSequenceExporter] ScreenshotManager was explicitly assigned (offline path).");
            }
            else
            {
                Debug.LogError("[StillFrameSequenceExporter] SetScreenshotManager() received NULL.");
            }
        }




    }
} // end script

