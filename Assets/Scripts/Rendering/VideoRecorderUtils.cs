// Copyright 2020 The Tilt Brush Authors
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
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;



namespace TiltBrush
{

    static public class VideoRecorderUtils
    {
        static private float m_VideoCaptureResolutionScale = 1.0f;
        static private int m_DebugVideoCaptureQualityLevel = -1;
        static private int m_PreCaptureQualityLevel = -1;

        // Update 1000
        static private int m_TargetFrameCount = -1;
        static private int m_CurrentFrameCount = 0;

        // Two-pass offline visualizer sync flag (Reserved strictly for Headless Offline HQ Renders)
        static private bool m_IsVisualizerPlaybackPass = false;

        // Dedicated Live-Pass Sync Flag (NEW: Separates interactive camera rig runs from offline traps)
        static private bool m_IsLiveAudioCaptureActive = false;

        // Public accessor to allow AudioCaptureManager to read the state safely
        static public bool IsLiveAudioCaptureActive => m_IsLiveAudioCaptureActive;

        /// <summary>
        /// Explicitly switches the visualizer's playback state before a pass begins.
        /// Call this from CameraPathCaptureRig step sequences.
        /// </summary>
        static public void SetVisualizerPlaybackPass(bool isPlayback)
        {
            // Explicit error log to trace the exact moment the background orchestrator switches passes
            Debug.LogError($"[VideoRecorderUtils.SetVisualizerPlaybackPass] Switching Two-Pass State. New State (IsPlayback): {isPlayback}");

            m_IsVisualizerPlaybackPass = isPlayback;
            if (VisualizerManager.m_Instance != null)
            {
                VisualizerManager.m_Instance.SetPlaybackMode(isPlayback);
            }
        }

        /// <summary>
        /// Explicitly flags whether the current two-pass run should actively record live hardware mixer streams.
        /// </summary>
        static public void SetLiveAudioCaptureActive(bool isActive)
        {
            Debug.LogError($"[VideoRecorderUtils.SetLiveAudioCaptureActive] Live audio recording capture state toggled: {isActive}");
            m_IsLiveAudioCaptureActive = isActive;
        }




        // [Range(RenderWrapper.SSAA_MIN, RenderWrapper.SSAA_MAX)]
        static private float m_SuperSampling = 2.0f;
        static private float m_PreCaptureSuperSampling = 1.0f;

        static internal UsdPathSerializer m_UsdPathSerializer;

        static private System.Diagnostics.Stopwatch m_RecordingStopwatch;
        static private string m_UsdPath;

        // === NEW PUBLIC PROPERTY ACCESSOR FOR CAMERAPATHCAPTURERIG SIMULATOR SYNC ===
        static public string UsdPath
        {
            get { return m_UsdPath; }
        }

        static private VideoRecorder m_ActiveVideoRecording;



        static private StillFrameSequenceExporter m_ActiveStillFrameExporter;
        static private bool m_UsingStillFrameFallback = false;

        static public VideoRecorder ActiveVideoRecording
        {
            get { return m_ActiveVideoRecording; }
        }

        static public StillFrameSequenceExporter ActiveStillFrameExporter
        {
            get { return m_ActiveStillFrameExporter; }
        }

        static public bool IsUsingStillFrameFallback
        {
            get { return m_UsingStillFrameFallback; }
        }

        static public int NumFramesInUsdSerializer
        {
            get
            {
                if (m_UsdPathSerializer != null && !m_UsdPathSerializer.IsRecording)
                {
                    return Mathf.CeilToInt((float)m_UsdPathSerializer.Duration *
                        (int)m_ActiveVideoRecording.FPS);
                }
                return 0;
            }
        }

        static public bool UsdPathSerializerIsBlocking
        {
            get
            {
                return (m_UsdPathSerializer != null &&
                    !m_UsdPathSerializer.IsRecording &&
                    !m_UsdPathSerializer.IsFinished);
            }
        }

        static public bool UsdPathIsFinished
        {
            get
            {
                return (m_UsdPathSerializer != null && m_UsdPathSerializer.IsFinished);
            }
        }

        static public Transform AdvanceAndDeserializeUsd()
        {
            if (m_UsdPathSerializer != null)
            {
                m_UsdPathSerializer.Time += Time.deltaTime;
                m_UsdPathSerializer.Deserialize();
                return m_UsdPathSerializer.transform;
            }
            return null;
        }

        /// <summary>
        /// Generates a high-quality preview video from a folder of still frames using ffmpeg.
        /// 
        /// This method works with both the old naming convention (folders ending in "_frames")
        /// and the new flat folder structure used by the offline second pass.
        /// 
        /// It automatically derives the base filename from the frames folder:
        /// - If the folder ends with "_frames", the suffix is removed for the output video name.
        /// - Otherwise, the folder name is used directly.
        /// 
        /// Uses -vsync 0 to guarantee no frames are dropped or duplicated.
        /// </summary>
        /// <summary>
        /// VideoRecorderUtils.GeneratePreviewVideoFromFrames
        /// Generates a high-quality preview video from a folder of still frames using ffmpeg.
        /// Automatically handles multiplexing and high-fidelity synchronization of a paired 
        /// uncompressed .wav audio track if discovered in the output folder.
        /// </summary>


