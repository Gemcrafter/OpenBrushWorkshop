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


using UnityEngine;
using System.Collections.Generic;
using Reaktion;

namespace TiltBrush
{
    //
    // Audio Analysis data available for shading
    // _WaveFormTex.r = waveform
    // _WaveFormTex.g = waveform smoothed
    // _WaveFormTex.b = waveform low pass
    // _WaveFormTex.a = waveform high pass
    //
    // _FFTTex.r = FFT
    // _FFTTex.g = FFT with Power Curve
    // _FFTTex.b = Peak FFT with Power Curve
    // _FFTTex.a = Normalized Band Pass Levels
    //
    // _BeatOutput.xyzw = beat detection from Reaktor (channels 1,2,3,4 are from reaktor, reaktor alt, reaktor low pass, reaktor high pass)
    // _BeatOutputAccum.xyzw = accumulated beat detection from Reaktor (channels 1,2,3,4 are from reaktor, reaktor alt, reaktor low pass, reaktor high pass)
    // _AudioVolume.xyzw = Real time RMS volume (channels 1,2,3,4 are from reaktor, reaktor alt, reaktor low pass, reaktor high pass)
    // _PeakBandLevels.xyzw = m_BandNormalizedLevels[0], m_BandNormalizedLevels[1], m_BandNormalizedLevels], m_BandNormalizedLevels[3],
    //


    public class VisualizerManager : MonoBehaviour
    {
        public class Fft
        {
            public virtual void Add(float[] samples, int count) { }
            public virtual void GetFftData(float[] resultBuffer) { }
        }

        public class Filter
        {
            public virtual void Process(float[] samples) { }
            public virtual double Frequency { get; set; }
        }

        static public VisualizerManager m_Instance;

        [Header("Band Levels")]
        [Tooltip("Decay rate for band peak levels over time.")]
        [SerializeField] private float m_BandPeakDecay = .9f;

        [Tooltip("Lerp speed when normalizing band peak levels.")]
        [SerializeField] private float m_NormalizedBandPeakLerp = .9f;

        [Header("FFT")]
        [Tooltip("Decay rate for FFT peak values.")]
        [SerializeField] private float m_FFTPeakDecay = .9f;

        [Tooltip("Overall scaling applied to FFT data.")]
        [SerializeField] private float m_FFTScale = 2.0f;

        [Tooltip("Power curve scaling applied to FFT before display.")]
        [SerializeField] private float m_FFTPowerScale = 2.0f;

        [Tooltip("Exponent used in the FFT power curve.")]
        [SerializeField] private float m_FFTPower = 1.5f;

        [Header("Waveform")]
        [Tooltip("Lerp factor used for the 'weird' waveform effect.")]
        [SerializeField] private float m_WeirdWaveformLerp = .7f;

        [Tooltip("Cutoff frequency for the high-pass filter.")]
        [SerializeField] private double m_HighPassFreq = 2000;

        [Tooltip("Cutoff frequency for the low-pass filter.")]
        [SerializeField] private double m_LowPassFreq = 150;

        [Header("Reaktor")]
        [Tooltip("Primary system audio injector used by Reaktor.")]
        [SerializeField] private SystemAudioInjector m_SystemAudioInjector;

        [Tooltip("Alternative system audio injector (used for different frequency bands).")]
        [SerializeField] private SystemAudioInjector m_SystemAudioInjectorAlt;

        [Tooltip("Low-pass filtered system audio injector.")]
        [SerializeField] private SystemAudioInjector m_SystemAudioInjectorLowPass;

        [Tooltip("High-pass filtered system audio injector.")]
        [SerializeField] private SystemAudioInjector m_SystemAudioInjectorHighPass;

        /// <summary>
        /// Simulated BPM Reactivity Tuning
        /// ----------------------------------------------------------------
        /// m_ReactivityStrength      : Overall intensity of beat + amplitude (main knob)
        /// m_MaxReactivityStrength   : Upper safety limit to prevent accidental extreme values
        /// m_BeatSharpness           : Controls how fast and punchy the beat pulse reacts
        /// m_AmplitudeModAmount      : Strength of the gentle amplitude wobble
        /// m_FftReactivityMultiplier : Separate multiplier for fake FFT and Waveform textures
        /// m_BeatAccumScale          : Scale of the accumulated beat value (_BeatOutputAccum)
        /// </summary>
        [Header("Simulated BPM - Reactivity Tuning")]
        [Tooltip("Overall intensity of beat + amplitude. This is the main reactivity strength knob.")]
        [SerializeField] private float m_ReactivityStrength = 1.0f;

        [Tooltip("Upper safety limit for m_ReactivityStrength. Prevents accidentally setting extreme values.")]
        [SerializeField] private float m_MaxReactivityStrength = 10f;

        [Tooltip("Controls how fast and punchy the beat pulse reacts. Higher = sharper reaction.")]
        [SerializeField] private float m_BeatSharpness = 5.0f;

        [Tooltip("Strength of the gentle amplitude wobble. Affects brushes that respond more to amplitude than raw beat.")]
        [SerializeField] private float m_AmplitudeModAmount = 0.5f;

        [Tooltip("Separate multiplier for the fake FFT and Waveform textures (_FFTTex / _WaveFormTex).")]
        [SerializeField] private float m_FftReactivityMultiplier = 1.0f;

        [Tooltip("Scale of the accumulated beat value (_BeatOutputAccum). Used by some brushes like Rainbow.")]
        [SerializeField] private float m_BeatAccumScale = 0.08f;

        [Tooltip("Multiplier that specifically controls the strength of simulated _AudioVolume (loudness).")]
        [SerializeField] private float m_VolumeMultiplier = 1.0f;

        [Header("Simulated Band Levels (Techno Tuning)")]
        [Tooltip("Multiplier for the lowest frequency band (bass/kick). Higher = stronger bass response.")]
        [SerializeField] private float m_BandLowMultiplier = 1.0f;

        [Tooltip("Multiplier for low-mid frequencies.")]
        [SerializeField] private float m_BandLowMidMultiplier = 0.90f;

        [Tooltip("Multiplier for mid frequencies.")]
        [SerializeField] private float m_BandMidMultiplier = 0.55f;

        [Tooltip("Multiplier for high-mid frequencies. Lower this for more bass-dominant techno feel.")]
        [SerializeField] private float m_BandHighMidMultiplier = 0.30f;

        [Header("Simulated BPM - Beat Pulse")]
        [Tooltip("Controls how wide/long each beat pulse lasts. Higher values = wider pulse (more frames with visible reactivity). Lower values = narrower, more percussive pulses. Works together with Beat Sharpness.")]
        [SerializeField] private float m_BeatPulseWidth = 1.0f;


        [Header("Simulated BPM - Accumulation")]
        [Tooltip("How fast the accumulated beat (BeatOutputAccum) builds up and decays. Higher = faster response.")]
        [SerializeField] private float m_AccumLerpSpeed = 0.08f;


        /// <summary>
        ///  phase tracking channels for FFT impacts
        /// </summary>
        [Header("Simulated FFT Texture - Channel Strengths")]
        [Tooltip("Red channel multiplier. Primarily controls low/bass energy in the fake FFT texture.")]
        [SerializeField] private float m_FftChannelR_Strength = 1.0f;

        [Tooltip("Green channel multiplier. Primarily controls mid-range frequencies in the fake FFT texture.")]
        [SerializeField] private float m_FftChannelG_Strength = 0.9f;

        [Tooltip("BLUE channel multiplier. This is the most important channel for the WaveformFFT brush (it reads _FFTTex.b). Increase this to make WaveformFFT more reactive.")]
        [SerializeField] private float m_FftChannelB_Strength = 2.2f;

        [Tooltip("Alpha channel multiplier. Affects overall/combined energy across all frequencies in the fake FFT texture.")]
        [SerializeField] private float m_FftChannelA_Strength = 1.0f;

        [Header("Simulated FFT - Blue Channel Behavior (WaveformFFT)")]
        [Tooltip("Controls how sharp and percussive the blue channel pulses are. Higher values = more spiky, aggressive reactivity on WaveformFFT.")]
        [SerializeField] private float m_FftBlueChannelSpikeSharpness = 9.0f;

        [Tooltip("Controls how many pulses per beat the blue channel produces. Higher = faster, more frequent spikes.")]
        [SerializeField] private float m_FftBlueChannelFrequency = 5.5f;


        [Header("Simulated Waveform Texture - Channel Strengths")]
        [Tooltip("Red channel = Raw waveform. Many brushes read this for basic reactivity.")]
        [SerializeField] private float m_WaveformChannelR_Strength = 1.0f;

        [Tooltip("Green channel = Smoothed waveform. Affects brushes that prefer gentler movement.")]
        [SerializeField] private float m_WaveformChannelG_Strength = 1.0f;

        [Tooltip("Blue channel = Low-pass (bass-heavy) waveform.")]
        [SerializeField] private float m_WaveformChannelB_Strength = 1.0f;

        [Tooltip("Alpha channel = High-pass (treble-heavy) waveform.")]
        [SerializeField] private float m_WaveformChannelA_Strength = 1.0f;

        [Header("Two-Pass Offline Cache (Scalars)")]
        private System.Collections.Generic.List<BakedAudioFrame> m_BakedRecordingFrames = new System.Collections.Generic.List<BakedAudioFrame>();
        private bool m_IsPlaybackModeActive = false;

        // === PUBLIC LIFE-CYCLE INTERFACE FOR EXTERNAL CAMERA RIG CORRELATION ===
        public bool IsPlaybackModeActive
        {
            get { return m_IsPlaybackModeActive; }
        }



        private int m_FFTSize = 512;
        private int m_SampleRate;

        private float[] m_Bands = new float[] { 31.5f, 63, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };
        private float m_Bandwidth = 1.414f;
        private float[] m_BandLevels;
        private float[] m_BandPeakLevels;
        private float[] m_BandNormalizedLevels;

        private Texture2D m_WaveFormTexture;
        private Color[] m_WaveFormRow;
        private Texture2D m_FFTTexture;
        private int m_FFTTextureSize = 256;
        private Color[] m_FFTRow;

        private Vector4 m_BandPeakLevelsOutput;
        private Vector4 m_BeatOutput;
        private Vector4 m_BeatOutputAccum;
        private Vector4 m_AudioVolume;

        private Fft m_FFT;
        private Filter m_LowPassFilter;
        private Filter m_HighPassFilter;

        private float[] m_AudioSamples;
        private float[] m_LChannelWeird;
        private float[] m_LChannelHighPass;
        private float[] m_LChannelLowPass;
        private float[] m_FFTResult;
        private float[] m_PeakFFTResult;

        private Reaktor m_Reaktor;
        private Reaktor m_ReaktorAlt;
        private Reaktor m_ReaktorLowPass;
        private Reaktor m_ReaktorHighPass;

        private List<GameObject> m_VisualizerObjects;
        private int m_VisualsRequestCount;
        private bool m_VisualsActive;

        private bool m_UseSimulatedBPM = false;
        private float m_SimulatedBPM = 128f;
        private Vector4 m_SimulatedBeatOutput;
        private Vector4 m_SimulatedBeatOutputAccum;
        private Vector4 m_SimulatedBandLevels;
        private Vector4 m_SimulatedAudioVolume;  //Update 2175 

