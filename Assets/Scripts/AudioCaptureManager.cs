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


using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace TiltBrush
{

    // This is the stack of audio objects that we create, and notes on how to clean them up.
    //
    // Class                Interface       Notes                                   Needs disposal?
    // --------------------------------------------------------------------------------------------
    // MMDevice                             Needs dispose                           MUST DISPOSE
    //
    // WasapiCapture        ISoundIn        Does _not_ dispose this.Device          MUST DISPOSE
    //  m_AudioCapture                      this.Device (sometimes a default device)
    //                                      Provides DataAvailable
    //
    // SoundInSource        IWaveSource     Removes self from base.DataAvailable    MUST DISPOSE
    //                                      Does _not_ dispose this.SoundIn
    //                                      this.SoundIn (cannot get at if disposed)
    //                                      Consumes base.DataAvailable, provides DataAvailable
    //
    // WaveToSampleBase     ISampleSource   Just disposes wrapped
    //                                      Cannot get at wrapped
    //
    // SingleBlockNotify    ISampleSource   Disposes wrapped if DisposeBaseSource=true
    //  m_FinalSouce                        this.BaseSource
    //

    public class AudioCaptureManager : MonoBehaviour
    {
        // Number of seconds to delay before searching for active audio device.
        // From experimentation this seems to be the minimum to ensure we don't pick up
        // any residual audio.
        const float DEVICE_SEARCH_DELAY = 1.0f;

        // added SimulatedBPM so we can improvise reactivity offline or when audio is not present
        private enum AudioCaptureType
        {
            File,
            System,
            App,
            Script,
            SimulatedBPM
        }

        static public AudioCaptureManager m_Instance;

        [SerializeField] private SystemAudioMonitor m_SystemAudio;
        [SerializeField] private GameObject m_FileAudio;
        [SerializeField] private GameObject m_AppAudio;

        // Offline Recording
        [Header("Two-Pass High-Fidelity Audio Settings")]
        [SerializeField] private bool m_EnableHighFidelityCapture = true;
        [Tooltip("Uncheck this to record visual brush pulsing from the spreadsheet data while leaving the final exported video file completely silent.")]
        [SerializeField] private bool m_SaveAudioTrackFile = true;
        [SerializeField][UnityEngine.Range(16000, 48000)] private int m_AudioCaptureSampleRate = 48000;
        [Tooltip("Options: '320k' for pristine compressed streaming audio, or 'copy' to map absolute uncompressed 1,536kbps PCM bytes into the MP4 stream container.")]
        [SerializeField] private string m_FFmpegAudioBitratePreset = "320k";
        [Tooltip("If checked, the exported video soundtrack will continue playing through the trailing black frames. If unchecked, the audio file will be trimmed to cut off the exact millisecond the camera stops moving.")]
        [SerializeField] private bool m_AllowAudioDuringPadding = true;
        public bool AllowAudioDuringPadding => m_AllowAudioDuringPadding;

        // Frame count at geometric path complete (content end). 0 = unknown.
        // Used when m_AllowAudioDuringPadding is false to silence the pad region.
        private int m_SecondPassContentFrameCount = 0;

        public int SecondPassContentFrameCount
        {
            get { return m_SecondPassContentFrameCount; }
        }



        // File: AudioCaptureManager.cs
        // Public getter properties to expose inspector parameters safely to external video utils
        public string FFmpegAudioBitratePreset => m_FFmpegAudioBitratePreset;
        public bool SaveAudioTrackFile => m_SaveAudioTrackFile;

        // File: AudioCaptureManager.cs Field Declarations Section
        private readonly object m_AudioStreamLock = new object();

        private int m_DiagnosticThreadTickCount = 0;

        public enum OfflineAudioRenderMode
        {
            Pure_Native_No_Audio = 0,
            Force_Simulated_BPM = 1,
            Use_PreRecorded_Data = 2
        }

        public enum OnlineAudioPreviewMode
        {
            Standard_Hardware_Audio = 0, // Listens to live microphone/desktop mixer loops natively
            Force_Simulated_BPM = 1      // Forces your math simulator to run live in the viewport
        }

        [Header("Two-Pass Offline Configuration (Headless Flow)")]
        public OfflineAudioRenderMode m_OfflineAudioMode = OfflineAudioRenderMode.Pure_Native_No_Audio;

        [Header("Real-Time Online Configuration (Live Game Flow)")]
        public OnlineAudioPreviewMode m_OnlineAudioMode = OnlineAudioPreviewMode.Standard_Hardware_Audio;



        [Header("Simulated BPM Settings")]
        public float m_ReactivityStrength = 1.0f;
        public float m_SimulatedBPM = 128f;


        private AudioCaptureType m_Type;
        private int m_CaptureRequestedCount;

        // Locked FPS for Pass 2 timeline sync (set by StopAndSaveHiFiAudioTrack before SynchronizeTimelineData).
        // Written only from CameraPathCaptureRig.PerformStep10 — never read back from the rig here.
        private int m_TimelineSyncFps = 30;
        private float[] m_IsolatedSnapshotSamples = null;
        private int m_SnapshotRawCount = 0;
        private int m_SnapshotFinalizedChannels = 2;


        void Awake()
        {
            ResetAudioCaptureType();
        }


        // Update 2117 AudioCaptureManager.LateUpdate
        void LateUpdate()
        {
            if (m_Type == AudioCaptureType.SimulatedBPM && VisualizerManager.m_Instance != null)
            {
                VisualizerManager.m_Instance.SetReactivityStrength(m_ReactivityStrength);
            }
        }

        /// <summary>
        /// AudioCaptureManager.OnDisable
        /// Native Unity lifecycle event hook. Forcefully flushes running parallel stream flags
        /// and safely releases unmanaged memory buffers if the object is ever disabled or torn down.
        /// </summary>
        private void OnDisable()
        {
            if (m_IsParallelHiFiAudioRecordingActive)
            {
                m_IsParallelHiFiAudioRecordingActive = false;
            }

            lock (m_AudioStreamLock)
            {
                if (m_ParallelHiFiAudioSamplesBuffer != null)
                {
                    m_ParallelHiFiAudioSamplesBuffer.Clear();
                }
            }
        }


        private void ResetAudioCaptureType()
        {
            m_Instance = this;
            if (LuaManager.Instance != null) LuaManager.Instance.VisualizerScriptingEnabled = false;
#if UNITY_ANDROID || UNITY_IOS
            m_Type = AudioCaptureType.App;
#else
            m_Type = AudioCaptureType.System;
#endif
            m_CaptureRequestedCount = 0;

        }

        public bool CaptureRequested
        {
            get { return m_CaptureRequestedCount > 0; }
        }



        public void EnableScripting()
        {
            m_FileAudio.SetActive(false);
            m_AppAudio.SetActive(false);
            m_SystemAudio.gameObject.SetActive(false);
            m_Type = AudioCaptureType.Script;
            LuaManager.Instance.VisualizerScriptingEnabled = true;
            App.Instance.AudioReactiveBrushesActive(true);
        }

        public void DisableScripting()
        {
            App.Instance.AudioReactiveBrushesActive(false);
            ResetAudioCaptureType();
        }




        // Update 2133 AudioCaptureManager.EnableSimulatedBPM
        // fixes access violation in cross script VisualizerManager
        public void EnableSimulatedBPM(float targetBPM)
        {
            if (m_Type == AudioCaptureType.SimulatedBPM && Mathf.Approximately(m_SimulatedBPM, targetBPM))
            {
                return;
            }

            DisableCurrentMode();
            m_Type = AudioCaptureType.SimulatedBPM;
            m_SimulatedBPM = Mathf.Max(1f, targetBPM);

            if (VisualizerManager.m_Instance != null)
            {
                VisualizerManager.m_Instance.SetSimulatedBPM(m_SimulatedBPM, true);
            }

            Debug.LogError($"[AudioCaptureManager] Simulated BPM mode ENABLED. Target BPM: {m_SimulatedBPM}");

            App.Instance.AudioReactiveBrushesActive(true);

            // Force visuals to re-evaluate now that Simulated BPM mode is active.
            // Using AudioCaptureStatusChange instead of direct field access.
            if (VisualizerManager.m_Instance != null)
            {
                VisualizerManager.m_Instance.AudioCaptureStatusChange(true);
            }
        }

        // Update 2118 AudioCaptureManager.DisableSimulatedBPM
        // Purpose: Explicitly disable Simulated BPM mode. Reset both BPM and reactivity strength
        // so the visualizer returns to normal behavior. Also resets audio capture type.
        // Update 2153 AudioCaptureManager.DisableSimulatedBPM

        // Update 2160 AudioCaptureManager.DisableSimulatedBPM
        // Purpose: Call CleanupSimulatedTextures() when leaving Simulated BPM mode
        // to prevent RenderTexture / Texture2D memory leaks.
        public void DisableSimulatedBPM()
        {
            if (m_Type != AudioCaptureType.SimulatedBPM)
                return;

            if (VisualizerManager.m_Instance != null)
            {
                VisualizerManager.m_Instance.CleanupSimulatedTextures();
                VisualizerManager.m_Instance.SetSimulatedBPM(128f, false);
            }

            Debug.Log("[AudioCaptureManager] Simulated BPM mode DISABLED.");
            App.Instance.AudioReactiveBrushesActive(false);
            ResetAudioCaptureType();
        }

        // Update 2116 AudioCaptureManager.DisableCurrentMode
        private void DisableCurrentMode()
        {
            switch (m_Type)
            {
                case AudioCaptureType.Script:
                    DisableScripting();
                    break;

                case AudioCaptureType.SimulatedBPM:
                    // Only reset internal state here — do not touch m_EnableSimulatedBPM
                    // (the checkbox is controlled by CaptureAudio or explicit DisableSimulatedBPM)
                    if (VisualizerManager.m_Instance != null)
                    {
                        VisualizerManager.m_Instance.SetSimulatedBPM(128f, false);
                    }
                    break;
            }
        }





        public void EnableAudioFileSource(bool enable, string path)
        {
            if (enable)
            {
                LuaManager.Instance.VisualizerScriptingEnabled = false;
                m_AppAudio.SetActive(false);
                m_SystemAudio.Deactivate();
                m_SystemAudio.gameObject.SetActive(false);
                m_Type = AudioCaptureType.File;
                StartCoroutine(LoadAudio(path));
            }
            else
            {
                ResetAudioCaptureType();
            }
        }

        IEnumerator LoadAudio(string path)
        {
            using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip("file://" + path, AudioType.UNKNOWN))
            {
                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success)
                {
                    var audioSource = m_FileAudio.GetComponent<AudioSource>();
                    AudioClip audioClip = DownloadHandlerAudioClip.GetContent(www);
                    audioSource.clip = audioClip;
                    audioSource.Play();
                }
                else
                {
                    Debug.LogError("Failed to load audio: " + www.error);
                }
            }
        }

        // Updated to handle SimulatedBPM
        public int SampleRate
        {
            get
            {
                switch (m_Type)
                {
                    case AudioCaptureType.File:
                        // TODO: Define this.
                        return 0;
                    case AudioCaptureType.System:
                        return m_SystemAudio.GetAudioDeviceSampleRate();
                    case AudioCaptureType.App:
                        return AudioSettings.outputSampleRate;
                    case AudioCaptureType.Script:
                        return LuaManager.Instance.ScriptedWaveformSampleRate;
                    case AudioCaptureType.SimulatedBPM:
                        return 44100;
                }
                return 0;
            }
        }

        public bool IsSimulatedBPMModeActive
        {
            get { return m_Type == AudioCaptureType.SimulatedBPM; }
        }

        // Updated for BPM Simulation
        public bool IsCapturingAudio
        {
            get
            {
                switch (m_Type)
                {
                    case AudioCaptureType.File:
                        return m_FileAudio.activeSelf;

                    case AudioCaptureType.System:
                        return m_SystemAudio.gameObject.activeSelf && m_SystemAudio.AudioDeviceSelected();

                    case AudioCaptureType.App:
                        return m_AppAudio.activeSelf;

                    case AudioCaptureType.Script:
                        return LuaManager.Instance.VisualizerScriptingEnabled;

                    case AudioCaptureType.SimulatedBPM:
                        return true;

                    default:
                        return false;
                }
            }
        }



        public string GetCaptureStatusMessage()
        {
            switch (m_Type)
            {
                case AudioCaptureType.File: return "Listening to Mic'";
                case AudioCaptureType.System: return m_SystemAudio.GetCaptureStatusMessage();
                case AudioCaptureType.App: return "Jammin'";
                case AudioCaptureType.Script: return "Scripted Waveform";
            }
            return "";
        }


        // Update 2119 AudioCaptureManager.CaptureAudio
        // Purpose: Removed now-redundant diagnostic log that fired on every toggle-off.
        // The core logic is stable and the message was no longer providing value.
        public void CaptureAudio(bool bCapture)
        {
            bool bWasRequested = CaptureRequested;
            m_CaptureRequestedCount += bCapture ? 1 : -1;
            Debug.Assert(m_CaptureRequestedCount >= 0);
            if (bCapture)
            {
                // =============================================================
                // ROUTE A: OFFLINE COMMAND-LINE RENDER FLOW
                // =============================================================
                if (App.Config != null && App.Config.OfflineRender)
                {
                    if (m_OfflineAudioMode == OfflineAudioRenderMode.Force_Simulated_BPM)
                    {
                        EnableSimulatedBPM(m_SimulatedBPM);
                        return;
                    }

                    // If set to pre-recorded file data, bypass any live hardware checks headlessly
                    if (m_OfflineAudioMode == OfflineAudioRenderMode.Use_PreRecorded_Data)
                    {
                        return;
                    }
                }
                // =============================================================
                // ROUTE B: LIVE INTERACTIVE IN-GAME FLOW
                // =============================================================
                else
                {
                    if (m_OnlineAudioMode == OnlineAudioPreviewMode.Force_Simulated_BPM)
                    {
                        EnableSimulatedBPM(m_SimulatedBPM);
                        return;
                    }
                }

                // Fall through to Standard Hardware Audio handling if no simulation overrides are active
                switch (m_Type)
                {
                    case AudioCaptureType.File:
                        m_FileAudio.SetActive(true);
                        m_FileAudio.GetComponent<VisualizerScript>().Activate(true);
                        break;
                    case AudioCaptureType.System:
                        if (!bWasRequested && CaptureRequested)
                        {
                            AudioManager.m_Instance.StopAudio();
                            AudioManager.Enabled = false;
                            PointerManager.m_Instance.ResetPointerAudio();
                            m_SystemAudio.gameObject.SetActive(true);
                            m_SystemAudio.Activate(DEVICE_SEARCH_DELAY);
                        }
                        break;
                    case AudioCaptureType.App:
                        m_AppAudio.SetActive(true);
                        break;
                    case AudioCaptureType.Script:
                        break;
                }
            }
            else
            {
                // =============================================================
                // DUAL-PASS INTERACTIVE PROTECTION GUARD
                // =============================================================
                // If we are actively running an interactive session inside the player,
                // and the camera rig is currently executing Pass 1 (Calibration Recording),
                // FORCE the hardware audio stream to stay alive so it can bake to disk safely.
                if (!App.Config.OfflineRender && CameraPathCaptureRig.m_Instance != null)
                {
                    if (CameraPathCaptureRig.m_Instance.IsFirstPassActive)
                    {
                        Debug.LogError("[Audio Pipeline] Guard Active: Preserving live sound card streams during Pass 1 transition sequence.");
                        return;
                    }
                }
                // =============================================================

                if (m_Type == AudioCaptureType.SimulatedBPM)
                {
                    m_Type = AudioCaptureType.System;
                    return;
                }
                switch (m_Type)
                {
                    case AudioCaptureType.File:
                        m_FileAudio.SetActive(false);
                        m_FileAudio.GetComponent<VisualizerScript>().Activate(false);
                        break;
                    case AudioCaptureType.System:
                        if (bWasRequested && !CaptureRequested)
                        {
                            AudioManager.Enabled = true;
                            PointerManager.m_Instance.ResetPointerAudio();
                            m_SystemAudio.Deactivate();
                            m_SystemAudio.gameObject.SetActive(false);
                        }
                        break;
                    case AudioCaptureType.App:
                        m_AppAudio.SetActive(false);
                        break;
                    case AudioCaptureType.Script:
                        break;
                }
            }
        }

        // =================================================================
        // TWO-PASS HIGH-FIDELITY AUTOMATION REPLAY RECORDER MODULE
        // =================================================================
        private AudioClip m_BackgroundAudioClip;
        private int m_AudioCaptureStartSampleIdx;
        private string m_ActiveCaptureOutputFilePath;

        // File: AudioCaptureManager.cs
        // === MASTER PARALLEL NATIVE AUDIO FILTERS PIPELINE FIELDS ===
        private System.Collections.Generic.List<float> m_ParallelHiFiAudioSamplesBuffer;
        private bool m_IsParallelHiFiAudioRecordingActive = false;
        private int m_ParallelHiFiChannelsCount = 2;



        /// <summary>
        /// AudioCaptureManager.StartHiFiAudioRecording
        /// Initializes an isolated internal audio capture listener pool in system memory, 
        /// dynamically pre-allocating a 5-minute safety runway at maximum sample capacity
        /// to completely eliminate mid-performance dynamic resizing headset lag spikes.
        /// </summary>
        public void StartHiFiAudioRecording(string videoOutputFilePath)
        {
            // FORCE ENFORCEMENT HOOK: If the camera path rig commands an uncompressed pass initialization,
            // we must force-enable the high-fidelity capture state system-wide to prevent initialization skips.
            m_EnableHighFidelityCapture = true;

            if (!m_EnableHighFidelityCapture) return;
            m_ActiveCaptureOutputFilePath = videoOutputFilePath;

            // Automatically detect channels count or default to high-fidelity stereo (2)
            int internalChannelsTarget = m_ParallelHiFiChannelsCount > 0 ? m_ParallelHiFiChannelsCount : 2;

            // Dynamic 5-Minute Capacity Formula: SampleRate * Channels * 60 seconds * 5 minutes
            int optimizedFiveMinuteCapacity = m_AudioCaptureSampleRate * internalChannelsTarget * 60 * 5;

            // --- CRITICAL MEMORY FOOTPRINT SAFE LOCK ---
            try
            {
                if (m_ParallelHiFiAudioSamplesBuffer == null)
                {
                    m_ParallelHiFiAudioSamplesBuffer = new System.Collections.Generic.List<float>(optimizedFiveMinuteCapacity);
                }
                else
                {
                    m_ParallelHiFiAudioSamplesBuffer.Capacity = optimizedFiveMinuteCapacity;
                }
            }
            catch (System.OutOfMemoryException ramEx)
            {
                UnityEngine.Debug.LogError($"[Audio Pipeline] FATAL: Workstation RAM allocation exhausted during pre-allocation setup loop: {ramEx.Message}. Dropping high-fidelity parallel capture safety checks.");
                m_EnableHighFidelityCapture = false;
                m_IsParallelHiFiAudioRecordingActive = false;
                return;
            }
            // -------------------------------------------

            m_ParallelHiFiAudioSamplesBuffer.Clear();

            // WAKE-UP HANDSHAKE: Forcefully flip your intercept flag to open the native audio listener thread loop
            m_IsParallelHiFiAudioRecordingActive = true;

            UnityEngine.Debug.LogError($"[Audio Pipeline] SUCCESS: Engaged native Unity audio thread listener loop capture pass. Pre-allocated capacity: {optimizedFiveMinuteCapacity} floats.");
        }





        /// <summary>
        /// AudioCaptureManager.OnAudioFilterRead
        /// Native Unity audio thread intercept hook. Thread-safely captures raw engine mixer
        /// PCM floating-point data streams before outputting to the physical workstation sound card.
        /// </summary>
        private void OnAudioFilterRead(float[] data, int channels)
        {
            // ====================================================================
            // CRITICAL DIAGNOSTIC PLACEMENT
            // ====================================================================
            // Increment and log at the absolute entry gate so we capture native
            // mixer activity even when the recording flag is false or the buffer
            // is not yet allocated. This distinguishes "callback never runs" from
            // "callback runs but gate is closed / buffer missing".
            m_DiagnosticThreadTickCount++;
            if (m_DiagnosticThreadTickCount % 100 == 0)
            {
                UnityEngine.Debug.LogError(
                    $"[Audio Thread Entry] Native mixer loop invoked. Total Ticks: {m_DiagnosticThreadTickCount} | " +
                    $"Active Flag: {m_IsParallelHiFiAudioRecordingActive} | " +
                    $"Buffer Allocated: {(m_ParallelHiFiAudioSamplesBuffer != null ? "YES" : "NO")} | " +
                    $"Current Buffer Count: {(m_ParallelHiFiAudioSamplesBuffer != null ? m_ParallelHiFiAudioSamplesBuffer.Count.ToString() : "N/A")}");
            }

            // Thread-safe fast optimization gate
            if (!m_IsParallelHiFiAudioRecordingActive || m_ParallelHiFiAudioSamplesBuffer == null)
            {
                return;
            }

            m_ParallelHiFiChannelsCount = channels;

            // --- ATOMIC THREAD BLOCK REGISTRY & WORKER SAFETY FENCE ---
            try
            {
                lock (m_AudioStreamLock)
                {
                    for (int i = 0; i < data.Length; i++)
                    {
                        m_ParallelHiFiAudioSamplesBuffer.Add(data[i]);
                    }
                }
            }
            catch (System.Exception threadEx)
            {
                // Forces a soft shutdown gate inside the background pool to prevent a raw hardware crash dump
                m_IsParallelHiFiAudioRecordingActive = false;
                UnityEngine.Debug.LogError($"[Audio Pipeline] Background worker thread collision intercepted inside mixer pipeline loop: {threadEx.Message}. Thread detached safely.");
            }
            // ----------------------------------------------------------
        }



        /// <summary>
        /// AudioCaptureManager.StopAndSaveHiFiAudioTrack
        /// Terminates the parallel background thread capture pass, thread-safely locks and trims 
        /// the memory buffers, pads out undershoots, and writes a valid standalone WAV file to disk.
        /// Retrofited to use multiple steps
        /// </summary>

        // ====================================================================
        // REFACTORED MAIN ENTRANCE HANDSHAKE
        // ====================================================================
        // targetFrameCount: master visual length N (duration × fps).
        // actualFramesWritten: pre-trim exporter count (diagnostic until HiFi runs after StopVideoCapture).
        // fps: locked Pass 2 capture fps from CameraPathCaptureRig.PerformStep10 (liveCaptureFPS).
        // Stored in m_TimelineSyncFps for AudioCaptureManager.SynchronizeTimelineData (Phase 1 length lock).
        public void StopAndSaveHiFiAudioTrack(int targetFrameCount = -1, int actualFramesWritten = -1, int fps = -1)
        {
            UnityEngine.Debug.LogError(
                "[Audio Pipeline] StopAndSaveHiFiAudioTrack master handshake sequence called. " +
                $"targetFrames={targetFrameCount} | actualFrames={actualFramesWritten} | fps={fps}");
            // Lock fps for SynchronizeTimelineData. Invalid values fall back to 30 and are logged.
            if (fps > 0)
            {
                m_TimelineSyncFps = fps;
            }
            else
            {
                m_TimelineSyncFps = 30;
                UnityEngine.Debug.LogError(
                    "[Audio Pipeline] StopAndSaveHiFiAudioTrack: fps <= 0; using fallback m_TimelineSyncFps=30. " +
                    "Caller should pass liveCaptureFPS from CameraPathCaptureRig.PerformStep10.");
            }
            // Execute the isolated steps sequentially
            SnapshotAudioStream();
            SynchronizeTimelineData(targetFrameCount, actualFramesWritten);
            WriteWavTrackToDisk();
        }

        // ====================================================================
        // DISCRETE STEP 1: ATOMIC AUDIO THREAD SNAPSHOT
        // ====================================================================
        // Always captures the full HiFi buffer. Pad-audio policy (m_AllowAudioDuringPadding)
        // is applied in SynchronizeTimelineData after length-lock to S_desired, using
        // CameraPathCaptureRig.SecondPassContentFrameCount — not wall-clock traversal trim.
        public void SnapshotAudioStream()
        {
            // DIAGNOSTIC HOOK 2: Exact buffer size at the moment Snapshot is entered,
            // before any flags are changed or the buffer is cleared.
            UnityEngine.Debug.LogError(
                $"[Snapshot Entry Check] Volatile buffer size before freeze call: {(m_ParallelHiFiAudioSamplesBuffer != null ? m_ParallelHiFiAudioSamplesBuffer.Count : -1)} | Diagnostic ticks seen: {m_DiagnosticThreadTickCount}");

            if (!m_EnableHighFidelityCapture)
            {
                UnityEngine.Debug.LogError("[Audio Pipeline] Snapshot aborted: High-Fidelity capture configuration flag is disabled.");
                return;
            }

            if (!m_IsParallelHiFiAudioRecordingActive)
            {
                UnityEngine.Debug.LogError("[Audio Pipeline] Snapshot aborted: Parallel thread intercept gates are already dark or inactive.");
                return;
            }

            // Instantly terminate the background intercept stream to freeze memory allocations
            m_IsParallelHiFiAudioRecordingActive = false;

            lock (m_AudioStreamLock)
            {
                if (m_ParallelHiFiAudioSamplesBuffer == null || m_ParallelHiFiAudioSamplesBuffer.Count == 0)
                {
                    UnityEngine.Debug.LogError("[Audio Pipeline] Snapshot FATAL: The multi-threaded parallel floating-point buffer is completely empty.");
                    m_IsolatedSnapshotSamples = null;
                    m_SnapshotRawCount = 0;
                    return;
                }

                m_SnapshotFinalizedChannels = m_ParallelHiFiChannelsCount > 0 ? m_ParallelHiFiChannelsCount : 2;
                m_SnapshotRawCount = m_ParallelHiFiAudioSamplesBuffer.Count;

                int targetSamplesCount = m_SnapshotRawCount;

                // Align blocks to channel size to prevent orphaned channel slots
                int alignmentRemainder = targetSamplesCount % m_SnapshotFinalizedChannels;
                if (alignmentRemainder > 0)
                {
                    targetSamplesCount -= alignmentRemainder;
                }

                m_IsolatedSnapshotSamples = new float[targetSamplesCount];
                for (int i = 0; i < targetSamplesCount; i++)
                {
                    m_IsolatedSnapshotSamples[i] = m_ParallelHiFiAudioSamplesBuffer[i];
                }

                // Flush original background memory allocation tracking instantly
                m_ParallelHiFiAudioSamplesBuffer.Clear();
            }

            UnityEngine.Debug.LogError(
                $"[Audio Pipeline] Step 1 Complete: Snapshot successfully captured {m_IsolatedSnapshotSamples.Length} samples from thread. " +
                $"AllowAudioDuringPadding={m_AllowAudioDuringPadding}");
        }



        // ====================================================================
        // DISCRETE STEP 1: ATOMIC AUDIO THREAD SNAPSHOT
        // ====================================================================
        /*
        public void SnapshotAudioStream()
        {
            // DIAGNOSTIC HOOK 2: Exact buffer size at the moment Snapshot is entered,
            // before any flags are changed or the buffer is cleared.
            UnityEngine.Debug.LogError(
                $"[Snapshot Entry Check] Volatile buffer size before freeze call: {(m_ParallelHiFiAudioSamplesBuffer != null ? m_ParallelHiFiAudioSamplesBuffer.Count : -1)} | Diagnostic ticks seen: {m_DiagnosticThreadTickCount}");

            if (!m_EnableHighFidelityCapture)
            {
                UnityEngine.Debug.LogError("[Audio Pipeline] Snapshot aborted: High-Fidelity capture configuration flag is disabled.");
                return;
            }
            if (!m_IsParallelHiFiAudioRecordingActive)
            {
                UnityEngine.Debug.LogError("[Audio Pipeline] Snapshot aborted: Parallel thread intercept gates are already dark or inactive.");
                return;
            }

            // Instantly terminate the background intercept stream to freeze memory allocations
            m_IsParallelHiFiAudioRecordingActive = false;

            lock (m_AudioStreamLock)
            {
                if (m_ParallelHiFiAudioSamplesBuffer == null || m_ParallelHiFiAudioSamplesBuffer.Count == 0)
                {
                    UnityEngine.Debug.LogError("[Audio Pipeline] Snapshot FATAL: The multi-threaded parallel floating-point buffer is completely empty.");
                    m_IsolatedSnapshotSamples = null;
                    m_SnapshotRawCount = 0;
                    return;
                }

                m_SnapshotFinalizedChannels = m_ParallelHiFiChannelsCount > 0 ? m_ParallelHiFiChannelsCount : 2;
                m_SnapshotRawCount = m_ParallelHiFiAudioSamplesBuffer.Count;
                int targetSamplesCount = m_SnapshotRawCount;

                // Apply timeline truncation checks safely inside isolated storage
                if (!m_AllowAudioDuringPadding)
                {
                    float traversalPercent = 100f;
                    if (TiltBrush.CameraPathCaptureRig.m_Instance != null)
                    {
                        traversalPercent = TiltBrush.CameraPathCaptureRig.m_Instance.SecondPassTraversalPercent;
                    }
                    float traversalRatio = UnityEngine.Mathf.Clamp01(traversalPercent / 100f);
                    float totalAudioBlocks = (float)targetSamplesCount / (float)m_SnapshotFinalizedChannels;
                    int truncatedBlockCount = UnityEngine.Mathf.RoundToInt(totalAudioBlocks * traversalRatio);
                    targetSamplesCount = truncatedBlockCount * m_SnapshotFinalizedChannels;

                    // Fallback Gate: If timeline reset early, preserve the raw unclipped sample pool
                    if (targetSamplesCount == 0 && m_SnapshotRawCount > 0)
                    {
                        targetSamplesCount = m_SnapshotRawCount;
                        UnityEngine.Debug.LogWarning("[Audio Pipeline] Snapshot Warning: Traversal percentage evaluated to 0. Bypassing truncation to protect buffer stream.");
                    }
                }

                // Align blocks to channel size to prevent DivideByZero / orphaned channel slots
                int alignmentRemainder = targetSamplesCount % m_SnapshotFinalizedChannels;
                if (alignmentRemainder > 0)
                {
                    targetSamplesCount -= alignmentRemainder;
                }

                m_IsolatedSnapshotSamples = new float[targetSamplesCount];
                for (int i = 0; i < targetSamplesCount; i++)
                {
                    m_IsolatedSnapshotSamples[i] = m_ParallelHiFiAudioSamplesBuffer[i];
                }

                // Flush original background memory allocation tracking instantly
                m_ParallelHiFiAudioSamplesBuffer.Clear();
            }

            UnityEngine.Debug.LogError($"[Audio Pipeline] Step 1 Complete: Snapshot successfully captured {m_IsolatedSnapshotSamples.Length} samples from thread.");
        }
        */

        // ====================================================================
        // DISCRETE STEP 2: TIMELINE TIMING SYNCHRONIZATION & PADDING
        // ====================================================================
        // Video is master: force the HiFi sample buffer to exactly
        //   S_desired = targetFrameCount * channels * (sampleRate / fps)
        // using locked m_TimelineSyncFps from StopAndSaveHiFiAudioTrack.
        // actualFramesWritten is diagnostic only (pre-trim exporter count).
        // Overshoot → hard trim tail. Undershoot → silent pad.
        //
        // Phase 3: If m_AllowAudioDuringPadding is false, after length-lock zero the
        // pad region (samples after content frame count). Content frame count comes from
        // CameraPathCaptureRig.SecondPassContentFrameCount (set at path complete).
        public void SynchronizeTimelineData(int targetFrameCount, int actualFramesWritten)
        {
            UnityEngine.Debug.LogError(
                $"[Audio Pipeline] Step 2 Init: Synchronizing timelines. " +
                $"Target: {targetFrameCount} | Actual(pre-trim): {actualFramesWritten} | " +
                $"fps={m_TimelineSyncFps} | AllowAudioDuringPadding={m_AllowAudioDuringPadding}");

            if (m_IsolatedSnapshotSamples == null || m_IsolatedSnapshotSamples.Length == 0)
            {
                UnityEngine.Debug.LogError("[Audio Pipeline] Step 2 FATAL: Cannot synchronize timeline. Snapshot array is null or empty.");
                return;
            }

            if (!m_SaveAudioTrackFile && targetFrameCount <= 0)
            {
                UnityEngine.Debug.LogError("[Audio Pipeline] Visuals-Only Mode detected on user cancel. Skipping synchronization modifications.");
                return;
            }

            if (targetFrameCount <= 0)
            {
                UnityEngine.Debug.LogError(
                    "[Audio Pipeline] Step 2: targetFrameCount <= 0; leaving snapshot length unchanged. " +
                    $"samples={m_IsolatedSnapshotSamples.Length}");
                return;
            }

            if (m_TimelineSyncFps <= 0)
            {
                UnityEngine.Debug.LogError(
                    "[Audio Pipeline] Step 2 FATAL: m_TimelineSyncFps <= 0; cannot length-lock. " +
                    "Ensure StopAndSaveHiFiAudioTrack was called with liveCaptureFPS.");
                return;
            }

            int channels = m_SnapshotFinalizedChannels > 0 ? m_SnapshotFinalizedChannels : 2;

            // Prefer live capture device rate; fall back to inspector HiFi rate.
            int sampleRate = SampleRate > 0 ? SampleRate : m_AudioCaptureSampleRate;
            if (sampleRate <= 0)
            {
                sampleRate = 48000;
                UnityEngine.Debug.LogError(
                    "[Audio Pipeline] Step 2: sampleRate was <= 0; using fallback 48000 Hz.");
            }

            // S_desired = N * channels * (sampleRate / fps), channel-aligned.
            long desiredSamplesLong =
                (long)targetFrameCount * (long)channels * (long)sampleRate / (long)m_TimelineSyncFps;
            int desiredSamples = (int)desiredSamplesLong;

            int alignmentRemainder = desiredSamples % channels;
            if (alignmentRemainder > 0)
            {
                desiredSamples -= alignmentRemainder;
            }

            if (desiredSamples <= 0)
            {
                UnityEngine.Debug.LogError(
                    $"[Audio Pipeline] Step 2 FATAL: desiredSamples computed as {desiredSamples}. " +
                    $"N={targetFrameCount} ch={channels} rate={sampleRate} fps={m_TimelineSyncFps}");
                return;
            }

            int currentSamples = m_IsolatedSnapshotSamples.Length;

            UnityEngine.Debug.LogError(
                $"[Audio Pipeline] Step 2 Math: currentSamples={currentSamples} | desiredSamples={desiredSamples} | " +
                $"channels={channels} | sampleRate={sampleRate} | fps={m_TimelineSyncFps} | " +
                $"targetFrames={targetFrameCount} | actualFrames(pre-trim)={actualFramesWritten}");

            // --- Length-lock to S_desired ---
            if (currentSamples > desiredSamples)
            {
                float[] trimmed = new float[desiredSamples];
                System.Array.Copy(m_IsolatedSnapshotSamples, 0, trimmed, 0, desiredSamples);
                m_IsolatedSnapshotSamples = trimmed;
                UnityEngine.Debug.LogError(
                    $"[Audio Pipeline] Step 2 Sync: overshot by {currentSamples - desiredSamples} samples. " +
                    $"Hard-trimmed tail to S_desired={desiredSamples}.");
            }
            else if (currentSamples < desiredSamples)
            {
                System.Collections.Generic.List<float> alignedSamplesList =
                    new System.Collections.Generic.List<float>(desiredSamples);
                alignedSamplesList.AddRange(m_IsolatedSnapshotSamples);
                int padCount = desiredSamples - currentSamples;
                for (int p = 0; p < padCount; p++)
                {
                    alignedSamplesList.Add(0f);
                }
                m_IsolatedSnapshotSamples = alignedSamplesList.ToArray();
                UnityEngine.Debug.LogError(
                    $"[Audio Pipeline] Step 2 Sync: undershot by {padCount} samples. " +
                    $"Appended silent padding to S_desired={desiredSamples}.");
            }
            else
            {
                UnityEngine.Debug.LogError("[Audio Pipeline] Step 2 Sync Success: sample count already matches S_desired.");
            }

            // --- Phase 3: silence pad region when audio must not play during padding ---
            if (!m_AllowAudioDuringPadding)
            {
                int contentFrameCount = 0;
                if (CameraPathCaptureRig.m_Instance != null)
                {
                    contentFrameCount = CameraPathCaptureRig.m_Instance.SecondPassContentFrameCount;
                }

                // Fallback: traversal % of target frames if content count was never set
                if (contentFrameCount <= 0 && CameraPathCaptureRig.m_Instance != null)
                {
                    contentFrameCount = UnityEngine.Mathf.RoundToInt(
                        targetFrameCount * (CameraPathCaptureRig.m_Instance.SecondPassTraversalPercent / 100f));
                    UnityEngine.Debug.LogError(
                        $"[Audio Pipeline] Step 2 Pad: ContentFrameCount was 0; fallback traversal-based contentFrames={contentFrameCount}");
                }

                if (contentFrameCount > 0 && contentFrameCount < targetFrameCount)
                {
                    long contentSamplesLong =
                        (long)contentFrameCount * (long)channels * (long)sampleRate / (long)m_TimelineSyncFps;
                    int contentSamples = (int)contentSamplesLong;
                    int contentAlign = contentSamples % channels;
                    if (contentAlign > 0)
                    {
                        contentSamples -= contentAlign;
                    }

                    if (contentSamples < 0)
                    {
                        contentSamples = 0;
                    }
                    if (contentSamples > m_IsolatedSnapshotSamples.Length)
                    {
                        contentSamples = m_IsolatedSnapshotSamples.Length;
                    }

                    int silenced = 0;
                    for (int i = contentSamples; i < m_IsolatedSnapshotSamples.Length; i++)
                    {
                        m_IsolatedSnapshotSamples[i] = 0f;
                        silenced++;
                    }

                    UnityEngine.Debug.LogError(
                        $"[Audio Pipeline] Step 2 Pad: AllowAudioDuringPadding=false | " +
                        $"contentFrames={contentFrameCount} | contentSamples={contentSamples} | " +
                        $"silencedPadSamples={silenced} | totalSamples={m_IsolatedSnapshotSamples.Length}");
                }
                else
                {
                    UnityEngine.Debug.LogError(
                        $"[Audio Pipeline] Step 2 Pad: AllowAudioDuringPadding=false but no pad region to silence " +
                        $"(contentFrames={contentFrameCount}, targetFrames={targetFrameCount}).");
                }
            }
        }



        // ====================================================================
        // DISCRETE STEP 3: FORMAT ENCODING AND STORAGE COMMIT
        // ====================================================================
        // Stamps the WAV header with the same capture sample rate used in
        // SynchronizeTimelineData (device rate → m_AudioCaptureSampleRate → 48000),
        // not AudioSettings.outputSampleRate, so header and PCM data agree.
        public void WriteWavTrackToDisk()
        {
            if (m_IsolatedSnapshotSamples == null || m_IsolatedSnapshotSamples.Length == 0)
            {
                UnityEngine.Debug.LogError("[Audio Pipeline] Step 3 FATAL: Disk serialization aborted. Target sample array data block is completely empty.");
                return;
            }

            // DISAMBIGUATION OVERRIDE GATE: Force a save if we are in an active, live audio capture session.
            // If we are in visuals-only mode OR an offline simulated BPM/pre-recorded replay run,
            // IsLiveAudioCaptureActive evaluates to false, letting the user cancellation flush run safely.
            bool shouldSaveWavFile = m_SaveAudioTrackFile || VideoRecorderUtils.IsLiveAudioCaptureActive;

            if (!shouldSaveWavFile)
            {
                UnityEngine.Debug.LogError("[Audio Pipeline] Visuals-Only or Offline Simulation active. Flushing isolated arrays from memory without writing file.");
                m_IsolatedSnapshotSamples = null;
                return;
            }

            int channels = m_SnapshotFinalizedChannels > 0 ? m_SnapshotFinalizedChannels : 2;

            // Match SynchronizeTimelineData: capture/WASAPI rate first, then inspector HiFi rate.
            int wavSampleRate = SampleRate > 0 ? SampleRate : m_AudioCaptureSampleRate;
            if (wavSampleRate <= 0)
            {
                wavSampleRate = 48000;
                UnityEngine.Debug.LogError(
                    "[Audio Pipeline] Step 3: sampleRate was <= 0; stamping WAV header with fallback 48000 Hz.");
            }

            UnityEngine.Debug.LogError(
                $"[Audio Pipeline] Step 3 Encode: samples={m_IsolatedSnapshotSamples.Length} | " +
                $"channels={channels} | wavSampleRate={wavSampleRate} | " +
                $"AudioSettings.outputSampleRate={UnityEngine.AudioSettings.outputSampleRate}");

            byte[] structuredWavBytes = ConvertPcmToWavFormat(
                m_IsolatedSnapshotSamples,
                channels,
                wavSampleRate);

            if (structuredWavBytes == null || structuredWavBytes.Length == 0)
            {
                UnityEngine.Debug.LogError("[Audio Pipeline] Step 3 FATAL: ConvertPcmToWavFormat factory returned a null or zero-length byte array payload container.");
                return;
            }

            string finalWavPath = System.IO.Path.ChangeExtension(m_ActiveCaptureOutputFilePath, ".wav");

            try
            {
                System.IO.File.WriteAllBytes(finalWavPath, structuredWavBytes);
            }
            catch (System.Exception ioEx)
            {
                UnityEngine.Debug.LogError($"[Audio Pipeline] Step 3 FATAL I/O EXCEPTION: Failed to write bytes to storage address: {ioEx.Message}");
                return;
            }

            UnityEngine.Debug.LogError(
                $"[Audio Pipeline] SUCCESS: Native High-Fidelity backing audio track saved cleanly to disk at {wavSampleRate}Hz: {finalWavPath}");

            // Clear out holding arrays to clean up RAM memory footprints
            m_IsolatedSnapshotSamples = null;
        }


        // ====================================================================
        // PUBLIC PROXY RELAY INTERFACES (THREAD-SAFE PROXY BRIDGE)
        // ====================================================================
        // Invoked by AudioFilterProxy on the Unity audio thread (mixer path).
        // The conditional check is nested inside the object locker to prevent
        // multi-threaded state race conditions during shutdown.
        public void AppendHiFiSamples(float[] data, int channels)
        {
            try
            {
                lock (m_AudioStreamLock)
                {
                    // Verify recording flags and memory allocations strictly inside the lock perimeter
                    if (!m_IsParallelHiFiAudioRecordingActive || m_ParallelHiFiAudioSamplesBuffer == null)
                    {
                        return;
                    }

                    // DIAGNOSTIC: Log received channel count and peak amplitude a few times
                    // at the start of the pass. Peak near zero proves the listener is receiving
                    // silence; a healthy peak (e.g. > 0.01) means capture is fine and the
                    // problem is elsewhere (encoding / playback).
                    if (m_ParallelHiFiAudioSamplesBuffer.Count < 48000) // roughly first second of audio
                    {
                        if (m_ParallelHiFiAudioSamplesBuffer.Count % 8000 < data.Length)
                        {
                            float peak = 0f;
                            for (int p = 0; p < data.Length; p++)
                            {
                                float a = data[p] < 0f ? -data[p] : data[p];
                                if (a > peak) peak = a;
                            }

                            UnityEngine.Debug.LogError(
                                $"[AppendHiFiSamples] Received channels={channels} | data.Length={data.Length} | " +
                                $"buffer size so far={m_ParallelHiFiAudioSamplesBuffer.Count} | peak={peak:F6}");
                        }
                    }

                    m_ParallelHiFiChannelsCount = channels;

                    for (int i = 0; i < data.Length; i++)
                    {
                        m_ParallelHiFiAudioSamplesBuffer.Add(data[i]);
                    }
                }
            }
            catch (System.Exception threadEx)
            {
                m_IsParallelHiFiAudioRecordingActive = false;
                UnityEngine.Debug.LogError(
                    $"[Audio Pipeline] Proxy bridge worker thread collision intercepted inside mixer pipeline loop: {threadEx.Message}. Thread detached safely.");
            }
        }





        // AudioCaptureManager.AppendHiFiSamplePair
        // Called from SystemAudioMonitor's WASAPI callback during two-pass HiFi only.
        // Early-outs when HiFi is not active so standard recording is unaffected.
        public void AppendHiFiSamplePair(float left, float right)
        {
            try
            {
                lock (m_AudioStreamLock)
                {
                    if (!m_IsParallelHiFiAudioRecordingActive || m_ParallelHiFiAudioSamplesBuffer == null)
                    {
                        return;
                    }

                    // DIAGNOSTIC: same peak gate as AppendHiFiSamples, for the first second
                    if (m_ParallelHiFiAudioSamplesBuffer.Count < 48000)
                    {
                        if (m_ParallelHiFiAudioSamplesBuffer.Count % 8000 < 2)
                        {
                            float peak = left < 0f ? -left : left;
                            float ar = right < 0f ? -right : right;
                            if (ar > peak) peak = ar;
                            UnityEngine.Debug.LogError(
                                $"[AppendHiFiSamplePair] peak={peak:F6} | buffer size so far={m_ParallelHiFiAudioSamplesBuffer.Count}");
                        }
                    }

                    m_ParallelHiFiChannelsCount = 2;
                    m_ParallelHiFiAudioSamplesBuffer.Add(left);
                    m_ParallelHiFiAudioSamplesBuffer.Add(right);
                }
            }
            catch (System.Exception threadEx)
            {
                m_IsParallelHiFiAudioRecordingActive = false;
                UnityEngine.Debug.LogError(
                    $"[Audio Pipeline] SystemAudio HiFi bridge collision: {threadEx.Message}. Thread detached safely.");
            }
        }


        /// <summary>
        /// Binary structural serialization engine. Transforms raw floating point memory frames
        /// into a valid standalone little-endian RIFF/WAVE file byte stream array.
        /// </summary>

        /// <summary>
        /// AudioCaptureManager.ConvertPcmToWavFormat
        /// Transforms raw uncompressed floating point memory frames into a valid little-endian WAV byte stream array.
        /// Optimized with bulk byte-packing memory array allocations to eliminate main thread compaction freezes.
        /// </summary>
        private byte[] ConvertPcmToWavFormat(float[] samples, int channels, int sampleRate)
        {
            short bitsPerSample = 16;
            int bytesPerSample = bitsPerSample / 8;
            int totalAudioBytesCount = samples.Length * bytesPerSample;

            // Pre-allocate the entire target byte array buffer upfront to completely prevent dynamic MemoryStream resizing leaks
            byte[] targetOutputByteBuffer = new byte[44 + totalAudioBytesCount];

            // --- 1. HIGH-SPEED BINARY BITSTREAM PACKING CORE ---
            // Convert high-overhead 32-bit floats (-1.0 to 1.0 range) into tight 16-bit short integers inside RAM registers
            int byteWriteOffset = 44; // Start packing sample bytes immediately past the 44-byte RIFF formatting header block
            for (int i = 0; i < samples.Length; i++)
            {
                // Inlined safety clamping and scaling to keep processing speeds ultra-fast
                float sampleVal = samples[i];
                if (sampleVal < -1f) sampleVal = -1f;
                else if (sampleVal > 1f) sampleVal = 1f;

                short convertedSampleInt = (short)(sampleVal * 32767f);

                // Manual little-endian raw byte splitting to bypass BinaryWriter token loops
                targetOutputByteBuffer[byteWriteOffset] = (byte)(convertedSampleInt & 0xFF);
                targetOutputByteBuffer[byteWriteOffset + 1] = (byte)((convertedSampleInt >> 8) & 0xFF);
                byteWriteOffset += 2;
            }

            // --- 2. VALID RIFF/WAVE HEADER STAMPING BLOCK ---
            using (System.IO.MemoryStream headerStream = new System.IO.MemoryStream(targetOutputByteBuffer))
            {
                using (System.IO.BinaryWriter headerWriter = new System.IO.BinaryWriter(headerStream))
                {
                    // --- RIFF Descriptor ---
                    headerWriter.Write(new char[] { 'R', 'I', 'F', 'F' }); // ChunkID
                    headerWriter.Write((int)(36 + totalAudioBytesCount));   // ChunkSize
                    headerWriter.Write(new char[] { 'W', 'A', 'V', 'E' }); // Format

                    // --- Format Sub-chunk ---
                    headerWriter.Write(new char[] { 'f', 'm', 't', ' ' }); // Subchunk1ID
                    headerWriter.Write((int)16);                            // Subchunk1Size (16 for PCM linear integer coding)
                    headerWriter.Write((short)1);                           // AudioFormat (1 indicates uncompressed PCM data)
                    headerWriter.Write((short)channels);                    // NumChannels
                    headerWriter.Write((int)sampleRate);                    // SampleRate
                    headerWriter.Write((int)(sampleRate * channels * bytesPerSample)); // ByteRate
                    headerWriter.Write((short)(channels * bytesPerSample));            // BlockAlign
                    headerWriter.Write((short)bitsPerSample);               // BitsPerSample

                    // --- Data Sub-chunk ---
                    headerWriter.Write(new char[] { 'd', 'a', 't', 'a' }); // Subchunk2ID
                    headerWriter.Write((int)totalAudioBytesCount);          // Subchunk2Size
                }
            }

            return targetOutputByteBuffer; // Returns the pre-allocated buffer array directly, completely bypassing .ToArray() memory copies
        }


    


    }
} // end script