        /// <summary>
        /// VideoRecorderUtils.GeneratePreviewVideoFromFrames
        /// Generates a high-quality preview video from a folder of still frames using ffmpeg.
        /// Automatically handles multiplexing (combining) a paired .wav into the .mp4 when present.
        /// Phase 4: logs frame count, expected video duration, WAV presence, and estimated WAV duration
        /// before running ffmpeg so A/V length can be verified in the console.
        /// </summary>
        public static void GeneratePreviewVideoFromFrames(string framesFolderPath, int framerate, int crf, string preset)
        {
            if (string.IsNullOrEmpty(framesFolderPath) || !Directory.Exists(framesFolderPath))
            {
                Debug.LogError("[VideoRecorderUtils.GeneratePreviewVideoFromFrames] Frames folder not found.");
                return;
            }

            string folderName = Path.GetFileName(framesFolderPath);

            // Support both old (_frames suffix) and new flat folder naming
            string baseFileName;
            if (folderName.EndsWith("_frames"))
            {
                baseFileName = folderName.Substring(0, folderName.Length - "_frames".Length);
            }
            else
            {
                baseFileName = folderName;
            }

            string outputVideoPath = Path.Combine(Path.GetDirectoryName(framesFolderPath), baseFileName + "_preview.mp4");

            // Build input pattern using full absolute path to the frames folder
            string inputPattern = Path.Combine(framesFolderPath, baseFileName + "_frame_%06d.png");

            // ====================================================================
            // PHASE 4: PRE-MUX DURATION DIAGNOSTICS
            // ====================================================================
            int frameFileCount = 0;
            try
            {
                string[] frameFiles = Directory.GetFiles(framesFolderPath, baseFileName + "_frame_*.png");
                frameFileCount = frameFiles.Length;
            }
            catch (Exception countEx)
            {
                Debug.LogError("[VideoRecorderUtils.GeneratePreviewVideoFromFrames] Failed to count frame files: " + countEx.Message);
            }

            float videoDurationSeconds = (framerate > 0 && frameFileCount > 0)
                ? (float)frameFileCount / (float)framerate
                : -1f;

            Debug.LogError(
                $"[VideoRecorderUtils.GeneratePreviewVideoFromFrames] Pre-mux video: " +
                $"frames={frameFileCount} | framerate={framerate} | " +
                $"expectedVideoDurationSec={(videoDurationSeconds >= 0f ? videoDurationSeconds.ToString("F3") : "n/a")}");

            // ====================================================================
            // TWO-PASS AUTOMATED HIGH-FIDELITY AUDIO MULTIPLEXER HOOK
            // ====================================================================
            string parentDir = Path.GetDirectoryName(framesFolderPath);
            string sourceWavPath = Path.Combine(parentDir, baseFileName + ".wav");
            bool hasLocalAudio = File.Exists(sourceWavPath);

            if (hasLocalAudio)
            {
                float wavDurationSeconds = EstimatePcmWavDurationSeconds(sourceWavPath);
                long wavBytes = 0;
                try { wavBytes = new FileInfo(sourceWavPath).Length; } catch { /* ignore */ }

                Debug.LogError(
                    $"[VideoRecorderUtils.GeneratePreviewVideoFromFrames] Pre-mux audio: " +
                    $"wavFound=True | path=\"{sourceWavPath}\" | bytes={wavBytes} | " +
                    $"estimatedWavDurationSec={(wavDurationSeconds >= 0f ? wavDurationSeconds.ToString("F3") : "n/a")}");

                if (videoDurationSeconds >= 0f && wavDurationSeconds >= 0f)
                {
                    float delta = wavDurationSeconds - videoDurationSeconds;
                    Debug.LogError(
                        $"[VideoRecorderUtils.GeneratePreviewVideoFromFrames] Pre-mux compare: " +
                        $"videoSec={videoDurationSeconds:F3} | wavSec={wavDurationSeconds:F3} | " +
                        $"deltaSec={delta:F3} | (-shortest will trim to shorter if mismatch)");
                }
            }
            else
            {
                Debug.LogError(
                    $"[VideoRecorderUtils.GeneratePreviewVideoFromFrames] Pre-mux audio: wavFound=False | lookedFor=\"{sourceWavPath}\"");
            }

            // Correct FFmpeg order:
            // 1) global flags + video input (with -framerate before -i)
            // 2) optional audio input
            // 3) video codec options
            // 4) audio codec options (only if audio present)
            // 5) -shortest (only if audio present)
            // 6) output path
            string audioInputArgs = "";
            string audioCodecArgs = "";

            if (hasLocalAudio && AudioCaptureManager.m_Instance != null)
            {
                string qualityPreset = AudioCaptureManager.m_Instance.FFmpegAudioBitratePreset;
                audioInputArgs = string.Format("-i \"{0}\" ", sourceWavPath);

                if (qualityPreset.ToLower() == "copy")
                {
                    audioCodecArgs = "-c:a copy ";
                }
                else
                {
                    audioCodecArgs = string.Format("-c:a aac -b:a {0} ", qualityPreset);
                }
            }

            string ffmpegCommand = string.Format(
                "-y -framerate {0} -i \"{1}\" {2}-c:v libx264 -pix_fmt yuv420p -crf {3} -preset {4} {5} {6} -vsync 0 \"{7}\"",
                framerate,
                inputPattern,
                audioInputArgs,
                crf,
                preset,
                audioCodecArgs,
                hasLocalAudio ? "-shortest" : "",
                outputVideoPath
            );

            // ====================================================================
            Debug.LogError("[VideoRecorderUtils.GeneratePreviewVideoFromFrames] Running ffmpeg: " + ffmpegCommand);

            try
            {
                System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = ffmpegCommand,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo))
                {
                    string output = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    if (process.ExitCode == 0)
                    {
                        Debug.LogError("[VideoRecorderUtils.GeneratePreviewVideoFromFrames] Preview video created successfully: " + outputVideoPath);
                    }
                    else
                    {
                        Debug.LogError("[VideoRecorderUtils.GeneratePreviewVideoFromFrames] ffmpeg failed. Output:\n" + output);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[VideoRecorderUtils.GeneratePreviewVideoFromFrames] Exception while running ffmpeg: " + e.Message);
            }
        }