        // ScriptName.FieldName: VisualizerManager.cs
        private Vector4 m_TwoPassOvershootAccumulator = Vector4.zero;
        private float m_LastTwoPassProgress = 0f;


        private Texture2D m_SimulatedFFTTempTex;
        private Texture2D m_SimulatedWaveFormTempTex;
        private bool m_SimulatedTexturesInitialized = false;

        private RenderTexture m_SimulatedFFTTex;
        private RenderTexture m_SimulatedWaveFormTex;

        private const int FFT_TEXTURE_WIDTH = 256;
        private const int FFT_TEXTURE_HEIGHT = 1;
        private const int WAVEFORM_TEXTURE_WIDTH = 256;
        private const int WAVEFORM_TEXTURE_HEIGHT = 1;

        //  private float m_SimulatedBeatAccum;  // no longer used we aer using the vector 4 version now
        private bool m_HasLoggedSimulatedBPMDiagnostic = false;

        // Scratch memory buffers to prevent runtime unmanaged garbage pile-ups
        private Texture2D m_TwoPassFftScratch;
        private Texture2D m_TwoPassWaveScratch;

        // File: VisualizerManager.cs
        // Place this single tracking flag at the top of your variable block:
        private bool m_IsTwoPassCleanupDone = false;

        // =================================================================
        // TWO-PASS OFFLINE CACHE EXPOSED METHODS (PUBLIC ACCESS)
        // =================================================================

        [System.Serializable]
        public struct BakedAudioFrame
        {
            public float normalizedProgress;      // Progress key along the camera path (0.0 to 1.0)
            public Vector4 beatOutput;            // Saved m_SimulatedBeatOutput
            public Vector4 beatOutputAccum;       // Saved m_SimulatedBeatOutputAccum
            public Vector4 bandPeakLevels;        // Saved m_SimulatedBandLevels
            public Vector4 audioVolume;           // Saved m_SimulatedAudioVolume
            public Color[] fftTexturePixels;       // Snapshot pixel byte array for _FFTTex
            public Color[] waveFormTexturePixels;  // Snapshot pixel byte array for _WaveFormTex
        }




        // =====================================================================
        // PUBLIC PROPERTIES (used by custom inspector + other systems)
        // These now automatically return simulated data when Simulated BPM mode is active.
        // =====================================================================

        // Update 2187 VisualizerManager - Public Properties
        // Purpose: Make the Visualization Data panel show our simulated data
        // when "Enable Simulated BPM" is active. Falls back to normal real-audio
        // data when Simulated BPM mode is off.
        public int FFTSize { get { return m_FFTSize; } }

        // Update 2188 VisualizerManager - Public Properties (Fixed RenderTexture issue)
        // Purpose: Return Texture (base class) instead of Texture2D so we can return
        // either the real Texture2D or the simulated RenderTexture without compile errors.
        // The Visualization Data panel still works because it accepts Texture.
        // Update 2189 VisualizerManager - Public Properties (Inspector-safe version)
        // Purpose: Always return a valid Texture2D to the Visualization Data panel
        // so it doesn't crash or go blank. When Simulated BPM is active we still
        // inject the simulated RenderTextures to the shaders (via InjectSimulatedDataToShaders).
        public Texture WaveformTexture
        {
            get
            {
                // Always return the real Texture2D for the inspector to avoid null crashes.
                // The actual simulated data is still written to shaders via InjectSimulatedDataToShaders().
                return m_WaveFormTexture;
            }
        }

        public Texture FFTTexture
        {
            get
            {
                // Always return the real Texture2D for the inspector.
                return m_FFTTexture;
            }
        }

        public Vector4 BandPeakLevelsOutput
        {
            get
            {
                if (AudioCaptureManager.m_Instance != null &&
                    AudioCaptureManager.m_Instance.IsSimulatedBPMModeActive)
                {
                    return m_SimulatedBandLevels;
                }
                return m_BandPeakLevelsOutput;
            }
        }

        public Vector4 BeatOutput
        {
            get
            {
                if (AudioCaptureManager.m_Instance != null &&
                    AudioCaptureManager.m_Instance.IsSimulatedBPMModeActive)
                {
                    return m_SimulatedBeatOutput;
                }
                return m_BeatOutput;
            }
        }

        public Vector4 BeatOutputAccum
        {
            get
            {
                if (AudioCaptureManager.m_Instance != null &&
                    AudioCaptureManager.m_Instance.IsSimulatedBPMModeActive)
                {
                    return m_SimulatedBeatOutputAccum;
                }
                return m_BeatOutputAccum;
            }
        }

        public Vector4 AudioVolume
        {
            get
            {
                if (AudioCaptureManager.m_Instance != null &&
                    AudioCaptureManager.m_Instance.IsSimulatedBPMModeActive)
                {
                    return m_SimulatedAudioVolume;
                }
                return m_AudioVolume;
            }
        }



        public bool VisualsRequested
        {
            get { return m_VisualsRequestCount > 0; }
        }

        public bool AreVisualsActive
        {
            get { return m_VisualsActive; }
        }

        // Update 2163 VisualizerManager.Awake
        void Awake()
        {
            m_Instance = this;
            m_VisualizerObjects = new List<GameObject>();
            Shader.DisableKeyword("AUDIO_REACTIVE");

#if DISABLE_AUDIO_CAPTURE || UNITY_OSX || UNITY_EDITOR_OSX
    m_FFT = new Fft();
#else
            m_FFT = new VisualizerCSCoreFft(1, 512);
#endif

            m_FFTResult = new float[m_FFTSize];
            m_PeakFFTResult = new float[m_FFTSize];
            m_BandLevels = new float[m_Bands.Length];
            m_BandPeakLevels = new float[m_Bands.Length];
            m_BandNormalizedLevels = new float[m_Bands.Length];

            m_WaveFormTexture = new Texture2D(m_FFTSize, 1, TextureFormat.ARGB32, true, true);
            m_WaveFormTexture.SetPixels32(new Color32[m_FFTSize]);
            m_WaveFormRow = new Color[m_FFTSize];

            m_FFTTexture = new Texture2D(m_FFTTextureSize, 1, TextureFormat.ARGB32, true, true);
            m_FFTTexture.SetPixels32(new Color32[m_FFTTextureSize]);
            m_FFTRow = new Color[m_FFTTextureSize];

            m_BandPeakLevelsOutput = Vector4.zero;
            m_BeatOutput = Vector4.zero;
            m_BeatOutputAccum = Vector4.zero;
            m_AudioVolume = Vector4.zero;

            m_LChannelWeird = new float[m_FFTSize];
            m_LChannelHighPass = new float[m_FFTSize];
            m_LChannelLowPass = new float[m_FFTSize];
            m_AudioSamples = new float[m_FFTSize];

            // Safe assignment of Reaktor components
            if (m_SystemAudioInjector != null)
                m_Reaktor = m_SystemAudioInjector.GetComponent<Reaktor>();
            if (m_SystemAudioInjectorAlt != null)
                m_ReaktorAlt = m_SystemAudioInjectorAlt.GetComponent<Reaktor>();
            if (m_SystemAudioInjectorLowPass != null)
                m_ReaktorLowPass = m_SystemAudioInjectorLowPass.GetComponent<Reaktor>();
            if (m_SystemAudioInjectorHighPass != null)
                m_ReaktorHighPass = m_SystemAudioInjectorHighPass.GetComponent<Reaktor>();

            m_VisualsRequestCount = 0;
            m_VisualsActive = false;
        }


        void LateUpdate()
        {
            // Diagnostic: confirm this build's LateUpdate runs and sees OfflineRender (every ~4s).
            if (Time.frameCount % 240 == 0)
            {
              //  Debug.LogError(
               //     $"[LateUpdate-ENTRY] OfflineRender={(App.Config != null && App.Config.OfflineRender)} frame={Time.frameCount}");
            }

            // Headless offline: Offline42 drives Simulated BPM with path time.
            if (App.Config != null && App.Config.OfflineRender)
            {
                return;
            }

            // Yield to VideoRecorderUtils if an active multi-pass timeline recording is running
            if (VideoRecorderUtils.m_UsdPathSerializer != null &&
                VideoRecorderUtils.m_UsdPathSerializer.IsRecording)
            {
                return;
            }

            // Continuous drive for Simulated BPM mode (standard live preview outside of recording)
            if (AudioCaptureManager.m_Instance != null &&
                AudioCaptureManager.m_Instance.IsSimulatedBPMModeActive)
            {
                UpdateSimulatedBeats(Time.time);
            }
        }

        // Update 213X VisualizerManager.RegisterVisualizerObject
        // Purpose: Add diagnostic logging so we can observe when (and if) objects successfully register during audio-reactive / Simulated BPM activation.
        // Update 2165 VisualizerManager.RegisterVisualizerObject
        public void RegisterVisualizerObject(GameObject rObject)
        {
            if (rObject == null) return;

            if (!m_VisualizerObjects.Contains(rObject))
            {
                m_VisualizerObjects.Add(rObject);
                Debug.Log("[VisualizerManager] Registered visualizer object: " + rObject.name + " | Total now: " + m_VisualizerObjects.Count);

                RegisterVisualizerObject reg = rObject.GetComponent<RegisterVisualizerObject>();
                if (reg != null && reg.m_ShowInReactorModeOnly)
                {
                    rObject.SetActive(AudioCaptureManager.m_Instance != null && AudioCaptureManager.m_Instance.IsCapturingAudio);
                }
            }
        }

        public void UnregisterVisualizerObject(GameObject rObject)
        {
            for (int i = 0; i < m_VisualizerObjects.Count; ++i)
            {
                if (m_VisualizerObjects[i] == rObject)
                {
                    m_VisualizerObjects.RemoveAt(i);
                    return;
                }
            }
        }

        // Update 2166 VisualizerManager.SetVisualizerObjectActive
        /// Simulated BPM currently suppresses Reaktor/Gear components
        public void SetVisualizerObjectActive(bool bActivate)
        {
            if (bActivate)
            {
                foreach (GameObject g in m_VisualizerObjects)
                {
                    g.SetActive(true);
                }

                if (AudioCaptureManager.m_Instance == null ||
                    !AudioCaptureManager.m_Instance.IsSimulatedBPMModeActive)
                {
                    InitializeAllReaktorComponents();
                }
            }
            else
            {
                foreach (GameObject g in m_VisualizerObjects)
                {
                    RegisterVisualizerObject reg = g.GetComponent<RegisterVisualizerObject>();
                    if (reg != null && reg.m_ShowInReactorModeOnly)
                    {
                        g.SetActive(false);
                    }
                }
            }
        }
        // Update 2167 VisualizerManager.InitializeAllReaktorComponents
        void InitializeAllReaktorComponents()
        {
            foreach (GameObject g in m_VisualizerObjects)
            {
                if (g == null) continue;

                MaterialGear materialGear = g.GetComponent<MaterialGear>();
                if (materialGear != null && materialGear.reaktor != null)
                {
                    materialGear.reaktor.Initialize(materialGear);
                }

                TransformGear transformGear = g.GetComponent<TransformGear>();
                if (transformGear != null && transformGear.reaktor != null)
                {
                    transformGear.reaktor.Initialize(transformGear);
                }

                ParticleSystemGear particleGear = g.GetComponent<ParticleSystemGear>();
                if (particleGear != null && particleGear.reaktor != null)
                {
                    particleGear.reaktor.Initialize(particleGear);
                }

                LightGear lightGear = g.GetComponent<LightGear>();
                if (lightGear != null && lightGear.reaktor != null)
                {
                    lightGear.reaktor.Initialize(lightGear);
                }

                ConstantMotionGear motionGear = g.GetComponent<ConstantMotionGear>();
                if (motionGear != null && motionGear.reaktor != null)
                {
                    motionGear.reaktor.Initialize(motionGear);
                }
            }
        }