// ====================================================================
// DISCRETE STEP 2: TIMELINE TIMING SYNCHRONIZATION & PADDING
// ====================================================================
// Video is master: force the HiFi sample buffer to exactly
//   S_desired = targetFrameCount * channels * (sampleRate / fps)
// using locked m_TimelineSyncFps from StopAndSaveHiFiAudioTrack.
// actualFramesWritten is diagnostic only (pre-trim exporter count).
// Overshoot → hard trim tail. Undershoot → silent pad.
/*
public void SynchronizeTimelineData(int targetFrameCount, int actualFramesWritten)
{
    UnityEngine.Debug.LogError(
        $"[Audio Pipeline] Step 2 Init: Synchronizing timelines. " +
        $"Target: {targetFrameCount} | Actual(pre-trim): {actualFramesWritten} | " +
        $"fps={m_TimelineSyncFps}");

    if (m_IsolatedSnapshotSamples == null || m_IsolatedSnapshotSamples.Length == 0)
    {
        UnityEngine.Debug.LogError("[Audio Pipeline] Step 2 FATAL: Cannot synchronize timeline. Snapshot array is null or empty.");
        return;
    }

    if (!m_SaveAudioTrackFile && targetFrameCount <= 0)
    {
        UnityEngine.Debug.LogError("[Audio Pipeline] Visuals-Only Mode detected on user cancel. Skipping synchronization modifications.");
        return;
    }

    if (targetFrameCount <= 0)
    {
        UnityEngine.Debug.LogError(
            "[Audio Pipeline] Step 2: targetFrameCount <= 0; leaving snapshot length unchanged. " +
            $"samples={m_IsolatedSnapshotSamples.Length}");
        return;
    }

    if (m_TimelineSyncFps <= 0)
    {
        UnityEngine.Debug.LogError(
            "[Audio Pipeline] Step 2 FATAL: m_TimelineSyncFps <= 0; cannot length-lock. " +
            "Ensure StopAndSaveHiFiAudioTrack was called with liveCaptureFPS.");
        return;
    }

    int channels = m_SnapshotFinalizedChannels > 0 ? m_SnapshotFinalizedChannels : 2;

    // Prefer live capture device rate; fall back to inspector HiFi rate.
    int sampleRate = SampleRate > 0 ? SampleRate : m_AudioCaptureSampleRate;
    if (sampleRate <= 0)
    {
        sampleRate = 48000;
        UnityEngine.Debug.LogError(
            "[Audio Pipeline] Step 2: sampleRate was <= 0; using fallback 48000 Hz.");
    }

    // S_desired = N * channels * (sampleRate / fps), channel-aligned.
    long desiredSamplesLong =
        (long)targetFrameCount * (long)channels * (long)sampleRate / (long)m_TimelineSyncFps;
    int desiredSamples = (int)desiredSamplesLong;

    int alignmentRemainder = desiredSamples % channels;
    if (alignmentRemainder > 0)
    {
        desiredSamples -= alignmentRemainder;
    }

    if (desiredSamples <= 0)
    {
        UnityEngine.Debug.LogError(
            $"[Audio Pipeline] Step 2 FATAL: desiredSamples computed as {desiredSamples}. " +
            $"N={targetFrameCount} ch={channels} rate={sampleRate} fps={m_TimelineSyncFps}");
        return;
    }

    int currentSamples = m_IsolatedSnapshotSamples.Length;

    UnityEngine.Debug.LogError(
        $"[Audio Pipeline] Step 2 Math: currentSamples={currentSamples} | desiredSamples={desiredSamples} | " +
        $"channels={channels} | sampleRate={sampleRate} | fps={m_TimelineSyncFps} | " +
        $"targetFrames={targetFrameCount} | actualFrames(pre-trim)={actualFramesWritten}");

    if (currentSamples == desiredSamples)
    {
        UnityEngine.Debug.LogError("[Audio Pipeline] Step 2 Sync Success: sample count already matches S_desired.");
        return;
    }

    if (currentSamples > desiredSamples)
    {
        // Hard trim overflow (wall-clock HiFi longer than N/fps video container).
        float[] trimmed = new float[desiredSamples];
        System.Array.Copy(m_IsolatedSnapshotSamples, 0, trimmed, 0, desiredSamples);
        m_IsolatedSnapshotSamples = trimmed;
        UnityEngine.Debug.LogError(
            $"[Audio Pipeline] Step 2 Sync: overshot by {currentSamples - desiredSamples} samples. " +
            $"Hard-trimmed tail to S_desired={desiredSamples}.");
        return;
    }

    // Undershoot: silent pad to S_desired.
    System.Collections.Generic.List<float> alignedSamplesList =
        new System.Collections.Generic.List<float>(desiredSamples);
    alignedSamplesList.AddRange(m_IsolatedSnapshotSamples);
    int padCount = desiredSamples - currentSamples;
    for (int p = 0; p < padCount; p++)
    {
        alignedSamplesList.Add(0f);
    }
    m_IsolatedSnapshotSamples = alignedSamplesList.ToArray();
    UnityEngine.Debug.LogError(
        $"[Audio Pipeline] Step 2 Sync: undershot by {padCount} samples. " +
        $"Appended silent padding to S_desired={desiredSamples}.");
}
*/