        /// <summary>
        /// Estimates duration of a standard 16-bit (or header-defined) PCM WAV with a 44-byte header
        /// as written by AudioCaptureManager.ConvertPcmToWavFormat. Returns -1 on failure.
        /// </summary>
        private static float EstimatePcmWavDurationSeconds(string wavPath)
        {
            try
            {
                using (FileStream fs = File.OpenRead(wavPath))
                using (BinaryReader br = new BinaryReader(fs))
                {
                    if (fs.Length < 44)
                    {
                        return -1f;
                    }

                    // Standard PCM WAV layout produced by ConvertPcmToWavFormat
                    br.BaseStream.Seek(22, SeekOrigin.Begin);
                    short channels = br.ReadInt16();
                    int sampleRate = br.ReadInt32();
                    br.ReadInt32(); // byteRate
                    br.ReadInt16(); // blockAlign
                    short bitsPerSample = br.ReadInt16();

                    if (channels <= 0 || sampleRate <= 0 || bitsPerSample <= 0)
                    {
                        return -1f;
                    }

                    int bytesPerSample = bitsPerSample / 8;
                    if (bytesPerSample <= 0)
                    {
                        return -1f;
                    }

                    long dataBytes = fs.Length - 44;
                    if (dataBytes <= 0)
                    {
                        return -1f;
                    }

                    long totalSamples = dataBytes / (channels * bytesPerSample);
                    return (float)totalSamples / (float)sampleRate;
                }
            }
            catch
            {
                return -1f;
            }
        }