        // Update 2168 VisualizerManager.EnableVisuals
        public void EnableVisuals(bool bEnable)
        {
            m_VisualsRequestCount += bEnable ? 1 : -1;
            Debug.Assert(m_VisualsRequestCount >= 0);

            if (m_VisualsRequestCount > 0)
            {
                if (AudioCaptureManager.m_Instance != null)
                {
                    ActivateVisuals(AudioCaptureManager.m_Instance.IsCapturingAudio);
                }
                else
                {
                    ActivateVisuals(false);
                }
            }
            else
            {
                ActivateVisuals(false);
            }
        }
        // Update 2170 VisualizerManager.AudioCaptureStatusChange
        public void AudioCaptureStatusChange(bool bCapturing)
        {
            if (m_VisualsRequestCount > 0 && AudioCaptureManager.m_Instance != null)
            {
                ActivateVisuals(bCapturing);
            }
        }




        public void SetSampleRate(int sampleRate)
        {
            m_SampleRate = sampleRate;
#if DISABLE_AUDIO_CAPTURE || UNITY_OSX || UNITY_EDITOR_OSX
    m_LowPassFilter = new Filter();
    m_HighPassFilter = new Filter();
#else
            m_LowPassFilter = new VisualizerCSCoreFilter(VisualizerCSCoreFilter.FilterType.Low,
                m_SampleRate, m_LowPassFreq);
            m_HighPassFilter = new VisualizerCSCoreFilter(VisualizerCSCoreFilter.FilterType.High,
                m_SampleRate, m_HighPassFreq);
#endif
        }

        // Update 2172 VisualizerManager.ProcessAudio
        // Purpose: Fixed critical bug where val4 was incorrectly clamped using val3.
        // Also improved dB-to-0–1 conversion for clarity and safety. Kept original
        // comments for future readability.

        // Update 2172 VisualizerManager.ProcessAudio
        // Purpose: Fixed critical bug where val4 was incorrectly clamped using val3.
        // Also improved dB-to-0–1 conversion for clarity and safety. Kept original
        // comments for future readability.
        // INTEGRATION UPDATE: Automatically bakes real-world audio spectra into the 
        // persistent data matrix when an interactive Pass 1 camera track recording is active.


        /// <summary>
        /// VisualizerManager.ProcessAudio
        /// Processes real-time audio waveform arrays and calculates frequency spectrum bands.
        /// Fully insulated with mode-interception fences to prevent live audio data residues
        /// from contaminating offline multi-pass or Simulated BPM playback metrics.
        /// </summary>
        /// <summary>
        /// VisualizerManager.ProcessAudio
        /// Processes real-time audio waveform arrays and calculates frequency spectrum bands.
        /// Fully insulated with mode-interception fences to prevent live audio data residues
        /// from contaminating offline multi-pass or Simulated BPM playback metrics.
        /// </summary>
        /// <summary>
        /// VisualizerManager.ProcessAudio
        /// Processes real-time audio waveform arrays and calculates frequency spectrum bands.
        /// Fully insulated with mode-interception fences to prevent live audio data residues
        /// from contaminating offline multi-pass or Simulated BPM playback metrics.
        /// </summary>
        /// <summary>
        /// VisualizerManager.ProcessAudio
        /// Processes real-time audio waveform arrays and calculates frequency spectrum bands.
        /// Fully insulated with mode-interception fences to prevent live audio data residues
        /// from contaminating offline multi-pass or Simulated BPM playback metrics.
        /// </summary>



        /// <summary>
        /// VisualizerManager.ProcessAudio
        /// Processes real-time audio waveform arrays and calculates frequency spectrum bands.
        /// Fully insulated with mode-interception fences to prevent live audio data residues
        /// from contaminating offline multi-pass or Simulated BPM playback metrics.
        /// </summary>

        public void ProcessAudio(float[] AudioData, int SampleRate)
        {
            Debug.Assert(AudioData.Length == m_FFTSize);
            if (SampleRate != m_SampleRate)
            {
                SetSampleRate(SampleRate);
            }
            m_FFT.Add(AudioData, AudioData.Length);
            m_FFT.GetFftData(m_FFTResult);

            // Calculate Band Levels. Band Levels are much more useful
            // for visualization / system feedback. Raw FFT is mostly useless.
            ConvertRawSpectrumToBandLevels();

            // Fill the buffers full
            for (int i = 0; i < m_FFTSize; ++i)
            {
                float fLChannel = AudioData[i];
                m_AudioSamples[i] = fLChannel;
                m_LChannelWeird[i] = Mathf.Lerp(fLChannel, m_LChannelWeird[i], m_WeirdWaveformLerp);
                m_PeakFFTResult[i] = Mathf.Max(m_PeakFFTResult[i] * m_FFTPeakDecay, m_FFTResult[i]);
            }

            m_AudioSamples.CopyTo(m_LChannelLowPass, 0);
            m_AudioSamples.CopyTo(m_LChannelHighPass, 0);

            m_LowPassFilter.Frequency = m_LowPassFreq;
            m_HighPassFilter.Frequency = m_HighPassFreq;

            m_LowPassFilter.Process(m_LChannelLowPass);
            m_HighPassFilter.Process(m_LChannelHighPass);

            // Pipe waveform values into texture
            for (int i = 0; i < m_FFTSize; ++i)
            {
                m_WaveFormRow[i].r = m_AudioSamples[i] * 0.5f + 0.5f;
                m_WaveFormRow[i].g = m_LChannelWeird[i] * 0.5f + 0.5f;
                m_WaveFormRow[i].b = m_LChannelLowPass[i] * 0.5f + 0.5f;
                m_WaveFormRow[i].a = m_LChannelHighPass[i] * 0.5f + 0.5f;
            }

            // Pipe FFT values into texture (only use the first half)
            for (int i = 0; i < m_FFTTextureSize; ++i)
            {
                int fMirroredIndex = 128 - Mathf.Abs(i - 128);
                m_FFTRow[i].r = m_FFTResult[fMirroredIndex] * m_FFTScale;
                m_FFTRow[i].g = m_FFTResult[fMirroredIndex] * Mathf.Pow((fMirroredIndex / 512.0f), m_FFTPower) * m_FFTPowerScale;
                m_FFTRow[i].b = m_PeakFFTResult[fMirroredIndex] * Mathf.Pow((fMirroredIndex / 512.0f), m_FFTPower) * m_FFTPowerScale;
            }

            for (int i = 0; i < m_FFTTextureSize; ++i)
            {
                int band_levels_index = (int)(i * ((float)m_BandLevels.Length / (float)m_FFTTextureSize));
                m_FFTRow[i].a = m_BandNormalizedLevels[band_levels_index];
            }

            // Pipe values into the Audio Injector for beat detection
            if (m_SystemAudioInjector)
                m_SystemAudioInjector.ProcessAudio(m_AudioSamples);
            if (m_SystemAudioInjectorAlt)
                m_SystemAudioInjectorAlt.ProcessAudio(m_AudioSamples);
            if (m_SystemAudioInjectorLowPass)
                m_SystemAudioInjectorLowPass.ProcessAudio(m_AudioSamples);
            if (m_SystemAudioInjectorHighPass)
                m_SystemAudioInjectorHighPass.ProcessAudio(m_AudioSamples);

            // Update Shaders
            Shader.SetGlobalTexture("_WaveFormTex", m_WaveFormTexture);
            Shader.SetGlobalVector("_PeakBandLevels", m_BandPeakLevelsOutput);
            Shader.SetGlobalTexture("_FFTTex", m_FFTTexture);
            Shader.SetGlobalVector("_BeatOutput", m_BeatOutput);
            Shader.SetGlobalVector("_BeatOutputAccum", m_BeatOutputAccum);
            Shader.SetGlobalVector("_AudioVolume", m_AudioVolume);

            m_BandPeakLevelsOutput = new Vector4(m_BandPeakLevels[0], m_BandPeakLevels[1], m_BandPeakLevels[2], m_BandPeakLevels[3]);

            m_WaveFormTexture.SetPixels(0, 0, m_FFTSize, 1, m_WaveFormRow);
            m_WaveFormTexture.Apply();

            m_FFTTexture.SetPixels(0, 0, m_FFTTextureSize, 1, m_FFTRow);
            m_FFTTexture.Apply();

            m_BeatOutput = new Vector4(m_Reaktor.output, m_ReaktorAlt.output, m_ReaktorLowPass.output, m_ReaktorHighPass.output);
            m_BeatOutputAccum = new Vector4(m_Reaktor.outputAccumulated, m_ReaktorAlt.outputAccumulated, m_ReaktorLowPass.outputAccumulated, m_ReaktorHighPass.outputAccumulated) * .02f;

            float val1 = Mathf.Clamp((60.0f + m_Reaktor.outputDb) / 60.0f, 0.0f, 1.0f);
            float val2 = Mathf.Clamp((60.0f + m_ReaktorAlt.outputDb) / 60.0f, 0.0f, 1.0f);
            float val3 = Mathf.Clamp((60.0f + m_ReaktorLowPass.outputDb) / 60.0f, 0.0f, 1.0f);
            float val4 = Mathf.Clamp((60.0f + m_ReaktorHighPass.outputDb) / 60.0f, 0.0f, 1.0f);
            m_AudioVolume = new Vector4(val1, val2, val3, val4);


            // =================================================================
            // AUTOMATED FRAME-LOCKED CALIBRATION PASS CAPTURE PIPELINE
            // =================================================================
            // Verify if an interactive in-game Pass 1 tracking run is currently traveling the timeline path
            if (CameraPathCaptureRig.m_Instance != null && CameraPathCaptureRig.m_Instance.IsFirstPassActive)
            {
                float currentProgress = CameraPathCaptureRig.m_Instance.GetCompletionOfCameraAlongPath() ?? 0f;
                BakedAudioFrame frame = new BakedAudioFrame
                {
                    normalizedProgress = currentProgress,
                    beatOutput = m_BeatOutput,
                    beatOutputAccum = m_BeatOutputAccum,
                    bandPeakLevels = m_BandPeakLevelsOutput,
                    audioVolume = m_AudioVolume,
                    // Capture active texture arrays directly out of your running CPU buffers
                    fftTexturePixels = (Color[])m_FFTRow.Clone(),
                    waveFormTexturePixels = (Color[])m_WaveFormRow.Clone()
                };

                if (m_BakedRecordingFrames == null)
                {
                    m_BakedRecordingFrames = new System.Collections.Generic.List<BakedAudioFrame>();
                }
                m_BakedRecordingFrames.Add(frame);
            }
            // =================================================================
        }

      