        // Update 2130 VideoRecorderUtils.SerializerNewUsdFrame
        // Purpose: Improved diagnostics when routing a stop through CameraPathCaptureRig.
        // Now logs both the internal frame counter and the StillFrameSequenceExporter count
        // when the target is reached. This helps verify that the correct frame count is being
        // used when handing control to the two-pass sequence.
        //
        // Phase 2: When Pass 2 is in the pad region and WritePaddingAsPureBlack is set,
        // CaptureFrame is called with forcePureBlack: true so trailing frames are solid black
        // while the frame budget still runs to exact N.
        //
        // Previous updates retained below for reference:
        /// Update 2041 VideoRecorderUtils.SerializerNewUsdFrame
        /// Purpose: When the target frame count is reached during a camera path recording,
        /// route the stop through CameraPathCaptureRig.StopRecordingPath(true) instead of
        /// calling StopVideoCapture(true) directly. This ensures the linear two-pass sequence
        /// (Steps 2-5 for first pass, Steps 7-9 for second pass) executes correctly.
        /// A fallback to direct StopVideoCapture() is kept only for recordings that do not
        /// use CameraPathCaptureRig.
        ///
        /// Update 2128: Changed Simulated BPM diagnostic from Debug.Log to Debug.LogError
        /// and added throttling (logs every 30 frames) to prevent console spam during long recordings
        /// while still providing useful visibility for evaluation.
        // VideoRecorderUtils.SerializerNewUsdFrame
        static public void SerializerNewUsdFrame()
        {
            if (m_UsdPathSerializer != null && m_UsdPathSerializer.IsRecording)
            {
                m_UsdPathSerializer.Time = (float)m_RecordingStopwatch.Elapsed.TotalSeconds;
                m_UsdPathSerializer.Serialize();

                // Two-Pass Phase-Locked Audio Reactivity Driver
                if (AudioCaptureManager.m_Instance != null)
                {
                    AudioCaptureManager.OfflineAudioRenderMode activeMode = AudioCaptureManager.m_Instance.m_OfflineAudioMode;

                    // Skip calculations entirely if the dropdown is set to Pure Native (State 3)
                    if (activeMode != AudioCaptureManager.OfflineAudioRenderMode.Pure_Native_No_Audio)
                    {
                        if (VisualizerManager.m_Instance == null)
                        {
                            Debug.LogError("[VideoRecorderUtils.SerializerNewUsdFrame] Audio active but VisualizerManager.m_Instance is null.");
                        }
                        else
                        {
                            // CRITICAL DISAMBIGUATION FILTER: Only route into offline steps if we aren't executing a live audio capture run
                            bool isTwoPassActiveRecording = CameraPathCaptureRig.m_Instance != null &&
                                                           CameraPathCaptureRig.m_Instance.UseFrameLimit &&
                                                           !App.Config.OfflineRender &&
                                                           !m_IsLiveAudioCaptureActive;

                            if (isTwoPassActiveRecording)
                            {
                                float recordingTime = (float)m_UsdPathSerializer.Time;
                                float totalDuration = (float)m_UsdPathSerializer.Duration;
                                float normalizedProgress = totalDuration > 0f ? Mathf.Clamp01(recordingTime / totalDuration) : 0f;
                                float dynamicProgressLimit = CameraPathCaptureRig.m_Instance.SecondPassTraversalPercent / 100f;

                                if (m_CurrentFrameCount % 30 == 0)
                                {
                                    Debug.LogError($"[Two-Pass Audio Sync: {(m_IsVisualizerPlaybackPass ? "PLAYBACK" : "BAKE")}] Progress: {normalizedProgress:F4}");
                                }

                                VisualizerManager.m_Instance.UpdateOfflineSimulationStep(normalizedProgress, recordingTime, dynamicProgressLimit);
                            }
                            else if (!m_IsLiveAudioCaptureActive)
                            {
                                // FALLBACK / REPLAY OVERRIDE LOOP (Only run if it isn't an interactive live capture session)
                                float recordingTime = (float)m_UsdPathSerializer.Time;
                                VisualizerManager.m_Instance.UpdateOfflineSimulationStep(1.0f, recordingTime, 1.0f);
                            }
                            else
                            {
                                // INTERACTIVE LIVE SEQUENCING PIECE: Do absolutely nothing here!
                                // Let your live hardware frequencies continue driving Shaders natively via ProcessAudio.
                            }
                        }
                    }
                    else
                    {
                        // STATE 3 ENFORCEMENT: Strictly force disable the global keyword every frame tick
                        Shader.DisableKeyword("AUDIO_REACTIVE");
                    }
                }

                m_CurrentFrameCount++;

                // Capture still frame if using fallback mode
                if (m_UsingStillFrameFallback && m_ActiveStillFrameExporter != null)
                {
                    float currentTime = (float)m_RecordingStopwatch.Elapsed.TotalSeconds;

                    // Phase 2: pure-black pad frames when path content is done and inspector flag is set
                    bool forcePureBlack = false;
                    if (CameraPathCaptureRig.m_Instance != null)
                    {
                        forcePureBlack = CameraPathCaptureRig.m_Instance.IsSecondPassInPadRegion
                            && CameraPathCaptureRig.m_Instance.WritePaddingAsPureBlack;
                    }

                    m_ActiveStillFrameExporter.CaptureFrame(currentTime, forcePureBlack);
                }

                // Target frame count reached?
                if (m_TargetFrameCount > 0)
                {
                    bool shouldStop = false;
                    int actualWritten = m_CurrentFrameCount;

                    if (m_UsingStillFrameFallback && m_ActiveStillFrameExporter != null)
                    {
                        actualWritten = m_ActiveStillFrameExporter.FrameCount;
                        if (actualWritten >= m_TargetFrameCount)
                        {
                            shouldStop = true;
                        }
                    }
                    else if (m_CurrentFrameCount >= m_TargetFrameCount)
                    {
                        shouldStop = true;
                    }

                    // DIAGNOSTIC: Exact counter values at the moment the stop decision is evaluated
                    Debug.LogError($"[SerializerNewUsdFrame DEBUG] m_CurrentFrameCount={m_CurrentFrameCount} | Exporter.FrameCount={(m_ActiveStillFrameExporter != null ? m_ActiveStillFrameExporter.FrameCount : -1)} | Target={m_TargetFrameCount} | shouldStop={shouldStop}");

                    if (shouldStop)
                    {
                        Debug.LogError($"[SerializerNewUsdFrame] Target frame count reached. " +
                                       $"Target: {m_TargetFrameCount} | Internal count: {m_CurrentFrameCount} | " +
                                       $"Exporter count: {actualWritten}. Routing stop through CameraPathCaptureRig.");

                        if (CameraPathCaptureRig.m_Instance != null)
                        {
                            CameraPathCaptureRig.m_Instance.StopRecordingPath(true);
                        }
                        else
                        {
                            // Fallback for recordings not using CameraPathCaptureRig
                            Debug.LogError("[SerializerNewUsdFrame] CameraPathCaptureRig.m_Instance is null. Falling back to direct StopVideoCapture.");
                            StopVideoCapture(true);
                        }
                        return;
                    }
                }

                // Path looping logic (unchanged)
                if (m_UsdPathSerializer.Duration > 0 && m_UsdPathSerializer.Time >= m_UsdPathSerializer.Duration)
                {
                    Debug.LogError("Camera path reached end before target frame count (" +
                                   m_CurrentFrameCount + " / " + m_TargetFrameCount +
                                   "). Looping path to reach exact frame target.");
                    m_UsdPathSerializer.Time = 0;
                    m_UsdPathSerializer.Deserialize();
                }
            }
        }







        // Update 1002 VideoRecorderUtils.GetTargetFrameCount
        // Purpose: Converts the user-specified duration in seconds into the correct number of frames.
        // Uses App.UserConfig.Video.FPS (or OfflineFPS when offline rendering is active).
        // This ensures consistent clip duration for Resolume packs whether recording in-game or offline.
        public static int GetTargetFrameCount(float durationSeconds)
        {
            if (durationSeconds <= 0f)
            {
                return -1;
            }

            float fps = App.UserConfig.Video.FPS;

            // If offline rendering is active, prefer the offline FPS setting
            if (m_ActiveVideoRecording != null && m_ActiveVideoRecording.IsCapturing)
            {
                fps = App.UserConfig.Video.OfflineFPS;
            }

            return Mathf.RoundToInt(durationSeconds * fps);
        }



        static public bool StartVideoCapture(string filePath, VideoRecorder recorder,
    UsdPathSerializer usdPathSerializer, bool offlineRender = false,
    bool appendFramesSuffix = true)
        {
            if (m_ActiveVideoRecording != null || m_ActiveStillFrameExporter != null)
            {
                bool classicReallyActive = m_ActiveVideoRecording != null &&
                    m_ActiveVideoRecording.IsCapturing;
                bool stillReallyActive = m_ActiveStillFrameExporter != null &&
                    m_ActiveStillFrameExporter.IsCapturing;

                if (classicReallyActive || stillReallyActive)
                {
                    Debug.LogError(
                        "[VideoRecorderUtils.StartVideoCapture] Rejected: capture already in progress. " +
                        "classic=" + classicReallyActive +
                        " stillFrame=" + stillReallyActive +
                        " m_ActiveVideoRecording=" + (m_ActiveVideoRecording != null) +
                        " m_ActiveStillFrameExporter=" + (m_ActiveStillFrameExporter != null));
                    OutputWindowScript.ReportFileSaved(
                        "Recording already in progress!",
                        null,
                        OutputWindowScript.InfoCardSpawnPos.Brush);
                    return false;
                }

                Debug.LogError(
                    "[VideoRecorderUtils.StartVideoCapture] Clearing stale capture state before start. " +
                    "m_ActiveVideoRecording=" + (m_ActiveVideoRecording != null) +
                    " m_ActiveStillFrameExporter=" + (m_ActiveStillFrameExporter != null) +
                    " m_UsingStillFrameFallback=" + m_UsingStillFrameFallback +
                    " m_TargetFrameCount=" + m_TargetFrameCount);
                m_ActiveVideoRecording = null;
                m_ActiveStillFrameExporter = null;
                m_UsingStillFrameFallback = false;
                m_TargetFrameCount = -1;
                m_CurrentFrameCount = 0;
                m_IsVisualizerPlaybackPass = false;
                m_IsLiveAudioCaptureActive = false;
            }

            if (!FileUtils.InitializeDirectoryWithUserError(
                Path.GetDirectoryName(filePath),
                "Failed to start video capture"))
            {
                return false;
            }

#if UNITY_ANDROID || UNITY_IOS
    bool stillFrameCapture = true;
#else
            bool stillFrameCapture = App.UserConfig.Video.ForceFrameSequenceRender;
#endif
            if (stillFrameCapture)
            {
                return StartStillFrameSequenceCapture(filePath, recorder, usdPathSerializer,
                    offlineRender, appendFramesSuffix);
            }

            recorder.IsPortrait = false;
            int sampleRate = 0;
            bool shouldRecordAudioTrack = false;

            // Stock: audio track only when capture is already running.
            if (AudioCaptureManager.m_Instance != null
                && AudioCaptureManager.m_Instance.IsCapturingAudio)
            {
                shouldRecordAudioTrack = true;
                sampleRate = AudioCaptureManager.m_Instance.SampleRate;
            }

            if (!recorder.StartCapture(filePath, sampleRate,
                shouldRecordAudioTrack, offlineRender,
                offlineRender ? App.UserConfig.Video.OfflineFPS : App.UserConfig.Video.FPS))
            {
                OutputWindowScript.ReportFileSaved("Failed to start capture!", null,
                    OutputWindowScript.InfoCardSpawnPos.Brush);
                return false;
            }

            m_ActiveVideoRecording = recorder;

            if (m_DebugVideoCaptureQualityLevel != -1)
            {
                m_PreCaptureQualityLevel = QualityControls.m_Instance.QualityLevel;
                QualityControls.m_Instance.QualityLevel = m_DebugVideoCaptureQualityLevel;
            }

            RenderWrapper wrapper = recorder.gameObject.GetComponent<RenderWrapper>();
            m_PreCaptureSuperSampling = wrapper.SuperSampling;
            wrapper.SuperSampling = m_SuperSampling;

            m_UsdPathSerializer = usdPathSerializer;
            if (!offlineRender)
            {
                m_UsdPath = SaveLoadScript.m_Instance.SceneFile.Valid ?
                    Path.ChangeExtension(filePath, "usda") : null;
                m_RecordingStopwatch = new System.Diagnostics.Stopwatch();
                m_RecordingStopwatch.Start();

                m_IsVisualizerPlaybackPass = false;
                if (VisualizerManager.m_Instance != null)
                {
                    VisualizerManager.m_Instance.SetPlaybackMode(false);
                }

                // HiFi only when two-pass (or caller) set live-audio active.
                if (m_IsLiveAudioCaptureActive && AudioCaptureManager.m_Instance != null)
                {
                    AudioCaptureManager.m_Instance.StartHiFiAudioRecording(filePath);
                }

                if (m_UsdPathSerializer != null && !m_UsdPathSerializer.StartRecording(m_UsdPath))
                {
                    UnityEngine.Object.Destroy(m_UsdPathSerializer);
                    m_UsdPathSerializer = null;
                }
            }
            else
            {
                recorder.SetCaptureFramerate(Mathf.RoundToInt(App.UserConfig.Video.OfflineFPS));
                m_UsdPath = null;
                if (m_UsdPathSerializer != null && m_UsdPathSerializer.Load(App.Config.m_VideoPathToRender))
                {
                    m_UsdPathSerializer.StartPlayback();
                }
                else if (m_UsdPathSerializer != null)
                {
                    UnityEngine.Object.Destroy(m_UsdPathSerializer);
                    m_UsdPathSerializer = null;
                }
            }

            return true;
        }