        int FrequencyToSpectrumIndex(float f)
        {
            var i = Mathf.FloorToInt(f / m_SampleRate * 2.0f * m_FFTResult.Length);
            return Mathf.Clamp(i, 0, m_FFTResult.Length - 1);
        }

     


        private void ConvertRawSpectrumToBandLevels()
        {
            for (var i = 0; i < m_Bands.Length; i++)
            {
                for (var bi = 0; bi < m_BandLevels.Length; bi++)
                {
                    int imin = FrequencyToSpectrumIndex(m_Bands[bi] / m_Bandwidth);
                    int imax = FrequencyToSpectrumIndex(m_Bands[bi] * m_Bandwidth);

                    var bandMax = 0.0f;
                    for (var fi = imin; fi <= imax; fi++)
                    {
                        bandMax = Mathf.Max(bandMax, m_FFTResult[fi]);
                    }

                    m_BandLevels[bi] = bandMax;
                    m_BandPeakLevels[bi] = Mathf.Max(m_BandPeakLevels[bi] * m_BandPeakDecay, bandMax);
                    m_BandNormalizedLevels[bi] = Mathf.Lerp(m_BandLevels[bi] / m_BandPeakLevels[bi], m_BandNormalizedLevels[bi], m_NormalizedBandPeakLerp);
                }
            }
        }

        public void InjectScriptedWaveform(Color[] fft)
        {
            for (int i = 0; i < m_FFTSize; ++i)
            {
                m_WaveFormRow[i] = fft[i];
            }
            m_WaveFormTexture.SetPixels(0, 0, m_FFTSize, 1, m_WaveFormRow);
            m_WaveFormTexture.Apply();
            Shader.SetGlobalTexture("_WaveFormTex", m_WaveFormTexture);
        }

        public void InjectScriptedFft(float[] fft1, float[] fft2, float[] fft3, float[] fft4)
        {
            for (int i = 0; i < m_FFTTextureSize; ++i)
            {
                m_FFTRow[i] = new Color(fft1[i], fft2[i], fft3[i], fft4[i]);
            }
            m_FFTTexture.SetPixels(0, 0, m_FFTTextureSize, 1, m_FFTRow);
            m_FFTTexture.Apply();
            Shader.SetGlobalTexture("_FFTTex", m_FFTTexture);
        }


        /// <summary>
        ///  Dual Functionality with FFT Simulation
        /// </summary>
        /// bEnable or disable audio reactive visuals.
        /// 

        // Update 2132 VisualizerManager.ActivateVisuals
        // Purpose: Add a one-time diagnostic log when Simulated BPM mode enables visuals.
        // Reports the total number of registered visualizer objects and a breakdown of
        // reactive Gear components (MaterialGear, TransformGear, ParticleSystemGear, etc.).
        // Uses AudioCaptureManager.IsSimulatedBPMModeActive as the authoritative check
        // to ensure the diagnostic fires reliably regardless of call order.
        // Update 2161 VisualizerManager.ActivateVisuals
        // Purpose: Remove redundant fallback check on m_UseSimulatedBPM.
        // The diagnostic and Reaktor suppression now rely solely on
        // AudioCaptureManager.IsSimulatedBPMModeActive for consistency.
        // Update 2177 VisualizerManager.ActivateVisuals
        // Purpose: One-time diagnostic when Simulated BPM mode enables visuals.
        // The detailed object/Gear count log now fires only once per mode enable
        // to eliminate repeated spam. All other behavior is unchanged.
        void ActivateVisuals(bool bEnable)
        {
            if (bEnable)
            {
                Shader.EnableKeyword("AUDIO_REACTIVE");
            }
            else
            {
                Shader.DisableKeyword("AUDIO_REACTIVE");
            }
            SetVisualizerObjectActive(bEnable);
            bool isSimulated = AudioCaptureManager.m_Instance != null &&
                               AudioCaptureManager.m_Instance.IsSimulatedBPMModeActive;
            if (!isSimulated && m_Reaktor != null)
            {
                m_Reaktor.enabled = bEnable;
            }
            if (isSimulated && bEnable && !m_HasLoggedSimulatedBPMDiagnostic)
            {
                m_HasLoggedSimulatedBPMDiagnostic = true;
                int totalObjects = m_VisualizerObjects.Count;
                int objectsWithAnyGear = 0;
                int materialGearCount = 0;
                int transformGearCount = 0;
                int particleGearCount = 0;
                int lightGearCount = 0;
                int otherGearCount = 0;
                foreach (GameObject g in m_VisualizerObjects)
                {
                    bool hasGear = false;
                    if (g.GetComponent<MaterialGear>()) { materialGearCount++; hasGear = true; }
                    if (g.GetComponent<TransformGear>()) { transformGearCount++; hasGear = true; }
                    if (g.GetComponent<ParticleSystemGear>()) { particleGearCount++; hasGear = true; }
                    if (g.GetComponent<LightGear>()) { lightGearCount++; hasGear = true; }
                    if (g.GetComponent<ConstantMotionGear>()) { otherGearCount++; hasGear = true; }
                    if (hasGear) objectsWithAnyGear++;
                }
                Debug.LogError($"[VisualizerManager] Simulated BPM visuals ENABLED.\n" +
                    $"Total visualizer objects: {totalObjects}\n" +
                    $"Objects with at least one Gear: {objectsWithAnyGear}\n" +
                    $" MaterialGear: {materialGearCount} | TransformGear: {transformGearCount}\n" +
                    $" ParticleSystemGear: {particleGearCount} | LightGear: {lightGearCount}\n" +
                    $" Other Gears: {otherGearCount}\n" +
                    $"Note: Reaktor is disabled in Simulated BPM mode. Shader globals (_BeatOutput, _FFTTex, _PeakBandLevels) are being injected directly.");
            }
            m_VisualsActive = bEnable;
        }



        /// =====================================================================
        /// SIMULATED BPM SECTION
        /// All Simulated BPM related methods are grouped here for readability
        /// and easier future maintenance / upgrades.
        /// =====================================================================

        // Existing methods moved here for grouping
        // New methods for Simple fake FFT + Band simulation

        // Update 2123 VisualizerManager.GenerateSimulatedBeatOutput
        // Introduced 2123
        // Update 2146 VisualizerManager.GenerateSimulatedBeatOutput
        // Purpose: Generate beat, amplitude, accumulated beat, and band level data
        // for Simulated BPM. Populates all variables used by InjectSimulatedDataToShaders().
        // Update 2174 VisualizerManager.GenerateSimulatedBeatOutput

        // Update 2200  
        // Update 2201 VisualizerManager.GenerateSimulatedBeatOutput
        // Purpose: Generate beat, amplitude, accumulated beat, and band level data.
        // Added m_BeatPulseWidth to give independent control over how long each beat pulse lasts.
        // This helps reduce long zero periods while keeping good percussive response.
        private void GenerateSimulatedBeatOutput(float phase)
        {
            // Beat pulse now uses both sharpness (attack) and pulse width (duration)
            float beatPulse = Mathf.Pow(Mathf.Max(0f, 1f - phase * m_BeatSharpness * m_BeatPulseWidth), 2.2f);
            float amplitudeMod = (Mathf.Sin(phase * Mathf.PI * 2f) * m_AmplitudeModAmount + m_AmplitudeModAmount);
            float strength = m_ReactivityStrength;

            m_SimulatedBeatOutput = new Vector4(
                beatPulse * strength,
                amplitudeMod * strength,
                0f,
                0f
            );

            // Improved Accumulated Beat (builds gradually)
            float targetAccum = beatPulse * 0.8f + 0.2f;
            float currentAccum = m_SimulatedBeatOutputAccum.x;
            float newAccum = Mathf.Lerp(currentAccum, targetAccum, m_AccumLerpSpeed);
            m_SimulatedBeatOutputAccum = new Vector4(newAccum, 0f, 0f, 0f);

            // Fake band levels using exposed multipliers
            float bandBase = beatPulse * strength * 0.9f;
            m_SimulatedBandLevels = new Vector4(
                bandBase * m_BandLowMultiplier,
                bandBase * m_BandLowMidMultiplier,
                bandBase * m_BandMidMultiplier,
                bandBase * m_BandHighMidMultiplier
            );

            // Audio Volume with dynamics
            float baseVolume = 0.35f;
            float beatBoost = beatPulse * 0.65f;
            float amplitudeWobble = amplitudeMod * 0.2f;

            float simulatedVolume = baseVolume + beatBoost + amplitudeWobble;
            simulatedVolume = Mathf.Clamp01(simulatedVolume * strength * m_VolumeMultiplier);

            m_SimulatedAudioVolume = new Vector4(
                simulatedVolume,
                simulatedVolume,
                simulatedVolume * 0.92f,
                simulatedVolume * 0.88f
            );

            // Throttled diagnostics
            if (Time.frameCount % 60 == 0)
            {
                Debug.LogError($"[SimBPM Accum/Volume] " +
                    $"Accum: {newAccum:F4} | Volume: {simulatedVolume:F4} | BeatPulse: {beatPulse:F4}");
            }
        }


        public void InjectBandPeaks(Vector4 data)
        {
            Shader.SetGlobalVector("_PeakBandLevels", data);
        }

        public void InjectScriptedBeatAccumulator(Vector4 data)
        {
            Shader.SetGlobalVector("_BeatOutputAccum", data);
        }

        public void InjectScriptedBeats(Vector4 data)
        {
            Shader.SetGlobalVector("_BeatOutput", data);
        }



        /// <summary>
        /// Returns whether Simulated BPM mode is currently active.
        /// </summary>
        public bool IsUsingSimulatedBPM
        {
            get { return m_UseSimulatedBPM; }
        }



        // Update 2157 VisualizerManager.SetReactivityStrength
        public void SetReactivityStrength(float strength)
        {
            m_ReactivityStrength = Mathf.Clamp(strength, 0f, m_MaxReactivityStrength);
        }

        /// <summary>
        /// Enables Simulated BPM mode and sets the target BPM used for rhythmic reactivity.
        /// </summary>
        // Update 2182 VisualizerManager.SetSimulatedBPM
        // Purpose: When disabling Simulated BPM mode (enable = false), automatically
        // reset the one-time diagnostic flag (m_HasLoggedSimulatedBPMDiagnostic) so
        // the detailed "visualizer objects" log can fire again the next time the mode
        // is enabled. This keeps all Simulated BPM state management inside this class.
        public void SetSimulatedBPM(float bpm, bool enable = true)
        {
            m_SimulatedBPM = Mathf.Max(1f, bpm);
            m_UseSimulatedBPM = enable;

            if (enable && m_Instance != null)
            {
                m_Instance.InitializeSimulatedTextures();
            }
            else
            {
                // Mode is being turned off — reset the diagnostic flag
                m_HasLoggedSimulatedBPMDiagnostic = false;
            }
        }

        /// <summary>
        /// Updates the simulated beat and amplitude values based on current recording time and BPM.
        /// This should be called every frame during recording when Simulated BPM mode is active.
        /// </summary>
        // Update 2173 VisualizerManager.UpdateSimulatedBeats
        public void UpdateSimulatedBeats(float currentTime)
        {
            bool isSimulated = AudioCaptureManager.m_Instance != null &&
                               AudioCaptureManager.m_Instance.IsSimulatedBPMModeActive;

            if (!isSimulated || m_SimulatedBPM <= 0f)
                return;

            if (m_Instance == null)
            {
                Debug.LogError("[VisualizerManager.UpdateSimulatedBeats] Called but VisualizerManager instance is null.");
                return;
            }

            float beatsPerSecond = m_SimulatedBPM / 60f;
            float phase = Mathf.Repeat(currentTime * beatsPerSecond, 1f);

            if (!m_SimulatedTexturesInitialized)
            {
                InitializeSimulatedTextures();
            }



            GenerateSimulatedBeatOutput(phase);
            GenerateSimulatedFFTTex(phase);
            GenerateSimulatedWaveFormTex(phase);
            InjectSimulatedDataToShaders();

            // Reduced logging - only log once per second to avoid spam during long recordings
            if (Time.frameCount % 60 == 0)
            {
                Debug.LogError($"[SimBPM] Time: {currentTime:F2} | BPM: {m_SimulatedBPM} | Beat: {m_SimulatedBeatOutput.x:F3}");
            }
        }

        // =====================================================================
        // SIMULATED BPM SECTION (TEXTURES
        // All Simulated BPM related methods are grouped here for readability
        // and easier future maintenance / upgrades.
        // =====================================================================



        // Update 2140 VisualizerManager.InitializeSimulatedTextures
        // Introduced 2140
        // Update 2148 VisualizerManager.InitializeSimulatedTextures
        // Purpose: Create RenderTextures + reusable Texture2D buffers once.
        // Called safely from UpdateSimulatedBeats() or when enabling Simulated BPM.
        private void InitializeSimulatedTextures()
        {
            if (m_SimulatedTexturesInitialized) return;

            // RenderTextures (these stay alive while mode is active)
            if (m_SimulatedFFTTex == null)
            {
                m_SimulatedFFTTex = new RenderTexture(FFT_TEXTURE_WIDTH, FFT_TEXTURE_HEIGHT, 0, RenderTextureFormat.ARGB32);
                m_SimulatedFFTTex.name = "SimulatedFFTTex";
                m_SimulatedFFTTex.filterMode = FilterMode.Bilinear;
                m_SimulatedFFTTex.wrapMode = TextureWrapMode.Clamp;
            }

            if (m_SimulatedWaveFormTex == null)
            {
                m_SimulatedWaveFormTex = new RenderTexture(WAVEFORM_TEXTURE_WIDTH, WAVEFORM_TEXTURE_HEIGHT, 0, RenderTextureFormat.ARGB32);
                m_SimulatedWaveFormTex.name = "SimulatedWaveFormTex";
                m_SimulatedWaveFormTex.filterMode = FilterMode.Bilinear;
                m_SimulatedWaveFormTex.wrapMode = TextureWrapMode.Clamp;
            }

            // Reusable CPU-side buffers (prevents garbage every frame)
            if (m_SimulatedFFTTempTex == null)
                m_SimulatedFFTTempTex = new Texture2D(FFT_TEXTURE_WIDTH, FFT_TEXTURE_HEIGHT, TextureFormat.RGBA32, false);

            if (m_SimulatedWaveFormTempTex == null)
                m_SimulatedWaveFormTempTex = new Texture2D(WAVEFORM_TEXTURE_WIDTH, WAVEFORM_TEXTURE_HEIGHT, TextureFormat.RGBA32, false);

            m_SimulatedTexturesInitialized = true;
            Debug.LogError("[VisualizerManager] Simulated BPM textures initialized (once).");
        }

        // Update 2141 VisualizerManager.GenerateSimulatedFFTTex
        // Introduced 2141
        // Purpose: Fill the simulated _FFTTex with plausible frequency data that reacts
        // to the current beat phase. This is the texture many shaders (Rainbow, etc.)
        // actually read via tex2D(_FFTTex, ...).

        // Update 2155 VisualizerManager.GenerateSimulatedFFTTex
        // Update 2184 VisualizerManager.GenerateSimulatedFFTTex
        // Purpose: Generate fake FFT texture with tunable per-channel strength.
        // Blue channel (b) now uses a sharper beat-style pulse because WaveformFFT
        // reads almost exclusively from _FFTTex.b. Added diagnostics to track values.
        // Update 2185 VisualizerManager.GenerateSimulatedFFTTex
        // Purpose: Generate fake FFT texture using exposed Inspector multipliers.
        // Blue channel uses a sharper beat-style pulse because WaveformFFT reads
        // almost exclusively from _FFTTex.b. Includes throttled diagnostics.
        private void GenerateSimulatedFFTTex(float phase)
        {
            if (m_SimulatedFFTTempTex == null) return;

            Color[] pixels = new Color[FFT_TEXTURE_WIDTH];
            float fftStrength = m_ReactivityStrength * m_FftReactivityMultiplier;

            for (int i = 0; i < FFT_TEXTURE_WIDTH; i++)
            {
                float x = (float)i / FFT_TEXTURE_WIDTH;

                // Bass (low frequencies)
                float bass = Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Abs(x - 0.1f) * 8f), 2f)
                             * (0.6f + 0.4f * Mathf.Sin(phase * 2f));