        // Update 1000 VideoRecorderUtils.SetTargetFrameCount (added in update 1000)
        // Purpose: Allows CameraPathCaptureRig (or future callers) to set the exact number of frames the camera should run. 
        // Resets the internal counter so each new recording starts fresh. Central place for the limit so both still-frame and video paths respect it.
        // Fix: N/A new feature for precise frame control

        static public void SetTargetFrameCount(int frames)
        {
            m_TargetFrameCount = frames;
            m_CurrentFrameCount = 0;
        }

        static private bool StartStillFrameSequenceCapture(string filePath, VideoRecorder recorder,
                                                   UsdPathSerializer usdPathSerializer,
                                                   bool offlineRender = false,
                                                   bool appendFramesSuffix = true)
        {
            StillFrameSequenceExporter exporter = recorder.gameObject.GetComponent<StillFrameSequenceExporter>();
            if (exporter == null)
            {
                exporter = recorder.gameObject.AddComponent<StillFrameSequenceExporter>();
            }

            Debug.LogError($"[VideoRecorderUtils] StillFrameSequenceExporter attached to GameObject: {recorder.gameObject.name}");
            Debug.LogError($"[VideoRecorderUtils] Full path of exporter's GameObject: {GetFullHierarchyPath(recorder.transform)}");

            float fps;
            if (offlineRender)
            {
                if (CameraPathCaptureRig.m_Instance != null && CameraPathCaptureRig.m_Instance.OfflineCaptureFPS > 0)
                {
                    fps = CameraPathCaptureRig.m_Instance.OfflineCaptureFPS;
                    Debug.LogError($"[VideoRecorderUtils] StillFrameSequenceCapture using rig override: offlineCaptureFPS = {fps}");
                }
                else
                {
                    fps = App.UserConfig.Video.OfflineFPS;
                    Debug.LogError($"[VideoRecorderUtils] StillFrameSequenceCapture: rig override unavailable/unset, using UserConfig.Video.OfflineFPS = {fps}");
                }
            }
            else
            {
                fps = App.UserConfig.Video.FPS;
            }

            if (!exporter.StartCapture(filePath, fps, appendFramesSuffix))
            {
                OutputWindowScript.ReportFileSaved("Failed to start still frame sequence capture!", null,
                    OutputWindowScript.InfoCardSpawnPos.Brush);
                return false;
            }

            m_ActiveStillFrameExporter = exporter;
            m_UsingStillFrameFallback = true;

            if (m_DebugVideoCaptureQualityLevel != -1)
            {
                m_PreCaptureQualityLevel = QualityControls.m_Instance.QualityLevel;
                QualityControls.m_Instance.QualityLevel = m_DebugVideoCaptureQualityLevel;
            }

            RenderWrapper wrapper = recorder.gameObject.GetComponent<RenderWrapper>();
            if (wrapper != null)
            {
                m_PreCaptureSuperSampling = wrapper.SuperSampling;
                wrapper.SuperSampling = m_SuperSampling;
            }

            m_UsdPathSerializer = usdPathSerializer;
            m_UsdPath = SaveLoadScript.m_Instance.SceneFile.Valid ?
                Path.ChangeExtension(filePath, "usda") : null;
            m_RecordingStopwatch = new System.Diagnostics.Stopwatch();
            m_RecordingStopwatch.Start();

            if (!offlineRender)
            {
                if (m_UsdPathSerializer != null && !m_UsdPathSerializer.StartRecording(m_UsdPath))
                {
                    Debug.LogWarning("USD Path Serializer failed to start recording");
                    UnityEngine.Object.Destroy(m_UsdPathSerializer);
                    m_UsdPathSerializer = null;
                }
            }

            return true;
        }

  

        static private string GetFullHierarchyPath(Transform t)
        {
            if (t == null) return "NULL";

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            while (t != null)
            {
                if (sb.Length > 0) sb.Insert(0, " > ");
                sb.Insert(0, t.name);
                t = t.parent;
            }
            return sb.ToString();
        }




        // Update 2131 VideoRecorderUtils.StopVideoCapture
        // Purpose: Minor diagnostic improvement when frame correction runs.
        // No functional changes to the correction logic.
        //
        // Previous documentation retained below:
        /// Update 2046 VideoRecorderUtils.StopVideoCapture
        /// Purpose: Fix broken frame correction logic + use explicit condition.
        ///
        /// History / Notes:
        /// - Previously used: if (saveCapture && m_TargetFrameCount > 0)
        /// This became obsolete after routing stops through CameraPathCaptureRig.StopRecordingPath().
        /// The old condition is kept in comments below so it can be reverted easily if testing reveals issues.
        /// - Changed to explicit condition: if (saveCapture && m_UsingStillFrameFallback && m_ActiveStillFrameExporter != null)
        /// for clarity and future resilience.
        /// - m_TargetFrameCount is reset early in this method, so we now capture its original value before resetting
        /// to ensure frame correction can still function when a target was set.
        /// - Preview video generation remains removed (handled only in PerformStep9_FinalizeSecondPassCleanup()).
        /// <summary>
        /// VideoRecorderUtils.StopVideoCapture
        /// Halts active recording modules, applies frame-count corrections, and flushes unmanaged file streams back to disk.
        /// </summary>
        /// <summary>
        /// VideoRecorderUtils.StopVideoCapture
        /// Halts active recording modules, applies frame-count corrections, and flushes unmanaged file streams back to disk.
        /// </summary>