                // Mid frequencies
                float mid = Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Abs(x - 0.4f) * 6f), 1.5f)
                            * (0.5f + 0.5f * Mathf.Sin(phase * 3f + 1f));

                // High frequencies (blue channel) - uses sharper beat-style pulse
                // because WaveformFFT relies heavily on _FFTTex.b
                float highPhase = Mathf.Repeat(phase * m_FftBlueChannelFrequency, 1f);
                float high = Mathf.Pow(Mathf.Max(0f, 1f - highPhase * m_FftBlueChannelSpikeSharpness), 2.2f)
                             * m_FftChannelB_Strength;

                // Apply per-channel strength multipliers
                float r = bass * fftStrength * m_FftChannelR_Strength;
                float g = mid * fftStrength * m_FftChannelG_Strength;
                float b = high * fftStrength; // Blue channel output (critical for WaveformFFT)
                float a = (bass + mid + high) * 0.5f * fftStrength * m_FftChannelA_Strength;

                pixels[i] = new Color(r, g, b, a);
            }

            m_SimulatedFFTTempTex.SetPixels(pixels);
            m_SimulatedFFTTempTex.Apply();
            Graphics.Blit(m_SimulatedFFTTempTex, m_SimulatedFFTTex);
            Shader.SetGlobalTexture("_FFTTex", m_SimulatedFFTTex);

            // === DIAGNOSTICS (throttled to every 30 frames) ===
            if (Time.frameCount % 30 == 0)
            {
                float maxB = 0f;
                for (int i = 0; i < pixels.Length; i++)
                    maxB = Mathf.Max(maxB, pixels[i].b);

                Debug.LogError($"[SimBPM FFT] Max Blue: {maxB:F4} | " +
                               $"R:{m_FftChannelR_Strength:F2} G:{m_FftChannelG_Strength:F2} " +
                               $"B:{m_FftChannelB_Strength:F2} A:{m_FftChannelA_Strength:F2}");
            }
        }



        // Update 2156 VisualizerManager.GenerateSimulatedWaveFormTex
        // Update 2186 VisualizerManager.GenerateSimulatedWaveFormTex
        // Purpose: Generate fake waveform texture using exposed per-channel strength controls.
        // Follows the same structure and diagnostic style as GenerateSimulatedFFTTex for consistency.
        // Includes throttled diagnostics so we can monitor the output during tuning.
        private void GenerateSimulatedWaveFormTex(float phase)
        {
            if (m_SimulatedWaveFormTempTex == null) return;

            Color[] pixels = new Color[WAVEFORM_TEXTURE_WIDTH];
            float waveStrength = m_ReactivityStrength * m_FftReactivityMultiplier;

            for (int i = 0; i < WAVEFORM_TEXTURE_WIDTH; i++)
            {
                float x = (float)i / WAVEFORM_TEXTURE_WIDTH;

                // Create a basic waveform shape modulated by phase (gentle rolling wave)
                float wave = Mathf.Sin(phase * 6f + x * 12f) * 0.5f + 0.5f;
                wave *= (0.7f + 0.3f * Mathf.Sin(phase * 2f));

                // Apply individual channel strength multipliers from Inspector
                float r = wave * waveStrength * m_WaveformChannelR_Strength;
                float g = wave * 0.85f * waveStrength * m_WaveformChannelG_Strength;
                float b = wave * 0.6f * waveStrength * m_WaveformChannelB_Strength;
                float a = wave * 1.2f * waveStrength * m_WaveformChannelA_Strength;

                pixels[i] = new Color(r, g, b, a);
            }

            m_SimulatedWaveFormTempTex.SetPixels(pixels);
            m_SimulatedWaveFormTempTex.Apply();
            Graphics.Blit(m_SimulatedWaveFormTempTex, m_SimulatedWaveFormTex);
            Shader.SetGlobalTexture("_WaveFormTex", m_SimulatedWaveFormTex);

            // === DIAGNOSTICS (throttled to every 30 frames) ===
            if (Time.frameCount % 30 == 0)
            {
                float maxR = 0f;
                for (int i = 0; i < pixels.Length; i++)
                    maxR = Mathf.Max(maxR, pixels[i].r);

                Debug.LogError($"[SimBPM Waveform] Max Red: {maxR:F4} | " +
                               $"R:{m_WaveformChannelR_Strength:F2} G:{m_WaveformChannelG_Strength:F2} " +
                               $"B:{m_WaveformChannelB_Strength:F2} A:{m_WaveformChannelA_Strength:F2}");
            }
        }


        // Update 2145 VisualizerManager.InjectSimulatedDataToShaders
        // Purpose: Inject all simulated audio data (Vector4s + textures) into shader globals.
        // Uses existing injection methods for the Vector4 data to avoid introducing
        // new undefined fields. Only the texture injection is new.
        // Update 2175 VisualizerManager.InjectSimulatedDataToShaders
        // Update 2183 VisualizerManager.InjectSimulatedDataToShaders
        // Purpose: Inject all simulated data + add detailed per-frame diagnostics so we can see
        // exactly what values are being written to the shader globals and textures.
        private void InjectSimulatedDataToShaders()
        {
            // === Vector4 data ===
            InjectScriptedBeats(m_SimulatedBeatOutput);
            InjectScriptedBeatAccumulator(m_SimulatedBeatOutputAccum);
            InjectBandPeaks(m_SimulatedBandLevels);

            // === Textures ===
            if (m_SimulatedFFTTex != null)
                Shader.SetGlobalTexture("_FFTTex", m_SimulatedFFTTex);

            if (m_SimulatedWaveFormTex != null)
                Shader.SetGlobalTexture("_WaveFormTex", m_SimulatedWaveFormTex);

            // === DIAGNOSTICS - Log key values once per second to avoid spam ===
            if (Time.frameCount % 60 == 0)
            {
                Debug.LogError($"[SimBPM Diagnostics] " +
                    $"Beat: {m_SimulatedBeatOutput.x:F4} | Amp: {m_SimulatedBeatOutput.y:F4} | " +
                    $"Accum: {m_SimulatedBeatOutputAccum.x:F4} | " +
                    $"Band0: {m_SimulatedBandLevels.x:F4} | " +
                    $"FFT Tex Null: {m_SimulatedFFTTex == null} | " +
                    $"Wave Tex Null: {m_SimulatedWaveFormTex == null}");
            }
        }

        // Update 2151 VisualizerManager.CleanupSimulatedTextures
        // Purpose: Release simulated RenderTextures and buffers when Simulated BPM mode is disabled.
        // Prevents memory leaks.
        // Update 2162 VisualizerManager.CleanupSimulatedTextures
        // Purpose: Properly release both RenderTextures and temporary Texture2D buffers
        // when Simulated BPM mode is disabled. Prevents memory leaks.

        // Update 2151 VisualizerManager.CleanupSimulatedTextures
        // Purpose: Release simulated RenderTextures and buffers when Simulated BPM mode is disabled.
        // Prevents memory leaks.
        // Update 2162 VisualizerManager.CleanupSimulatedTextures
        // Purpose: Properly release both RenderTextures and temporary Texture2D buffers
        // when Simulated BPM mode is disabled. Prevents memory leaks.
        /// <summary>
        /// VisualizerManager.CleanupSimulatedTextures
        /// Releases simulated RenderTextures and temporary buffers when playback terminates.
        /// Safely clears active hardware shader profiles to prevent VRAM memory leaks.
        /// </summary>
        /// <summary>
        /// VisualizerManager.CleanupSimulatedTextures
        /// Releases simulated RenderTextures and temporary buffers when playback terminates.
        /// Safely clears active hardware shader profiles to prevent VRAM memory leaks.
        /// </summary>
        public void CleanupSimulatedTextures()
        {
            // 1. Forcefully flush and unbind all hardware material channels before destroying memory links
            ClearShadersToQuietState();

            if (m_SimulatedFFTTex != null)
            {
                m_SimulatedFFTTex.Release();
                DestroyImmediate(m_SimulatedFFTTex);
                m_SimulatedFFTTex = null;
            }

            if (m_SimulatedWaveFormTex != null)
            {
                m_SimulatedWaveFormTex.Release();
                DestroyImmediate(m_SimulatedWaveFormTex);
                m_SimulatedWaveFormTex = null;
            }

            if (m_SimulatedFFTTempTex != null)
            {
                DestroyImmediate(m_SimulatedFFTTempTex);
                m_SimulatedFFTTempTex = null;
            }

            if (m_SimulatedWaveFormTempTex != null)
            {
                DestroyImmediate(m_SimulatedWaveFormTempTex);
                m_SimulatedWaveFormTempTex = null;
            }

            m_SimulatedTexturesInitialized = false;

            // =================================================================
            // TWO-PASS OFFLINE CACHE DEMOLITION
            // =================================================================
            if (m_BakedRecordingFrames != null)
            {
                m_BakedRecordingFrames.Clear();
                m_BakedRecordingFrames.TrimExcess();
            }
            m_IsPlaybackModeActive = false;

            Debug.LogError("[VisualizerManager] Simulated BPM textures and offline recording cache cleaned up.");
        }




        /// <summary>
        /// Governs whether the visualizer acts as a data generator or a data consumer.
        /// </summary>
        public void SetPlaybackMode(bool active)
        {
            // Only trigger list wiping if we are explicitly transitioning from true to false
            if (m_IsPlaybackModeActive && !active)
            {
                if (m_BakedRecordingFrames != null)
                {
                    m_BakedRecordingFrames.Clear();
                    m_BakedRecordingFrames.TrimExcess();
                }
            }

            // === LIFECYCLE RE-ENGAGEMENT GATE ===
            // If the orchestrator initializes a fresh playback pass (true), 
            // forcefully unlock the cleanup gate flag so the binary texture 
            // buffers can be safely allocated in memory on the upcoming frames.
            if (active)
            {
                m_IsTwoPassCleanupDone = false;
            }

            m_IsPlaybackModeActive = active;
        }



        /// <summary>
        /// Core routing entry point driven directly by VideoRecorderUtils during recording passes.
        /// Explicitly accepts 3 arguments to sync with the backward compatible driver.
        /// </summary>
        public void UpdateOfflineSimulationStep(float progress, float calculatedTimelineTime, float progressLimit)
        {
            if (m_IsPlaybackModeActive)
            {
                ApplyBakedFrame(progress, progressLimit);
            }
            else
            {
                UpdateSimulatedBeats(calculatedTimelineTime);
                BakeCurrentFrame(progress);
            }
        }

        private void BakeCurrentFrame(float progress)
        {
            BakedAudioFrame frame = new BakedAudioFrame
            {
                normalizedProgress = progress,
                beatOutput = m_SimulatedBeatOutput,
                beatOutputAccum = m_SimulatedBeatOutputAccum,
                bandPeakLevels = m_SimulatedBandLevels,
                audioVolume = m_SimulatedAudioVolume,
                fftTexturePixels = null,
                waveFormTexturePixels = null
            };

            // Capture FFT Texture snapshot if initialized
            if (m_SimulatedFFTTex != null)
            {
                RenderTexture activeBuffer = RenderTexture.active;
                RenderTexture.active = m_SimulatedFFTTex;

                Texture2D tempTex = new Texture2D(m_SimulatedFFTTex.width, m_SimulatedFFTTex.height, TextureFormat.RGBA32, false);
                tempTex.ReadPixels(new Rect(0, 0, m_SimulatedFFTTex.width, m_SimulatedFFTTex.height), 0, 0);
                tempTex.Apply();

                frame.fftTexturePixels = tempTex.GetPixels();

                RenderTexture.active = activeBuffer;
                DestroyImmediate(tempTex);
            }

            // Capture WaveForm Texture snapshot if initialized
            if (m_SimulatedWaveFormTex != null)
            {
                RenderTexture activeBuffer = RenderTexture.active;
                RenderTexture.active = m_SimulatedWaveFormTex;

                Texture2D tempTex = new Texture2D(m_SimulatedWaveFormTex.width, m_SimulatedWaveFormTex.height, TextureFormat.RGBA32, false);
                tempTex.ReadPixels(new Rect(0, 0, m_SimulatedWaveFormTex.width, m_SimulatedWaveFormTex.height), 0, 0);
                tempTex.Apply();

                frame.waveFormTexturePixels = tempTex.GetPixels();

                RenderTexture.active = activeBuffer;
                DestroyImmediate(tempTex);
            }

            m_BakedRecordingFrames.Add(frame);
        }



        /// <summary>
        /// VisualizerManager.InjectTwoPassBakedFrame
        /// An isolated, zero-allocation injection pipeline for two-pass rendering.
        /// Maps audio metrics using strict list index counts, forcing textures to a silent 
        /// baseline during timeline overshoots to allow natural material dampening.
        /// </summary>
        public void InjectTwoPassBakedFrame(float progress)
        {
            // Reset tracking gates when starting a fresh rendering pass sequence
            if (progress <= 0.01f)
            {
                m_IsTwoPassCleanupDone = false;
            }

            // Hard safety intercept to protect the system from post-cleanup loop ticks
            if (m_IsTwoPassCleanupDone)
            {
                return;
            }

            // === CRITICAL DIAGNOSTIC ERROR FEEDBACK GATES ===
            if (m_BakedRecordingFrames == null)
            {
                if (UnityEngine.Time.frameCount % 60 == 0)
                {
                    UnityEngine.Debug.LogError("[Audio Pipeline] FAILURE: Cannot inject frame. The master recording list database is NULL. Your file loading stage failed.");
                }
                return;
            }

            if (m_BakedRecordingFrames.Count == 0)
            {
                if (UnityEngine.Time.frameCount % 60 == 0)
                {
                    UnityEngine.Debug.LogError("[Audio Pipeline] FAILURE: Cannot inject frame. The master recording list contains 0 restored frames. Your data sheet asset is empty.");
                }
                return;
            }
            // ===============================================

            if (m_BakedRecordingFrames == null || m_BakedRecordingFrames.Count == 0) return;

            // === CRITICAL SYSTEM FORCE ACTIVATION ===
            // Forcefully enable the master hardware keyword to ensure the graphics card
            // compiles and runs the audio-reactive loops inside your brush materials.
            Shader.EnableKeyword("AUDIO_REACTIVE");

            // 1. STRICT INDEX LOOK-UP: Locks frame sequences line-for-line to stop timing jitter
            // This local clamp guarantees the file array can never throw an out-of-bounds error.
            float secureProgress = Mathf.Clamp01(progress);
            int targetIndex = Mathf.Clamp((int)(secureProgress * (m_BakedRecordingFrames.Count - 1)), 0, m_BakedRecordingFrames.Count - 1);
            BakedAudioFrame targetFrame = m_BakedRecordingFrames[targetIndex];

            // 2. Persistent Render Texture Allocations
            if (m_SimulatedFFTTex == null)
            {
                m_SimulatedFFTTex = new RenderTexture(256, 1, 0, RenderTextureFormat.ARGB32);
                m_SimulatedFFTTex.filterMode = FilterMode.Bilinear;
                m_SimulatedFFTTex.wrapMode = TextureWrapMode.Clamp; // Fixed to target FFT
                m_SimulatedFFTTex.Create();
                UnityEngine.Debug.LogWarning("[Audio Pipeline] Headless System Note: Allocated fresh hardware m_SimulatedFFTTex canvas instance.");
            }
            if (m_SimulatedWaveFormTex == null)
            {
                m_SimulatedWaveFormTex = new RenderTexture(256, 1, 0, RenderTextureFormat.ARGB32);
                m_SimulatedWaveFormTex.filterMode = FilterMode.Bilinear;
                m_SimulatedWaveFormTex.wrapMode = TextureWrapMode.Clamp;
                m_SimulatedWaveFormTex.Create();
                UnityEngine.Debug.LogWarning("[Audio Pipeline] Headless System Note: Allocated fresh hardware m_SimulatedWaveFormTex canvas instance.");
            }

            // 3. ZERO-ALLOCATION SCRATCH BUFFER INGESTION
            // Reuses existing CPU memory allocations to eliminate unmanaged allocation leaks mid-render.
            // Bypassed automatically during overshoots (progress >= 1.0f) via the downstream texture flushes.
            if (progress < 1.0f)
            {
                if (targetFrame.fftTexturePixels != null && targetFrame.fftTexturePixels.Length > 0)
                {
                    if (m_TwoPassFftScratch == null) m_TwoPassFftScratch = new Texture2D(256, 1, TextureFormat.RGBA32, false);
                    m_TwoPassFftScratch.SetPixels(targetFrame.fftTexturePixels);
                    m_TwoPassFftScratch.Apply();
                    Graphics.Blit(m_TwoPassFftScratch, m_SimulatedFFTTex);
                }
                if (targetFrame.waveFormTexturePixels != null && targetFrame.waveFormTexturePixels.Length > 0)
                {
                    if (m_TwoPassWaveScratch == null) m_TwoPassWaveScratch = new Texture2D(256, 1, TextureFormat.RGBA32, false);
                    m_TwoPassWaveScratch.SetPixels(targetFrame.waveFormTexturePixels);
                    m_TwoPassWaveScratch.Apply();
                    Graphics.Blit(m_TwoPassWaveScratch, m_SimulatedWaveFormTex);
                }
            }

            // 4. UN-CLAMPED TIMELINE EXTENSION ENGINE
            // If the camera rig enters trailing padding zones (progress >= 1.0f), we zero out
            // the active volume pulses so the sketch transitions to a silent state naturally.
            // However, we extrapolate a continuous upward climb for the accumulator vector,
            // ensuring that scrolling brush textures and noise warps keep moving flawlessly.
            Vector4 finalAccumulator = targetFrame.beatOutputAccum;

            if (progress >= 1.0f)
            {
                float overshootTime = progress - 1.0f;
                // Maintain a steady rolling wave progression past the path limits
                finalAccumulator += new Vector4(overshootTime, overshootTime, overshootTime, overshootTime) * 5.0f;

                Shader.SetGlobalVector("_BeatOutput", Vector4.zero);
                Shader.SetGlobalVector("_BeatOutputAccum", finalAccumulator);
                Shader.SetGlobalVector("_AudioVolume", Vector4.zero);
                Shader.SetGlobalVector("_PeakBandLevels", Vector4.zero);

                // Extrapolate a steady rolling climb for the layout accumulator to prevent gradient freezing
                Shader.SetGlobalFloat("_AudioVolumeAccum", finalAccumulator.x);

                // --- INTEGRATED HARDWARE PIXEL BLACKOUT CORE ---
                // Flush the hardware render textures to clear black/transparent during overshoots
                // to completely eliminate trailing pixel frozen artifacts on advanced mesh shaders.
                RenderTexture activeBuffer = RenderTexture.active;

                RenderTexture.active = m_SimulatedFFTTex;
                GL.Clear(true, true, Color.clear);

                RenderTexture.active = m_SimulatedWaveFormTex;
                GL.Clear(true, true, Color.clear);

                RenderTexture.active = activeBuffer;
            }
            else
            {
                // Standard In-Bounds Traversal Mode
                Shader.SetGlobalVector("_BeatOutput", targetFrame.beatOutput);
                Shader.SetGlobalVector("_BeatOutputAccum", finalAccumulator);
                Shader.SetGlobalVector("_AudioVolume", targetFrame.audioVolume);
                Shader.SetGlobalVector("_PeakBandLevels", targetFrame.bandPeakLevels);

                // Inject the true, pre-recorded volume index tracker needed by Hypercolor vertex loops
                Shader.SetGlobalFloat("_AudioVolumeAccum", targetFrame.audioVolume.x);
            }

            // =================================================================
            // 5. UNIFIED GLOBAL HARDWARE SHADER OVERWRITE CORE
            // =================================================================
            // Overwrite EVERY possible permutation of global shader texture properties 
            // used across the Open Brush audio-reactive material library to unify basic 
            // scalar brushes and advanced texture-mapped brushes simultaneously.
            Shader.SetGlobalTexture("_FFTTex", m_SimulatedFFTTex);
            Shader.SetGlobalTexture("_WaveFormTex", m_SimulatedWaveFormTex);
            Shader.SetGlobalTexture("_AudioCaptureTextureFFT", m_SimulatedFFTTex);
            Shader.SetGlobalTexture("_AudioCaptureTextureWaveform", m_SimulatedWaveFormTex);

            // 6. DETAILED TEXEL MAPPINGS FOR ADVANCED SHADER MESH DISPLACEMENTS
            // 256 pixel grid row length translates precisely to a width multiplier of 1/256 = 0.00390625f
            Vector4 texelSize = new Vector4(0.00390625f, 1f, 256f, 1f);
            Shader.SetGlobalVector("_FFTTex_TexelSize", texelSize);
            Shader.SetGlobalVector("_WaveFormTex_TexelSize", texelSize);
            Shader.SetGlobalVector("_AudioCaptureTextureFFT_TexelSize", texelSize);
            Shader.SetGlobalVector("_AudioCaptureTextureWaveform_TexelSize", texelSize);
        }



        /// <summary>
        /// VisualizerManager.CleanUpTwoPassScratchAssets
        /// Standalone cleanup node triggered at the absolute end of the camera rig sequence.
        /// Forcefully releases all CPU scratch textures, clears hardware VRAM RenderTextures,
        /// and unbinds global shader keywords to prevent project cross-talk leaks.
        /// </summary>
        public void CleanUpTwoPassScratchAssets()
        {
            UnityEngine.Debug.LogWarning("[Audio Pipeline] Standalone sequence termination triggered. Commencing cleanup cascade...");

            // 1. Force the numerical variables and texture contents to black out completely
            ClearShadersToQuietState();

            // 2. Destroy the CPU-side unmanaged memory scratch textures safely
            if (m_TwoPassFftScratch != null) { DestroyImmediate(m_TwoPassFftScratch); m_TwoPassFftScratch = null; }
            if (m_TwoPassWaveScratch != null) { DestroyImmediate(m_TwoPassWaveScratch); m_TwoPassWaveScratch = null; }

            // 3. RELEASE UNMANAGED VRAM HARDWARE CONTAINER LEAKS
            if (m_SimulatedFFTTex != null)
            {
                if (m_SimulatedFFTTex.IsCreated()) m_SimulatedFFTTex.Release();
                DestroyImmediate(m_SimulatedFFTTex);
                m_SimulatedFFTTex = null;
            }
            if (m_SimulatedWaveFormTex != null)
            {
                if (m_SimulatedWaveFormTex.IsCreated()) m_SimulatedWaveFormTex.Release();
                DestroyImmediate(m_SimulatedWaveFormTex);
                m_SimulatedWaveFormTex = null;
            }

            // =================================================================
            // 4. ABSOLUTE SHADER PROPERTY DECOUPLING (COMPREHENSIVE)
            // =================================================================
            // Forcefully unbind every permutation of the graphics card samplers
            // by overwriting them with pure null targets, leaving a clean slate
            // for subsequent sketch assets.
            Shader.SetGlobalTexture("_FFTTex", null);
            Shader.SetGlobalTexture("_WaveFormTex", null);
            Shader.SetGlobalTexture("_AudioCaptureTextureFFT", null);
            Shader.SetGlobalTexture("_AudioCaptureTextureWaveform", null);

            // === EXTRA SHADER PROPERTY DECOUPLING FOR HYPERCOLOR COMPATIBILITY ===
            // Reset the dynamic vertex displacement texel vectors and gradient counters back to pure zero targets
            Shader.SetGlobalFloat("_AudioVolumeAccum", 0f);
            Shader.SetGlobalVector("_FFTTex_TexelSize", Vector4.zero);
            Shader.SetGlobalVector("_WaveFormTex_TexelSize", Vector4.zero);
            Shader.SetGlobalVector("_AudioCaptureTextureFFT_TexelSize", Vector4.zero);
            Shader.SetGlobalVector("_AudioCaptureTextureWaveform_TexelSize", Vector4.zero);

            // === MASTER KEYWORD TEARDOWN ===
            // Disable the shader keyword globally to restore standard brush states
            Shader.DisableKeyword("AUDIO_REACTIVE");

            // 5. Lock the gate to block accidental re-allocations during padding loops
            m_IsTwoPassCleanupDone = true;

            // === UNIFIED LIFECYCLE RE-ENGAGEMENT HANDSHAKE (ANTI-RACE GUARD) ===
            // Explicitly clear initialization and playback state flags here so that the subsequent
            // automated calls from Offline45 evaluate to false and skip redundant asset destructions.
            m_SimulatedTexturesInitialized = false;
            m_IsPlaybackModeActive = false;

            if (m_BakedRecordingFrames != null)
            {
                m_BakedRecordingFrames.Clear();
                m_BakedRecordingFrames.TrimExcess();
            }
            // ===================================================================

            UnityEngine.Debug.LogWarning("[Audio Pipeline] SUCCESS: Cleared unmanaged textures, unbound global shaders, and locked lifecycle gates.");
        }



        public void ApplyBakedFrame(float progress, float progressLimit)
        {
            // Phase 2 Boundary Guard Hook: Reset variables and clear texture states past threshold
            if (progress > progressLimit)
            {
                ClearShadersToQuietState();
                if (m_TwoPassFftScratch != null) { DestroyImmediate(m_TwoPassFftScratch); m_TwoPassFftScratch = null; }
                if (m_TwoPassWaveScratch != null) { DestroyImmediate(m_TwoPassWaveScratch); m_TwoPassWaveScratch = null; }
                return;
            }

            if (m_BakedRecordingFrames == null || m_BakedRecordingFrames.Count == 0) return;

            BakedAudioFrame targetFrame = FindClosestBakedFrame(progress);

            // Inject saved numerical states directly back into active global vectors
            m_SimulatedBeatOutput = targetFrame.beatOutput;
            m_SimulatedBeatOutputAccum = targetFrame.beatOutputAccum;
            m_SimulatedBandLevels = targetFrame.bandPeakLevels;
            m_SimulatedAudioVolume = targetFrame.audioVolume;

            // =================================================================
            // UNIFIED OFFLINE RENDER TEXTURE GUARDIAN PIPELINE
            // =================================================================
            if (m_SimulatedFFTTex == null)
            {
                m_SimulatedFFTTex = new RenderTexture(256, 1, 0, RenderTextureFormat.ARGB32);
                m_SimulatedFFTTex.filterMode = FilterMode.Bilinear;
                m_SimulatedFFTTex.wrapMode = TextureWrapMode.Clamp;
                m_SimulatedFFTTex.Create();
            }
            if (m_SimulatedWaveFormTex == null)
            {
                m_SimulatedWaveFormTex = new RenderTexture(256, 1, 0, RenderTextureFormat.ARGB32);
                m_SimulatedWaveFormTex.filterMode = FilterMode.Bilinear;
                m_SimulatedWaveFormTex.wrapMode = TextureWrapMode.Clamp;
                m_SimulatedWaveFormTex.Create();
            }
            // =================================================================

            // ZERO-ALLOCATION FFT SCRATCH BUFFER INGESTION
            if (m_SimulatedFFTTex != null && targetFrame.fftTexturePixels != null && targetFrame.fftTexturePixels.Length > 0)
            {
                // Reuses the persistent memory slot to block heap generation leaks
                if (m_TwoPassFftScratch == null) m_TwoPassFftScratch = new Texture2D(256, 1, TextureFormat.RGBA32, false);
                m_TwoPassFftScratch.SetPixels(targetFrame.fftTexturePixels);
                m_TwoPassFftScratch.Apply();
                Graphics.Blit(m_TwoPassFftScratch, m_SimulatedFFTTex);
            }

            // ZERO-ALLOCATION WAVEFORM SCRATCH BUFFER INGESTION
            if (m_SimulatedWaveFormTex != null && targetFrame.waveFormTexturePixels != null && targetFrame.waveFormTexturePixels.Length > 0)
            {
                if (m_TwoPassWaveScratch == null) m_TwoPassWaveScratch = new Texture2D(256, 1, TextureFormat.RGBA32, false);
                m_TwoPassWaveScratch.SetPixels(targetFrame.waveFormTexturePixels);
                m_TwoPassWaveScratch.Apply();
                Graphics.Blit(m_TwoPassWaveScratch, m_SimulatedWaveFormTex);
            }

            // Force global materials to instantly adapt to values
            InjectSimulatedDataToShaders();

            // ABSOLUTE GLOBAL SHADER OVERWRITE GATES
            Shader.SetGlobalVector("_BeatOutput", m_SimulatedBeatOutput);
            Shader.SetGlobalVector("_BeatOutputAccum", m_SimulatedBeatOutputAccum);
            Shader.SetGlobalVector("_AudioVolume", m_SimulatedAudioVolume);
            Shader.SetGlobalVector("_PeakBandLevels", m_SimulatedBandLevels);
            Shader.SetGlobalTexture("_FFTTex", m_SimulatedFFTTex);
            Shader.SetGlobalTexture("_WaveFormTex", m_SimulatedWaveFormTex);
        }



        private BakedAudioFrame FindClosestBakedFrame(float progress)
        {
            int bestIndex = 0;
            float minDifference = float.MaxValue;

            for (int i = 0; i < m_BakedRecordingFrames.Count; i++)
            {
                float diff = Mathf.Abs(m_BakedRecordingFrames[i].normalizedProgress - progress);
                if (diff < minDifference)
                {
                    minDifference = diff;
                    bestIndex = i;
                }
            }
            return m_BakedRecordingFrames[bestIndex];
        }

        // File: VisualizerManager.cs
        private void ClearShadersToQuietState()
        {
            // 1. Fire native engine mappings first so our upcoming overrides can wipe them out
            InjectSimulatedDataToShaders();

            // 2. Black out all internal memory variables cleanly to quiet the engine logic
            m_SimulatedBeatOutput = Vector4.zero;
            m_SimulatedBeatOutputAccum = Vector4.zero;
            m_SimulatedBandLevels = Vector4.zero;
            m_SimulatedAudioVolume = Vector4.zero;

            m_BeatOutput = Vector4.zero;
            m_BeatOutputAccum = Vector4.zero;
            m_BandPeakLevelsOutput = Vector4.zero;
            m_AudioVolume = Vector4.zero;

            // 3. Black out FFT hardware Render Texture buffers safely
            if (m_SimulatedFFTTex != null && m_SimulatedFFTTex.IsCreated())
            {
                RenderTexture activeBuffer = RenderTexture.active;
                RenderTexture.active = m_SimulatedFFTTex;
                GL.Clear(true, true, Color.clear);
                RenderTexture.active = activeBuffer;
            }

            // 4. Black out WaveForm hardware Render Texture buffers safely
            if (m_SimulatedWaveFormTex != null && m_SimulatedWaveFormTex.IsCreated())
            {
                RenderTexture activeBuffer = RenderTexture.active;
                RenderTexture.active = m_SimulatedWaveFormTex;
                GL.Clear(true, true, Color.clear);
                RenderTexture.active = activeBuffer;
            }

            // 5. ABSOLUTE GLOBAL HARDWARE OVERWRITE GATES
            // Forcefully pump pure zero vectors into every possible property name 
            // used across the Open Brush material libraries to drop them to zero.
            Shader.SetGlobalVector("_BeatOutput", Vector4.zero);
            Shader.SetGlobalVector("_BeatOutputAccum", Vector4.zero);
            Shader.SetGlobalVector("_AudioVolume", Vector4.zero);
            Shader.SetGlobalVector("_PeakBandLevels", Vector4.zero);

            // 6. FIXED MATERIAL TRACKING RESETS FOR ADVANCED BRUSHES
            // Forcefully unbind vertex displacements and color counters completely
            Shader.SetGlobalFloat("_AudioVolumeAccum", 0f);
            Shader.SetGlobalVector("_FFTTex_TexelSize", Vector4.zero);
            Shader.SetGlobalVector("_WaveFormTex_TexelSize", Vector4.zero);
            Shader.SetGlobalVector("_AudioCaptureTextureFFT_TexelSize", Vector4.zero);
            Shader.SetGlobalVector("_AudioCaptureTextureWaveform_TexelSize", Vector4.zero);
        }



        // =================================================================
        // TWO-PASS OFFLINE DATA DISK SERIALIZATION (SAVE / LOAD)
        // =================================================================

        /// <summary>
        /// Serializes the entire baked audio frame array cache to a flat binary file on disk.
        /// </summary>
        public void SaveBakedAudioCache(string filePath)
        {
            if (m_BakedRecordingFrames == null || m_BakedRecordingFrames.Count == 0)
            {
                Debug.LogError("[VisualizerManager] Cannot save audio cache; baked frames list is empty.");
                return;
            }

            try
            {
                using (System.IO.BinaryWriter writer = new System.IO.BinaryWriter(System.IO.File.Open(filePath, System.IO.FileMode.Create)))
                {
                    // Write header version and total frame snapshot count
                    writer.Write(1); // File Version format tracking
                    writer.Write(m_BakedRecordingFrames.Count);

                    foreach (BakedAudioFrame frame in m_BakedRecordingFrames)
                    {
                        writer.Write(frame.normalizedProgress);

                        // Write Scalar Vectors (XYZW channels)
                        writer.Write(frame.beatOutput.x); writer.Write(frame.beatOutput.y); writer.Write(frame.beatOutput.z); writer.Write(frame.beatOutput.w);
                        writer.Write(frame.beatOutputAccum.x); writer.Write(frame.beatOutputAccum.y); writer.Write(frame.beatOutputAccum.z); writer.Write(frame.beatOutputAccum.w);
                        writer.Write(frame.bandPeakLevels.x); writer.Write(frame.bandPeakLevels.y); writer.Write(frame.bandPeakLevels.z); writer.Write(frame.bandPeakLevels.w);
                        writer.Write(frame.audioVolume.x); writer.Write(frame.audioVolume.y); writer.Write(frame.audioVolume.z); writer.Write(frame.audioVolume.w);

                        // Serialize FFT Texture pixel arrays
                        bool hasFFT = frame.fftTexturePixels != null && frame.fftTexturePixels.Length > 0;
                        writer.Write(hasFFT);
                        if (hasFFT)
                        {
                            writer.Write(frame.fftTexturePixels.Length);
                            foreach (Color c in frame.fftTexturePixels)
                            {
                                writer.Write(c.r); writer.Write(c.g); writer.Write(c.b); writer.Write(c.a);
                            }
                        }

                        // Serialize WaveForm Texture pixel arrays
                        bool hasWave = frame.waveFormTexturePixels != null && frame.waveFormTexturePixels.Length > 0;
                        writer.Write(hasWave);
                        if (hasWave)
                        {
                            writer.Write(frame.waveFormTexturePixels.Length);
                            foreach (Color c in frame.waveFormTexturePixels)
                            {
                                writer.Write(c.r); writer.Write(c.g); writer.Write(c.b); writer.Write(c.a);
                            }
                        }
                    }
                }
                Debug.LogError($"[VisualizerManager] Audio Reactivity cache saved successfully to disk. Total frames: {m_BakedRecordingFrames.Count}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[VisualizerManager] Failed to write audio cache file to disk: {e.Message}");
            }
        }


        /// <summary>
        /// VisualizerManager.LoadBakedAudioCache
        /// Deserializes a saved companion file from disk and populates the operational 
        /// playback cache. Uses configuration validation fences to block initialization drops.
        /// </summary>
        public void LoadBakedAudioCache(string filePath)
        {
            // --- ABSOLUTE INITIALIZATION CONFIGURATION FENCE ---
            // Verify that Open Brush's core app registries have fully parsed their command line
            // arguments before loading files to prevent uninitialized null reference parameter drops.
            if (App.Instance == null || App.Config == null)
            {
                UnityEngine.Debug.LogWarning("[VisualizerManager] Deferring audio cache load: Core App configuration registry is not yet initialized.");
                return;
            }
            // ---------------------------------------------------

            if (!System.IO.File.Exists(filePath))
            {
                Debug.LogError($"[VisualizerManager] Cannot load audio cache; file does not exist: {filePath}");
                return;
            }

            try
            {
                using (System.IO.BinaryReader reader = new System.IO.BinaryReader(System.IO.File.Open(filePath, System.IO.FileMode.Open)))
                {
                    int version = reader.ReadInt32();
                    int frameCount = reader.ReadInt32();

                    if (m_BakedRecordingFrames == null)
                    {
                        m_BakedRecordingFrames = new System.Collections.Generic.List<BakedAudioFrame>();
                    }
                    m_BakedRecordingFrames.Clear();

                    // =================================================================
                    // OFFLINE HEADLESS TEXTURE INITIALIZATION CORE
                    // =================================================================
                    // Forcefully instantiate physical memory canvas slots if running headlessly
                    // to prevent texture arrays from skipping during headless time advancement loops.
                    if (m_WaveFormTexture == null)
                    {
                        m_WaveFormTexture = new Texture2D(256, 1, TextureFormat.RGBA32, false);
                        m_WaveFormTexture.filterMode = FilterMode.Bilinear;
                        m_WaveFormTexture.wrapMode = TextureWrapMode.Clamp;
                    }
                    if (m_FFTTexture == null)
                    {
                        m_FFTTexture = new Texture2D(256, 1, TextureFormat.RGBA32, false);
                        m_FFTTexture.filterMode = FilterMode.Bilinear;
                        m_FFTTexture.wrapMode = TextureWrapMode.Clamp;
                    }
                    // =================================================================

                    for (int i = 0; i < frameCount; i++)
                    {
                        BakedAudioFrame frame = new BakedAudioFrame();
                        frame.normalizedProgress = reader.ReadSingle();

                        // 1. EXTRACT CRITICAL VECTOR STREAMS
                        // Pull the exact, pre-recorded performance values directly out of the binary stream
                        frame.beatOutput = new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        frame.beatOutputAccum = new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        frame.bandPeakLevels = new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        frame.audioVolume = new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

                        // 2. PROCESS FFT TEXTURE PIXEL BLOCKS
                        bool hasFFT = reader.ReadBoolean();
                        if (hasFFT)
                        {
                            int pixelLength = reader.ReadInt32();
                            frame.fftTexturePixels = new Color[pixelLength];
                            for (int p = 0; p < pixelLength; p++)
                            {
                                frame.fftTexturePixels[p] = new Color(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                            }
                        }

                        // 3. PROCESS WAVEFORM TEXTURE PIXEL BLOCKS
                        bool hasWave = reader.ReadBoolean();
                        if (hasWave)
                        {
                            int pixelLength = reader.ReadInt32();
                            frame.waveFormTexturePixels = new Color[pixelLength];
                            for (int p = 0; p < pixelLength; p++)
                            {
                                frame.waveFormTexturePixels[p] = new Color(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                            }
                        }

                        m_BakedRecordingFrames.Add(frame);
                    }

                    // === FIXED LIFECYCLE RE-ENGAGEMENT HANDSHAKE ===
                    // Trigger the master property method instead of doing a raw variable write
                    // to ensure all tracking counters and cleanup gates unlock simultaneously.
                    SetPlaybackMode(true);

                    Debug.LogError($"[VisualizerManager] Audio Reactivity cache loaded successfully from disk. Restored frames: {m_BakedRecordingFrames.Count}");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[VisualizerManager] Failed to read audio cache file from disk: {e.Message}");
                SetPlaybackMode(false);
            }
        }






    }

} // namespace TiltBrush