        static public void StopVideoCapture(bool saveCapture)
        {
            int finalWritten = m_CurrentFrameCount;
            if (m_UsingStillFrameFallback && m_ActiveStillFrameExporter != null)
            {
                finalWritten = m_ActiveStillFrameExporter.FrameCount;
            }

            if (m_TargetFrameCount > 0)
            {
                Debug.LogError("Recording stopped. Target frames: " + m_TargetFrameCount +
                 " | Actual frames written: " + finalWritten);
            }

            int originalTarget = m_TargetFrameCount;
            m_TargetFrameCount = -1;
            m_CurrentFrameCount = 0;

            if (m_DebugVideoCaptureQualityLevel != -1)
            {
                QualityControls.m_Instance.QualityLevel = m_PreCaptureQualityLevel;
            }

            if (m_UsingStillFrameFallback && m_ActiveStillFrameExporter != null)
            {
                m_ActiveStillFrameExporter.StopCapture(saveCapture);

                if (saveCapture && m_UsingStillFrameFallback && m_ActiveStillFrameExporter != null)
                {
                    int actual = m_ActiveStillFrameExporter.FrameCount;
                    if (originalTarget > 0)
                    {
                        if (actual < originalTarget)
                        {
                            int framesShort = originalTarget - actual;
                            m_ActiveStillFrameExporter.DuplicateLastFrames(framesShort);
                            Debug.LogError("Frame count was short by " + framesShort +
                            ". Duplicated last frame(s) to reach exact target.");
                        }
                        else if (actual > originalTarget)
                        {
                            int framesOver = actual - originalTarget;
                            m_ActiveStillFrameExporter.RemoveLastFrames(framesOver);
                            Debug.LogError("Frame count was over by " + framesOver +
                            ". Removed excess frame(s) to reach exact target.");
                        }
                        else
                        {
                            Debug.LogError("Frame count was exact. No correction needed.");
                        }
                    }
                }

                var wrapper = m_ActiveStillFrameExporter.gameObject.GetComponent<RenderWrapper>();
                if (wrapper != null)
                {
                    wrapper.SuperSampling = m_PreCaptureSuperSampling;
                }
                m_ActiveStillFrameExporter = null;
                m_UsingStillFrameFallback = false;
            }
            else if (m_ActiveVideoRecording != null)
            {
                m_ActiveVideoRecording.gameObject.GetComponent<RenderWrapper>().SuperSampling =
                    m_PreCaptureSuperSampling;
                m_ActiveVideoRecording.StopCapture(save: saveCapture);
                m_ActiveVideoRecording = null;
            }

            if (m_UsdPathSerializer != null)
            {
                bool wasRecording = m_UsdPathSerializer.IsRecording;
                m_UsdPathSerializer.Stop();

                if (wasRecording)
                {
                    m_RecordingStopwatch.Stop();
                    if (!string.IsNullOrEmpty(m_UsdPath))
                    {
                        if (App.UserConfig.Video.SaveCameraPath && saveCapture)
                        {
                            m_UsdPathSerializer.Save();
                            CreateOfflineRenderBatchFile(
                                SaveLoadScript.m_Instance.SceneFile.FullPath, m_UsdPath);

                            // Two-pass / live-audio only — not classic single-pass.
                            if (m_IsLiveAudioCaptureActive
                                && AudioCaptureManager.m_Instance != null
                                && AudioCaptureManager.m_Instance.IsSimulatedBPMModeActive)
                            {
                                if (VisualizerManager.m_Instance != null && !m_IsVisualizerPlaybackPass)
                                {
                                    string directory = Path.GetDirectoryName(m_UsdPath);
                                    string filename = Path.GetFileNameWithoutExtension(m_UsdPath);
                                    string companionAudioPath = Path.Combine(
                                        directory, filename + ".openbrushaudio");
                                    VisualizerManager.m_Instance.SaveBakedAudioCache(
                                        companionAudioPath);
                                }
                            }
                        }
                    }
                }
            }

            m_IsVisualizerPlaybackPass = false;
            m_IsLiveAudioCaptureActive = false;

            if (VisualizerManager.m_Instance != null)
            {
                VisualizerManager.m_Instance.SetPlaybackMode(false);
            }

            m_UsdPathSerializer = null;
            m_RecordingStopwatch = null;
            App.Switchboard.TriggerVideoRecordingStopped();
        }




        /// Creates a batch file the user can execute to make a high quality re-render of the video that
        /// has just been recorded.
        static void CreateOfflineRenderBatchFile(string sketchFile, string usdaFile)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            string batFile = Path.ChangeExtension(usdaFile, ".HQ_Render.bat");
            var pathSections = Application.dataPath.Split('/').ToArray();
            var exePath = String.Join("/", pathSections.Take(pathSections.Length - 1).ToArray());

            // It would be nice to think of a way to get this to do something sensible in the editor!
            string offlineRenderExePath = Process.GetCurrentProcess().MainModule.FileName;

            string batText = string.Format(
                "@\"{0}/Support/bin/renderVideo.cmd\" ^\n\t\"{1}\" ^\n\t\"{2}\" ^\n\t\"{3}\"",
                exePath, sketchFile, usdaFile, offlineRenderExePath);
            File.WriteAllText(batFile, batText);
#endif
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            string shFile = Path.ChangeExtension(usdaFile, ".HQ_Render.sh");
            var pathSections = Application.dataPath.Split('/').ToArray();
            var exePath = String.Join("/", pathSections.Take(pathSections.Length - 1).ToArray());

            // It would be nice to think of a way to get this to do something sensible in the editor!
            string offlineRenderExePath = Process.GetCurrentProcess().MainModule.FileName;

            string batText = $"\"{exePath}/Support/bin/renderVideo.sh\" \\\n\t\"{sketchFile}\" \\\n\t\"{usdaFile}\" \\\n\t\"{offlineRenderExePath}\"";
            File.WriteAllText(shFile, batText);
#endif

        }
    }

} // namespace TiltBrush


