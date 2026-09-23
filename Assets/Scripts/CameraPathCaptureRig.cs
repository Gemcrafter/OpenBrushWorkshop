// IMPORTANT GATHER THE LICENSE FROM OLDER FILES

using System;
using System.IO;
using UnityEngine;
using UnityEngine.Localization;
using System.Collections;

// =====================================================
// FAKE COMMAND LINE TEMPLATE - Offline Render Testing
// =====================================================
// Format: --Category.Key Value   (space separated, no = sign)
//
// === Core Offline Flow Control ===
// --Video.OfflineFPS 60
// --Video.OfflineResolution 2160
//
// === Rig-Level Overrides (recommended for testing) ===
// These take priority over config values when set on the rig
// --CameraPathCaptureRig.offlineCaptureFPS 60
// --CameraPathCaptureRig.captureWidthOverride 3840
// --CameraPathCaptureRig.captureHeightOverride 2160
// --CameraPathCaptureRig.targetDurationSeconds 8
// --CameraPathCaptureRig.offlineVideoQualityCRF 18
// --CameraPathCaptureRig.offlineVideoQualityPreset veryslow
//
// === Testing Helpers ===
// --CameraPathCaptureRig.m_DeleteFirstPassFolder true     (delete calibration folder after render)
// =====================================================




namespace TiltBrush
{
    public class CameraPathCaptureRig : MonoBehaviour
    {
        [SerializeField] private GameObject m_Object;
        [SerializeField] private GameObject m_Camera;
        [SerializeField] private LocalizedString m_PathRecorded;
        [SerializeField] private LocalizedString m_PathCancelled;

        private CameraPathPreviewWidget m_Widget;
        private ScreenshotManager m_Manager;
        private UsdPathSerializer m_VideoUsdSerializer;
        private Camera m_CameraComponent;
        private Vector2 m_CameraClipPlanesBase;

        [Header("Precise Frame Control")]
        [SerializeField] private float targetDurationSeconds = 8.0f;

        [Tooltip("Master switch for normal Open Brush recording behavior.\n\n" +
                 "• Checked = Enables two-pass timing using targetDurationSeconds (calibrated mode).\n" +
                 "• Unchecked = Normal single-pass recording (standard Open Brush behavior).\n\n" +
                 "This only affects normal in-game recording. Headless offline rendering uses its own dedicated flow.")]
        [SerializeField] private bool useFrameLimit = true;

        [Header("Second Pass")]
        [Tooltip(
            "Offline path mapping (headless two-pass only). Live recording is unchanged.\n\n" +
            "Prefix: Second Pass Traversal Percent is the fraction of the FRAME BUDGET that still moves along the path; " +
            "after that the camera freezes (optional pure black pad).\n\n" +
            "Compress: Second Pass Traversal Percent is the fraction of the USD PATH DURATION compressed into the full clip of length T " +
            "(pathSpan = pathDuration * ratio). Motion runs for all N frames.\n\n" +
            "Default: Prefix.")]
        [SerializeField] private OfflinePathMapping m_OfflinePathMapping = OfflinePathMapping.Prefix;

        [Tooltip(
            "Meaning depends on Offline Path Mapping (see that field).\n\n" +
            "Prefix: percent of frame budget that advances along the path (remainder = hold/pad).\n" +
            "Compress: percent of path duration mapped into the full target duration T.\n\n" +
            "Valid range: 1 to 100 inclusive. Values outside that range hard-fail the offline job.")]
        [Range(1, 100)]
        [SerializeField] private int secondPassTraversalPercent = 100;

        [Header("Two-Pass Offline Post-Processing Options")]
        [Tooltip("When checked, the final trailing frame window (calculated dynamically from your Second Pass Traversal Percent slider) \n" +
               " will bypass camera texture capture entirely and write pure (0,0,0) zero-byte arrays directly to disk.")]
        [SerializeField] private bool m_WritePaddingAsPureBlack = false;
        // Required for backwards comatibility to check the recording mode
        // Public exposure of toggles to ensure safe backward-compatible audio routing
        public bool UseFrameLimit => useFrameLimit;
        public int SecondPassTraversalPercent => secondPassTraversalPercent;
        public OfflinePathMapping OfflinePathMappingMode => m_OfflinePathMapping;



        [Header("Video Quality")]
        [Tooltip("CRF (Constant Rate Factor) controls video quality vs file size.\n" +
                 "Lower = better quality, larger file. Recommended range: 14–23.")]
        [SerializeField] private int videoQualityCRF = 14;

        [Tooltip("Encoding preset. Slower presets produce better quality at the same file size.\n" +
                 "Common values: veryslow, slower, slow, medium.")]
        [SerializeField] private string videoQualityPreset = "veryslow";

        [Header("Live Recording")]
        [Tooltip("Target FPS used when calculating how many frames to capture during normal in-game two-pass recording.\n" +
                 "Only affects the live recording path.")]
        [SerializeField] private int liveCaptureFPS = 30;

        [Tooltip("Framerate of the final .mp4 preview video generated after normal in-game recording.\n" +
                 "This controls the playback speed of the output video.")]
        [SerializeField] private int liveOutputFramerate = 30;

        [Header("Offline Recording")]
        [Tooltip("Target FPS used to calculate time scale and frame count during headless offline rendering (--renderCameraPath).\n\n" +
                 "• Set a value > 0 to explicitly control offline capture rate from this rig (advanced).\n" +
                 "• Leave at 0 to use UserConfig.Video.OfflineFPS (supports normal Open Brush offline rendering via generated .bat files and command line).")]
        [SerializeField] private int offlineCaptureFPS = 60;


        [Tooltip("Framerate of the final .mp4 video after headless offline rendering.\n" +
                 "Leave at 0 to use the same value as offlineCaptureFPS when final video generation is added to the offline path.")]
        [SerializeField] private int offlineOutputFPS = 60;

        [Header("Capture Resolution Override")]
        [Tooltip("Set both values > 0 to force a specific resolution during still-frame capture (e.g. 3840 x 2160).\n" +
                 "Leave both at 0 to use the resolution from UserConfig or command line.")]
        [SerializeField] public int captureWidthOverride = 0;
        [SerializeField] public int captureHeightOverride = 0;

        [Tooltip("When enabled, Offline47 muxes a companion .wav from the live recording into the final offline .mp4.\n" +
         "When disabled, offline output is video-only.\n" +
         "Looks for a .wav next to the source .usda path (e.g. Untitled_814_01.wav).")]
        [SerializeField] private bool m_OfflineMuxCompanionWav = true;


        [Header("Offline ffmpeg encode")]
        [Tooltip(
            "Standard = current Offline47 ffmpeg line.\n" +
            "WindowsPlayerSafe = cfr + faststart for Windows Media Player / Movies & TV.\n" +
            "Does not change resolution, CRF, frame count, path, or BPM timing.")]
        [SerializeField] private OfflineFfmpegEncodePath offlineFfmpegEncodePath = OfflineFfmpegEncodePath.Standard;


        [Header("Offline Video Quality")]
        [Tooltip("CRF (Constant Rate Factor) used when generating the final .mp4 video " +
         "from still frames during headless offline rendering.\n" +
         "Lower = better quality, larger file. Recommended range: 14–23.")]
        [SerializeField] private int offlineVideoQualityCRF = 14;

        [Tooltip("Encoding preset used when generating the final .mp4 video " +
                 "from still frames during headless offline rendering.\n" +
                 "Slower presets produce better quality at the same file size.\n" +
                 "Common values: veryslow, slower, slow, medium.")]
        [SerializeField] private string offlineVideoQualityPreset = "veryslow";

        // Accessor (for future use in Offline47 and potential command line overrides)
        public int OfflineVideoQualityCRF => offlineVideoQualityCRF;
        public string OfflineVideoQualityPreset => offlineVideoQualityPreset;

        // Live Recording accessors (used by normal in-game two-pass)
        public int LiveCaptureFPS => liveCaptureFPS;
        public int LiveOutputFramerate => liveOutputFramerate;

        // Offline Recording accessors (used by headless two-pass)
        public int OfflineCaptureFPS => offlineCaptureFPS;
        public int OfflineOutputFPS => offlineOutputFPS;

        // Video Quality (shared)
        public int VideoQualityCRF => videoQualityCRF;
        public string VideoQualityPreset => videoQualityPreset;

        private string m_FinalFrameFolderPath = "";




        public bool Enabled => m_Object.activeSelf;
        public static CameraPathCaptureRig m_Instance;

        public ScreenshotManager Manager => m_Manager;

        // === Two-Pass State ===
        // private bool m_IsTwoPassSession = false;
        // === Two-Pass State ===
        private int m_FirstPassActualFrames = 0;

        // Set in PerformStep2 to the first-pass exporter FilePath (base path).
        // Used by CancelledRecordingCleanup so a second-pass cancel can also remove first-pass artifacts.
        private string m_FirstPassFolderPath = "";

        // Latest recording attempt paths (updated in Step 1 and again in Step 6).
        // Used by CancelledRecordingCleanup after Active* have been cleared.
        private string m_CurrentRecordingBasePath = "";
        private string m_CurrentFramesFolderPath = "";


        // === NEW PUBLIC LIFECYCLE ACCESSORS FOR ALIGNED AUDIO RECORDING ===
        // Action A Fix: Restrict this property to evaluate true ONLY during an active
        // first-pass run (m_FirstPassActualFrames == 0). This blinds the geometric
        // completion gate inside CameraPathPreviewWidget.OnUpdate during Pass 2,
        // ensuring that only the frame-count budget in SerializerNewUsdFrame is
        // allowed to terminate the second pass. The -1 sentinel set in Step 10
        // also keeps the property false after shutdown.
        public bool IsRecordingActive
        {
            get
            {
                return m_FirstPassActualFrames == 0 &&
                       m_Widget != null &&
                       WidgetManager.m_Instance != null &&
                       WidgetManager.m_Instance.FollowingPath;
            }
        }

        public bool IsFirstPassActive
        {
            get
            {
                return m_FirstPassActualFrames == 0 && IsRecordingActive;
            }
        }

        private float m_SecondPassSpeedMultiplier = 1.0f;
        private int m_SecondPassStartFrameCount = 0;

        // Pass 2 pad region: true after geometric path completes; capture continues until frame budget N.
        // Cleared at PerformStep6 start and PerformStep10 reset. Not used in Pass 1.
        private bool m_SecondPassPathContentComplete = false;

        public bool IsSecondPassInPadRegion
        {
            get
            {
                return m_SecondPassPathContentComplete;
            }
        }

        public bool WritePaddingAsPureBlack
        {
            get
            {
                return m_WritePaddingAsPureBlack;
            }
        }

        // Runtime: exporter FrameCount at geometric path complete (content end). Not a user setting.
        // Used when AudioCaptureManager.m_AllowAudioDuringPadding is false to silence pad samples.
        private int m_SecondPassContentFrameCount = 0;

        public int SecondPassContentFrameCount
        {
            get
            {
                return m_SecondPassContentFrameCount;
            }
        }




        // Headless Two-Pass State
        private int m_OfflineFirstPassFrameCount = 0;
        private string m_OfflineFirstPassFolderPath = "";
        private float m_OfflineTimeScale = 1.0f;
        private int m_OfflineTargetFrameCount = 0;
        private bool m_DeleteFirstPassFolder = false;
        private UsdPathSerializer m_OfflineUsdSerializer;
        private VideoRecorder m_OfflineVideoRecorder;
        private ScreenshotManager m_OfflineScreenshotManager;





        // Stores the frames folder path for the offline second pass
        // so Offline47 can generate the final video from the correct location.
        private string m_OfflineFinalFrameFolderPath = "";
        // === Offline Second Pass State ===
        private string m_OfflineSecondPassFolderPath;


        private Coroutine m_TwoPassDelayCoroutine;
        private Coroutine m_Step3WaitCoroutine;
        private Coroutine m_Step8WaitCoroutine;

        // Headless Flow
        private Coroutine m_OfflineCompletionCheckCoroutine;
        private Coroutine m_OfflineScaledTimeCoroutine;
        private string m_OfflineOriginalFilePath;   // Stores first pass folder path for diagnostics/restart

        // Resolved render parameters (populated in Offline38)
        private int m_ResolvedOfflineFPS = 60;
        private float m_ResolvedDurationSeconds = 8f;
        private int m_ResolvedWidth = 0;
        private int m_ResolvedHeight = 0;

        // new serializer specific to second pass onine recording
        private UsdPathSerializer m_SecondPassUsdSerializer;

        // Used to compare first-pass end position vs second-pass starting position
        private Vector3 m_FirstPassEndPosition;
        private Quaternion m_FirstPassEndRotation;

        /// <summary>
        /// Offline second-pass path mapping (headless only). Live PerformStep* is unchanged.
        /// Prefix: fraction of frame budget advances path, then freeze/pad.
        /// Compress: fraction of USD path duration is mapped into the full clip of length T.
        /// </summary>
        public enum OfflinePathMapping
        {
            Prefix = 0,
            Compress = 1
        }

        /// <summary>
        ///  Ability to switch ffmpeg commands as the defualt gives windows packaged videoo players
        ///  issues with decoding resulting in glitches on high resolution frames
        /// </summary>
        public enum OfflineFfmpegEncodePath
        {
            Standard = 0,
            WindowsPlayerSafe = 1
        }


        // =====================================================================
        // UNITY LIFECYCLE
        // =====================================================================


        void Awake()
        {
            m_Instance = this;

            App.Switchboard.ToolChanged += OnToolChanged;
            App.Switchboard.CameraPathVisibilityChanged += RefreshVisibility;
            App.Switchboard.CameraPathDeleted += RefreshVisibility;
            App.Switchboard.AllWidgetsDestroyed += RefreshVisibility;
            App.Switchboard.CameraPathCreated += RefreshVisibility;
            App.Switchboard.CameraPathKnotChanged += RefreshVisibility;
            App.Switchboard.CurrentCameraPathChanged += RefreshVisibility;
            App.Scene.LayerCanvasesUpdate += OnLayerCanvasesUpdate;
            App.Scene.MainCanvas.PoseChanged += OnPoseChanged;
        }

        void OnDestroy()
        {
            App.Switchboard.ToolChanged -= OnToolChanged;
            App.Switchboard.CameraPathVisibilityChanged -= RefreshVisibility;
            App.Switchboard.CameraPathDeleted -= RefreshVisibility;
            App.Switchboard.AllWidgetsDestroyed -= RefreshVisibility;
            App.Switchboard.CameraPathCreated -= RefreshVisibility;
            App.Switchboard.CameraPathKnotChanged -= RefreshVisibility;
            App.Switchboard.CurrentCameraPathChanged -= RefreshVisibility;
            App.Scene.LayerCanvasesUpdate -= OnLayerCanvasesUpdate;
            App.Scene.MainCanvas.PoseChanged -= OnPoseChanged;
        }

        private void OnLayerCanvasesUpdate()
        {
            RefreshVisibility();
        }

        public void Init()
        {
            m_Object.SetActive(true);
            m_Object.SetActive(false);

            m_VideoUsdSerializer = m_Camera.GetComponentInChildren<UsdPathSerializer>(true);
            m_Manager = m_Camera.GetComponentInChildren<ScreenshotManager>(true);
            m_CameraComponent = m_Manager.GetComponent<Camera>();
            m_CameraClipPlanesBase.x = m_CameraComponent.nearClipPlane;
            m_CameraClipPlanesBase.y = m_CameraComponent.farClipPlane;

            m_Widget = GetComponentInChildren<CameraPathPreviewWidget>();
            m_Widget.gameObject.SetActive(false);
        }

        // =====================================================================
        // PUBLIC API
        // =====================================================================

        public void SetPreviewWidgetCompletionPercent(float zeroToOne)
        {
            m_Widget.SetCompletionAlongPath(zeroToOne);
        }

        public void OverridePreviewWidgetPathT(PathT? t)
        {
            m_Widget.OverridePathT = t;
        }

        public float? GetCompletionOfCameraAlongPath()
        {
            return m_Widget.GetCompletionAlongPath();
        }

        public void UpdateCameraTransform(Transform xf)
        {
            m_Camera.transform.position = xf.position;
            m_Camera.transform.rotation = xf.rotation;
        }

        public void SetFov(float fov)
        {
            m_CameraComponent.fieldOfView = fov;
        }


        public void NotifySecondPassPathContentComplete()
        {
            if (m_SecondPassPathContentComplete)
            {
                return;
            }

            m_SecondPassPathContentComplete = true;

            m_SecondPassContentFrameCount = 0;
            if (VideoRecorderUtils.ActiveStillFrameExporter != null)
            {
                m_SecondPassContentFrameCount = VideoRecorderUtils.ActiveStillFrameExporter.FrameCount;
            }

            Debug.LogError(
                "[CameraPathCaptureRig.NotifySecondPassPathContentComplete] Pass 2 path content complete — " +
                $"entering pad region (hold pose / optional black) until frame budget. " +
                $"ContentFrameCount={m_SecondPassContentFrameCount}");
        }


        // =====================================================================
        // LINEAR TWO-PASS SEQUENCE
        // =====================================================================

        // Update 2079 CameraPathCaptureRig.PerformStep1_StartFirstPassRecording
        // Purpose: Start first pass with no frame limit so it runs until the path naturally ends.
        // Added explicit confirmation log that target frame count is set to -1.

        private void PerformStep1_StartFirstPassRecording()
        {
            // Action B Fix: Forcefully blow away the persistent -1 shutdown token
            // back down to a pure zero state the exact millisecond a brand new pass begins.
            m_FirstPassActualFrames = 0;

            Debug.LogError("[CameraPathCaptureRig.PerformStep1_StartFirstPassRecording] Step 1 - Starting first pass at full speed.");

            var currentPathData = WidgetManager.m_Instance.GetCurrentCameraPath();
            if (currentPathData != null && currentPathData.WidgetScript != null)
            {
                currentPathData.WidgetScript.Path.TemporarySpeedMultiplier = 1.0f;
            }

            // First pass: Run until the path naturally ends.
            // Do NOT apply a target frame count here.
            VideoRecorderUtils.SetTargetFrameCount(-1);
            Debug.LogError("[PerformStep1] First pass target frame count explicitly set to -1 (no limit).");

            m_Widget.ResetToPathStart();
            m_Widget.TintForRecording(true);
            UpdateCameraTransform(m_Widget.transform);
            WidgetManager.m_Instance.FollowingPath = true;

            SketchSurfacePanel.m_Instance.EnableSpecificTool(BaseTool.ToolType.CameraPathTool);
            App.Switchboard.TriggerCameraPathModeChanged(CameraPathTool.Mode.Recording);

            // FORCE VISUALIZER CACHE INTO BAKE MODE BEFORE PASS 1 BEGINS
            VideoRecorderUtils.SetVisualizerPlaybackPass(false);

            // ====================================================================
            // LIVE-CAPTURE AUDIO RESILIENCE HOOK (Action C Integration)
            // ====================================================================
            // Explicitly activate the live capture tracking state at the very start
            // of Pass 1. This guarantees that your interactive live sequence holds
            // a true state throughout the entire journey, preventing any down-stream
            // layout loops from falling into headless simulation code traps.
            VideoRecorderUtils.SetLiveAudioCaptureActive(true);

            VideoRecorderUtils.StartVideoCapture(
                MultiCamTool.GetSaveName(MultiCamStyle.Video),
                m_Manager.GetComponent<VideoRecorder>(),
                m_VideoUsdSerializer);

            // Capture the base path and frames folder immediately after StartVideoCapture
            // so CancelledRecordingCleanup can still delete them even after Active* are cleared.
            m_CurrentRecordingBasePath = "";
            m_CurrentFramesFolderPath = "";
            if (VideoRecorderUtils.ActiveStillFrameExporter != null)
            {
                m_CurrentRecordingBasePath = VideoRecorderUtils.ActiveStillFrameExporter.FilePath;
                if (!string.IsNullOrEmpty(m_CurrentRecordingBasePath))
                {
                    string dir = Path.GetDirectoryName(m_CurrentRecordingBasePath);
                    string baseName = Path.GetFileNameWithoutExtension(m_CurrentRecordingBasePath);
                    m_CurrentFramesFolderPath = Path.Combine(dir, baseName + "_frames");
                }
            }

            Debug.LogError(
                $"[PerformStep1] Captured recording paths for cancel cleanup: " +
                $"base=\"{m_CurrentRecordingBasePath}\" | framesFolder=\"{m_CurrentFramesFolderPath}\"");
        }



        // Update 2032
        private void PerformStep2_StopFirstPassAndCollectData()
        {
            Debug.LogError("[CameraPathCaptureRig.PerformStep2_StopFirstPassAndCollectData] Step 2 - First pass complete. Collecting frame data.");

            if (VideoRecorderUtils.ActiveStillFrameExporter != null)
            {
                m_FirstPassActualFrames = VideoRecorderUtils.ActiveStillFrameExporter.FrameCount;
                m_FirstPassFolderPath = VideoRecorderUtils.ActiveStillFrameExporter.FilePath;

                Debug.LogError("[PerformStep2] First pass data collected successfully.");
                Debug.LogError($"[PerformStep2] Frames: {m_FirstPassActualFrames} | Folder: {m_FirstPassFolderPath}");

                if (m_FirstPassActualFrames == 0)
                {
                    Debug.LogError("[PerformStep2] WARNING - First pass frame count is 0. This may indicate a capture issue.");
                }
            }
            else
            {
                Debug.LogError("[CameraPathCaptureRig.PerformStep2_StopFirstPassAndCollectData] WARNING - No ActiveStillFrameExporter found.");
                m_FirstPassActualFrames = 0;
                m_FirstPassFolderPath = "";
            }
        }


        // Update 2021
        private void PerformStep3_EnsureFirstPassFramesAreWritten()
        {
            Debug.LogError("[CameraPathCaptureRig.PerformStep3_EnsureFirstPassFramesAreWritten] Step 3 - Ensuring first pass frames are written to disk.");

            if (m_Step3WaitCoroutine != null)
                StopCoroutine(m_Step3WaitCoroutine);

            m_Step3WaitCoroutine = StartCoroutine(RunStep3FrameConfirmation());
        }

        private System.Collections.IEnumerator RunStep3FrameConfirmation()
        {
            const float timeoutSeconds = 30f;
            float startTime = Time.realtimeSinceStartup;
            int expectedFrames = m_FirstPassActualFrames;

            Debug.LogError("[CameraPathCaptureRig.RunStep3FrameConfirmation] Step 3 waiting started. " +
                           $"Expecting {expectedFrames} frames to be written to disk.");

            while (true)
            {
                int currentFrames = 0;
                if (VideoRecorderUtils.ActiveStillFrameExporter != null)
                {
                    currentFrames = VideoRecorderUtils.ActiveStillFrameExporter.FrameCount;
                }

                if (currentFrames >= expectedFrames)
                {
                    Debug.LogError("[CameraPathCaptureRig.RunStep3FrameConfirmation] Step 3 complete. " +
                                   $"All expected frames ({expectedFrames}) have been written.");
                    yield break;
                }

                if (Time.realtimeSinceStartup - startTime > timeoutSeconds)
                {
                    Debug.LogError("[CameraPathCaptureRig.RunStep3FrameConfirmation] WARNING - Timeout waiting for frames to be written.");
                    Debug.LogError($"[RunStep3FrameConfirmation] Expected: {expectedFrames} | Current: {currentFrames} | Elapsed: {timeoutSeconds}s");
                    yield break;
                }

                yield return new WaitForSeconds(0.25f);
            }
        }
        // Update 2077 CameraPathCaptureRig.PerformStep4_CalculateSecondPassSpeed
        // Purpose: Calculate second pass speed based on desired traversal percentage.
        // Lower percentage = faster speed = camera stops earlier on the path while still
        // hitting close to the target frame count.
        private void PerformStep4_CalculateSecondPassSpeed()
        {
            Debug.LogError("[CameraPathCaptureRig.PerformStep4_CalculateSecondPassSpeed] Step 4 - Calculating second pass speed.");
            Debug.LogError("[PerformStep4] Using secondPassTraversalPercent = " + secondPassTraversalPercent + "%");

            if (m_FirstPassActualFrames > 0 && targetDurationSeconds > 0f)
            {
                // Use liveCaptureFPS explicitly for the calibrated two-pass path
                int targetFrames = Mathf.RoundToInt(targetDurationSeconds * liveCaptureFPS);
                float idealMultiplier = (float)m_FirstPassActualFrames / targetFrames;
                float traversalRatio = secondPassTraversalPercent / 100f;
                float finalMultiplier = idealMultiplier / traversalRatio;

                m_SecondPassSpeedMultiplier = finalMultiplier;

                Debug.LogError($"[Calibration] Using liveCaptureFPS = {liveCaptureFPS} | " +
                               $"FirstPassFrames: {m_FirstPassActualFrames} | " +
                               $"TargetFrames: {targetFrames} | " +
                               $"Ideal: {idealMultiplier:F3} | " +
                               $"Traversal: {secondPassTraversalPercent}% | " +
                               $"FinalMultiplier: {finalMultiplier:F3}");

                var currentPathData = WidgetManager.m_Instance.GetCurrentCameraPath();
                if (currentPathData != null && currentPathData.WidgetScript != null)
                {
                    CameraPath path = currentPathData.WidgetScript.Path;
                    path.TemporarySpeedMultiplier = finalMultiplier;

                    if (path.NumPositionKnots > 0)
                    {
                        Debug.LogError("[Second Pass Speed Sample] Start: " + path.GetSpeed(new PathT(0)).ToString("F2"));
                        int midIndex = path.NumPositionKnots / 2;
                        Debug.LogError("[Second Pass Speed Sample] Middle: " + path.GetSpeed(new PathT(midIndex)).ToString("F2"));
                        int endIndex = path.NumPositionKnots - 1;
                        Debug.LogError("[Second Pass Speed Sample] End: " + path.GetSpeed(new PathT(endIndex)).ToString("F2"));
                    }
                }
            }
            else
            {
                Debug.LogError("[CameraPathCaptureRig.PerformStep4_CalculateSecondPassSpeed] WARNING - Invalid first pass data.");
                m_SecondPassSpeedMultiplier = 1.0f;
            }
        }

        // Update 2035 CameraPathCaptureRig.PerformStep5_ShowWaitForSecondPass
        // Purpose: Step 5 of the linear two-pass sequence.
        // Activates the second-pass visual indicator and starts the delay before the second pass.
        private void PerformStep5_ShowWaitForSecondPass()
        {
            Debug.LogError("[CameraPathCaptureRig.PerformStep5_ShowWaitForSecondPass] Step 5 - Starting wait before second pass.");

            // Activate distinct visual indicator for second pass
            if (m_Widget != null)
            {
                m_Widget.TintForSecondPass(true);
            }

            if (m_TwoPassDelayCoroutine != null)
                StopCoroutine(m_TwoPassDelayCoroutine);

            m_TwoPassDelayCoroutine = StartCoroutine(RunSecondPassDelay());
        }

        private System.Collections.IEnumerator RunSecondPassDelay()
        {
            Debug.LogError("[CameraPathCaptureRig.RunSecondPassDelay] Delay coroutine started. Waiting 3 seconds before checking info cards...");

            yield return new WaitForSeconds(3.0f);

            // Wait for any active info cards to finish (prevents overlap with Step 6)
            if (InfoCardAnimation.s_NumCards > 0)
            {
                Debug.LogError($"[RunSecondPassDelay] Info cards still active ({InfoCardAnimation.s_NumCards}). Waiting for them to clear...");

                while (InfoCardAnimation.s_NumCards > 0)
                {
                    yield return new WaitForSeconds(0.5f);
                }

                Debug.LogError("[RunSecondPassDelay] Info cards cleared. Proceeding to second pass.");
            }
            else
            {
                Debug.LogError("[RunSecondPassDelay] No active info cards. Proceeding immediately to second pass.");
            }

            Debug.LogError("[CameraPathCaptureRig.RunSecondPassDelay] Delay complete. Starting second pass.");
            PerformStep6_StartSecondPassRecording();
        }


        // Update 3003 CameraPathCaptureRig.PerformStep6_StartSecondPassRecording
        // Added resolution source logging at the start of the second pass.
        // Wired liveCaptureFPS into target frame calculation for the calibrated two-pass path.
        // Naming fix: cache GetSaveName once and reuse for both StartVideoCapture and
        // StartHiFiAudioRecording so the WAV shares the same base name as the frames folder.
        // HiFi audio source: SystemAudioMonitor WASAPI bridge (AppendHiFiSamplePair).
        // AudioFilterProxy is NOT attached here — the Unity listener mix is silent for
        // system/desktop audio and would interleave zeros into the HiFi buffer.
        // Phase 2: clear pad-region flag so Pass 2 starts in content mode.


        private void PerformStep6_StartSecondPassRecording()
        {
            Debug.LogError("[CameraPathCaptureRig.PerformStep6_StartSecondPassRecording] Step 6 - Starting second pass.");

            // Pass 2 starts in content region (path motion); pad flag set later on geometric complete.
            m_SecondPassPathContentComplete = false;
            m_SecondPassContentFrameCount = 0;

            // === CAPTURE RESOLUTION SOURCE LOGGING (Second Pass) ===
            if (captureWidthOverride > 0 && captureHeightOverride > 0)
            {
                Debug.LogError($"[CameraPathCaptureRig] CAPTURE RESOLUTION OVERRIDE ACTIVE (Second Pass): {captureWidthOverride}x{captureHeightOverride}");
            }
            else if (App.UserConfig.Video.OfflineResolution > 0)
            {
                Debug.LogError($"[CameraPathCaptureRig] Using OfflineResolution from config (Second Pass): {App.UserConfig.Video.OfflineResolution}");
            }
            else
            {
                Debug.LogError($"[CameraPathCaptureRig] Using default Resolution from config (Second Pass): {App.UserConfig.Video.Resolution}");
            }
            // === END RESOLUTION SOURCE LOGGING ===

            if (VideoRecorderUtils.ActiveStillFrameExporter != null)
            {
                m_SecondPassStartFrameCount = VideoRecorderUtils.ActiveStillFrameExporter.FrameCount;
            }
            else
            {
                m_SecondPassStartFrameCount = 0;
            }

            if (m_SecondPassSpeedMultiplier > 0f)
            {
                var currentPathData = WidgetManager.m_Instance.GetCurrentCameraPath();
                if (currentPathData != null && currentPathData.WidgetScript != null)
                {
                    Debug.LogError("[PerformStep6] Applying speed multiplier: " + m_SecondPassSpeedMultiplier);
                    currentPathData.WidgetScript.Path.TemporarySpeedMultiplier = m_SecondPassSpeedMultiplier;
                }
            }

            if (useFrameLimit && targetDurationSeconds > 0f)
            {
                // Use liveCaptureFPS explicitly for the calibrated two-pass path
                int calculatedFrames = Mathf.RoundToInt(targetDurationSeconds * liveCaptureFPS);
                VideoRecorderUtils.SetTargetFrameCount(calculatedFrames);
                Debug.LogError($"[PerformStep6] Using liveCaptureFPS = {liveCaptureFPS} for target frame calculation. Calculated frames: {calculatedFrames}");
            }
            else
            {
                VideoRecorderUtils.SetTargetFrameCount(-1);
            }

            m_Widget.ResetToPathStart();
            m_Widget.TintForSecondPass(true);
            UpdateCameraTransform(m_Widget.transform);
            WidgetManager.m_Instance.FollowingPath = true;

            SketchSurfacePanel.m_Instance.EnableSpecificTool(BaseTool.ToolType.CameraPathTool);

            // Action D Fix 1: Cleans up the code bloat and deletes the duplicate trigger event loop.
            App.Switchboard.TriggerCameraPathModeChanged(CameraPathTool.Mode.Recording);

            // ====================================================================
            // LIVE-CAPTURE AUDIO RESILIENCE INITIALIZATION HOOKS (Action D Fix 2 & 3)
            // ====================================================================
            // Explicitly toggle on our new live capture tracking state. This keeps
            // the real-time audio hardware stream online and shields the recording run
            // from processing any static offline simulation or playback injection loops.
            VideoRecorderUtils.SetLiveAudioCaptureActive(true);

            // NAMING FIX: Capture the save path once so the sequence counter does not
            // advance between StartVideoCapture and StartHiFiAudioRecording.
            // Both the frames folder and the .wav will share the same base name.
            // Second pass gets its own name (_01) via GetSaveName — first pass was _00.
            string targetMasterVideoPath = MultiCamTool.GetSaveName(MultiCamStyle.Video);
            Debug.LogError($"[GetSaveName Debug] Pass 2 cached path = '{targetMasterVideoPath}'");

            VideoRecorderUtils.StartVideoCapture(
                targetMasterVideoPath,
                m_Manager.GetComponent<VideoRecorder>(),
                m_VideoUsdSerializer);

            // DIAGNOSTIC LOG HOOK: Exposes whether the exporter is inheriting stale data from Pass 1
            if (VideoRecorderUtils.ActiveStillFrameExporter != null)
            {
                Debug.LogError($"[Pass 2 Debug] Exporter FrameCount at start of Pass 2 = {VideoRecorderUtils.ActiveStillFrameExporter.FrameCount} | IsCapturing = {VideoRecorderUtils.ActiveStillFrameExporter.IsCapturing}");
            }

            // Session paths for cancel cleanup (prefix wipe uses these)
            m_CurrentRecordingBasePath = "";
            m_CurrentFramesFolderPath = "";
            m_FinalFrameFolderPath = "";

            if (VideoRecorderUtils.ActiveStillFrameExporter != null)
            {
                string videoFilePath = VideoRecorderUtils.ActiveStillFrameExporter.FilePath;
                m_CurrentRecordingBasePath = videoFilePath;

                if (!string.IsNullOrEmpty(videoFilePath))
                {
                    string parentDir = Path.GetDirectoryName(videoFilePath);
                    string baseName = Path.GetFileNameWithoutExtension(videoFilePath);
                    m_CurrentFramesFolderPath = Path.Combine(parentDir, baseName + "_frames");
                    m_FinalFrameFolderPath = m_CurrentFramesFolderPath;
                    Debug.LogError("[PerformStep6] Derived frames folder: " + m_FinalFrameFolderPath);
                }
            }

            Debug.LogError(
                $"[PerformStep6] Session paths for cancel cleanup: " +
                $"base=\"{m_CurrentRecordingBasePath}\" | framesFolder=\"{m_CurrentFramesFolderPath}\"");

            // NOTE: AudioFilterProxy is intentionally NOT attached for system/desktop audio.
            // HiFi samples are fed from SystemAudioMonitor.SingleBlockNotificationStreamOnSingleBlockRead
            // via AudioCaptureManager.AppendHiFiSamplePair. The Unity AudioListener mix is silent
            // for WASAPI-captured music and would only inject zeros into the buffer.

            // Task 6 Hook: Wake up and start parallel high-fidelity capture using the exact synchronized video path
            if (AudioCaptureManager.m_Instance != null)
            {
                // Reuse the cached path so the .wav shares the same base name as the frames/video.
                Debug.LogError($"[GetSaveName Debug] Pass 2 StartHiFiAudioRecording path (reused cache) = '{targetMasterVideoPath}'");
                AudioCaptureManager.m_Instance.StartHiFiAudioRecording(targetMasterVideoPath);
            }
        }




        // Update 2022
        private void PerformStep7_StopSecondPass()
        {
            Debug.LogError("[CameraPathCaptureRig.PerformStep7_StopSecondPass] Step 7 - Stopping second pass.");

            if (m_TwoPassDelayCoroutine != null)
            {
                Debug.LogError("[PerformStep7] Stopping active TwoPassDelay coroutine.");
                StopCoroutine(m_TwoPassDelayCoroutine);
                m_TwoPassDelayCoroutine = null;
            }
            else
            {
                Debug.LogError("[PerformStep7] No active TwoPassDelay coroutine to stop.");
            }
        }

        // Update 2048 CameraPathCaptureRig.PerformStep8_EnsureSecondPassFramesAreWritten
        // CameraPathCaptureRig.PerformStep8_EnsureSecondPassFramesAreWritten
        private void PerformStep8_EnsureSecondPassFramesAreWritten()
        {
            Debug.LogError("[CameraPathCaptureRig.PerformStep8_EnsureSecondPassFramesAreWritten] Step 8 - Ensuring second pass frames are written to disk.");
            if (m_Step8WaitCoroutine != null)
                StopCoroutine(m_Step8WaitCoroutine);
            m_Step8WaitCoroutine = StartCoroutine(RunStep8FrameConfirmation());
        }


        // Update 2055 CameraPathCaptureRig.RunStep8FrameConfirmation
        // Updated to calculate expected frames using liveCaptureFPS directly
        // (consistent with how PerformStep6 sets the target frame count).
        // CameraPathCaptureRig.RunStep8FrameConfirmation
        // CameraPathCaptureRig.RunStep8FrameConfirmation
        // Update 2055 CameraPathCaptureRig.RunStep8FrameConfirmation
        // Updated to calculate expected frames using liveCaptureFPS directly
        // (consistent with how PerformStep6 sets the target frame count).
        // Also performs clean teardown of the temporary AudioFilterProxy after
        // the HiFi snapshot has been taken.
        private System.Collections.IEnumerator RunStep8FrameConfirmation()
        {
            const float timeoutSeconds = 30f;
            float startTime = Time.realtimeSinceStartup;

            // Use liveCaptureFPS explicitly (must match how PerformStep6 calculates target frames)
            int expectedNewFrames = Mathf.RoundToInt(targetDurationSeconds * liveCaptureFPS);
            int startCount = m_SecondPassStartFrameCount;
            int targetCount = startCount + expectedNewFrames;

            Debug.LogError("[CameraPathCaptureRig.RunStep8FrameConfirmation] Waiting for second pass frames. " +
                           $"Using liveCaptureFPS = {liveCaptureFPS} | Start: {startCount} | Target: {targetCount}");

            bool timedOut = false;

            while (true)
            {
                int currentFrames = 0;
                if (VideoRecorderUtils.ActiveStillFrameExporter != null)
                {
                    currentFrames = VideoRecorderUtils.ActiveStillFrameExporter.FrameCount;
                }

                if (currentFrames >= targetCount)
                {
                    Debug.LogError("[CameraPathCaptureRig.RunStep8FrameConfirmation] Step 8 complete. " +
                                   $"Frames written during second pass: {currentFrames - startCount}");
                    Debug.LogError("[RunStep8FrameConfirmation] m_FinalFrameFolderPath at end of Step 8 = " + (m_FinalFrameFolderPath ?? "NULL"));
                    break;
                }

                if (Time.realtimeSinceStartup - startTime > timeoutSeconds)
                {
                    timedOut = true;
                    Debug.LogError("[CameraPathCaptureRig.RunStep8FrameConfirmation] WARNING - Timeout waiting for second pass frames. Proceeding with finalization anyway.");
                    break;
                }

                yield return new WaitForSeconds(0.25f);
            }

            // Clear ownership of this coroutine now that the wait is finished
            m_Step8WaitCoroutine = null;

            // ====================================================================
            // CHANNEL B - STRICT CHRONOLOGICAL FINALIZATION
            // Runs only after HDD confirms frames (or after timeout).
            // ====================================================================
            if (timedOut)
            {
                Debug.LogError("[RunStep8FrameConfirmation] Channel B starting finalization after TIMEOUT.");
            }
            else
            {
                Debug.LogError("[RunStep8FrameConfirmation] Channel B starting finalization after successful frame confirmation.");
            }

            // 1. Cleanup temporary folders / visual indicators
            PerformStep9_FinalizeSecondPassCleanup();

            // 2. Halt audio thread + write .wav (Snapshot -> Synchronize -> WriteWav)
            PerformStep10_BlockFurtherPasses();

            // 2b. Tear down the temporary AudioFilterProxy now that the snapshot
            //     has been taken. The proxy is no longer needed and must not remain
            //     on the VR camera hierarchy.
            AudioListener activeListener = UnityEngine.Object.FindObjectOfType<AudioListener>();
            if (activeListener != null)
            {
                AudioFilterProxy proxy = activeListener.gameObject.GetComponent<AudioFilterProxy>();
                if (proxy != null)
                {
                    Destroy(proxy);
                    Debug.LogError($"[Proxy Bridge] Removed AudioFilterProxy from '{activeListener.gameObject.name}' after HiFi snapshot.");
                }
            }


            // 3. Close still-frame exporter and reset multi-pass flags
            Debug.LogError("[RunStep8FrameConfirmation] Calling StopVideoCapture(true) from Channel B.");
            VideoRecorderUtils.StopVideoCapture(true);

            // 4. Generate preview .mp4 via FFmpeg
            PerformStep11_GeneratePreviewVideo();

            // FIX 2: Widget reset removed from Channel B.
            // FollowingPath is now cleared on the main thread in StopRecordingPath
            // (second-pass branch). This prevents the visual endless loop and stops
            // SerializerNewUsdFrame from re-entering and spawning duplicate coroutines.
            // The visual snap-back (ResetToPathStart) can be re-added here later if needed.

            Debug.LogError("[RunStep8FrameConfirmation] Channel B finalization complete.");
        }




        // Update 2072 CameraPathCaptureRig.PerformStep9_FinalizeSecondPassCleanup
        private void PerformStep9_FinalizeSecondPassCleanup()
        {
            Debug.LogError("[CameraPathCaptureRig.PerformStep9_FinalizeSecondPassCleanup] ============================================");
            Debug.LogError("[CameraPathCaptureRig.PerformStep9_FinalizeSecondPassCleanup] Step 9 - Finalizing second pass and cleaning up.");
            Debug.LogError("[CameraPathCaptureRig.PerformStep9_FinalizeSecondPassCleanup] ============================================");

            // === DIAGNOSTICS: Folder state ===
            Debug.LogError("[PerformStep9] m_FinalFrameFolderPath value = " + (m_FinalFrameFolderPath ?? "NULL"));
            Debug.LogError("[PerformStep9] IsNullOrEmpty check = " + string.IsNullOrEmpty(m_FinalFrameFolderPath));

            bool folderExists = false;
            if (!string.IsNullOrEmpty(m_FinalFrameFolderPath))
            {
                folderExists = Directory.Exists(m_FinalFrameFolderPath);
            }
            Debug.LogError("[PerformStep9] Directory.Exists check = " + folderExists);

            // Turn off second-pass visual indicator
            if (m_Widget != null)
            {
                m_Widget.TintForSecondPass(false);
            }

            // === Delete first pass folder ===
            if (!string.IsNullOrEmpty(m_FirstPassFolderPath) && Directory.Exists(m_FirstPassFolderPath))
            {
                Debug.LogError("[PerformStep9] Attempting to delete first pass folder: " + m_FirstPassFolderPath);
                try
                {
                    Directory.Delete(m_FirstPassFolderPath, true);
                    Debug.LogError("[CameraPathCaptureRig.PerformStep9_FinalizeSecondPassCleanup] Deleted first pass folder successfully.");
                }
                catch (Exception e)
                {
                    Debug.LogError("[CameraPathCaptureRig.PerformStep9_FinalizeSecondPassCleanup] Failed to delete first pass folder.");
                    Debug.LogError("[PerformStep9] Exception message: " + e.Message);
                    Debug.LogError("[PerformStep9] Stack trace:\n" + e.ToString());
                }
            }
            else
            {
                Debug.LogError("[PerformStep9] First pass folder not found or already deleted. Path: " + (m_FirstPassFolderPath ?? "NULL"));
            }

            // NOTE: We intentionally do NOT clear m_FinalFrameFolderPath here.
            // It must remain valid for Step 11 (GeneratePreviewVideo).
            Debug.LogError("[PerformStep9] Step 9 cleanup complete. m_FinalFrameFolderPath preserved for Step 11.");
        }


        // Update 2040 CameraPathCaptureRig.PerformStep10_BlockFurtherPasses
        // Update 2040 CameraPathCaptureRig.PerformStep10_BlockFurtherPasses
        /// <summary>
        /// CameraPathCaptureRig.PerformStep10_BlockFurtherPasses
        /// ScriptName.FunctionName: CameraPathCaptureRig.PerformStep10_BlockFurtherPasses
        /// Halts active background timeline coroutines, thread-safely compiles your WAV data sheets,
        /// blocks subsequent camera translations, and flushes active two-pass runtime tracking states.
        /// </summary>


        private void PerformStep10_BlockFurtherPasses()
        {
            Debug.LogError("[CameraPathCaptureRig.PerformStep10_BlockFurtherPasses] Step 10 - Blocking further passes and resetting two-pass state.");

            // ====================================================================
            // 1. CRITICAL THREAD HALT CASCADE (ANTI-RACE GUARD) - Task 7
            // ====================================================================
            if (m_TwoPassDelayCoroutine != null)
            {
                Debug.LogError("[PerformStep10] Force-halting active TwoPassDelay coroutine loop via system override.");
                StopCoroutine(m_TwoPassDelayCoroutine);
                m_TwoPassDelayCoroutine = null;
            }
            if (m_Step3WaitCoroutine != null)
            {
                Debug.LogError("[PerformStep10] Force-halting pending Step3 Frame Confirmation coroutine loop via system override.");
                StopCoroutine(m_Step3WaitCoroutine);
                m_Step3WaitCoroutine = null;
            }
            if (m_Step8WaitCoroutine != null)
            {
                Debug.LogError("[PerformStep10] Force-halting pending Step8 Frame Confirmation coroutine loop via system override.");
                StopCoroutine(m_Step8WaitCoroutine);
                m_Step8WaitCoroutine = null;
            }

            // ====================================================================
            // 2. TWO-PASS PARALLEL HIGH-FIDELITY AUDIO SAVE HOOK - Task 8
            // ====================================================================
            if (AudioCaptureManager.m_Instance != null)
            {
                int finalTargetFramesCount = Mathf.RoundToInt(targetDurationSeconds * liveCaptureFPS);

                int actualFramesWrittenCount = VideoRecorderUtils.ActiveStillFrameExporter != null
                    ? VideoRecorderUtils.ActiveStillFrameExporter.FrameCount
                    : 0;

                Debug.LogError(
                    $"[PerformStep10] HiFi save params: targetFrames={finalTargetFramesCount} | " +
                    $"actualFrames={actualFramesWrittenCount} | liveCaptureFPS={liveCaptureFPS}");

                AudioCaptureManager.m_Instance.StopAndSaveHiFiAudioTrack(
                    finalTargetFramesCount,
                    actualFramesWrittenCount,
                    liveCaptureFPS);
            }

            // ====================================================================
            // 3. EXISTING SYSTEM VARIABLE CLEANUP & STATE RESET
            // ====================================================================
            if (m_Widget != null)
            {
                m_Widget.TintForSecondPass(false);
            }

            Debug.LogError("[PerformStep10] Resetting two-pass state variables...");

            m_FirstPassActualFrames = -1;
            m_FirstPassFolderPath = "";
            m_SecondPassStartFrameCount = 0;
            m_SecondPassSpeedMultiplier = 1.0f;

            // Phase 2/3: clear pad region and content frame count for the next recording.
            m_SecondPassPathContentComplete = false;
            m_SecondPassContentFrameCount = 0;

            // NOTE: Do NOT clear m_CurrentRecordingBasePath / m_CurrentFramesFolderPath here.
            // CancelledRecordingCleanup needs them and runs after this method on the abort path.

            Debug.LogError("[PerformStep10] State variables reset complete.");
            Debug.LogError("[PerformStep10] BlockFurtherPasses complete. System is reset for next recording.");
        }



        // Update 2073 CameraPathCaptureRig.PerformStep11_GeneratePreviewVideo
        // Updated to use the new explicit Live Recording fields.
        // This method only runs in the normal in-game two-pass path.
        private void PerformStep11_GeneratePreviewVideo()
        {
            Debug.LogError("[CameraPathCaptureRig.PerformStep11_GeneratePreviewVideo] Step 11 - Generating preview video.");

            // === DIAGNOSTICS: Color change ===
            Debug.LogError("[PerformStep11] About to call TintForVideoGeneration(true). m_Widget is " + (m_Widget != null ? "valid" : "NULL"));

            // Turn on video generation color (blue)
            if (m_Widget != null)
            {
                m_Widget.TintForVideoGeneration(true);
                Debug.LogError("[PerformStep11] TintForVideoGeneration(true) called. Blue border should now be active.");
            }
            else
            {
                Debug.LogError("[PerformStep11] WARNING - m_Widget is null. Cannot set blue color.");
            }

            // === DIAGNOSTICS: Folder check ===
            Debug.LogError("[PerformStep11] Checking m_FinalFrameFolderPath = " + (m_FinalFrameFolderPath ?? "NULL"));

            // Generate preview video
            if (!string.IsNullOrEmpty(m_FinalFrameFolderPath) && Directory.Exists(m_FinalFrameFolderPath))
            {
                Debug.LogError(
                    $"[PerformStep11] Folder is valid. Starting video generation (mux frames + wav if present). " +
                    $"liveOutputFramerate={liveOutputFramerate} | CRF={videoQualityCRF} | preset={videoQualityPreset}");

                // Use Live Recording fields (this method only runs in the normal in-game two-pass path)
                VideoRecorderUtils.GeneratePreviewVideoFromFrames(
                    m_FinalFrameFolderPath,
                    liveOutputFramerate,
                    videoQualityCRF,
                    videoQualityPreset
                );

                // Optional: Move video to parent Videos folder
                string framesFolderName = Path.GetFileName(m_FinalFrameFolderPath);
                string baseFileName = framesFolderName.Replace("_frames", "");
                string parentFolder = Path.GetDirectoryName(m_FinalFrameFolderPath);
                string generatedVideoPath = Path.Combine(parentFolder, baseFileName + "_preview.mp4");

                if (File.Exists(generatedVideoPath))
                {
                    string finalVideoPath = Path.Combine(parentFolder, baseFileName + "_preview.mp4");
                    if (!generatedVideoPath.Equals(finalVideoPath, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Move(generatedVideoPath, finalVideoPath);
                    }

                    Debug.LogError("[PerformStep11] Preview mp4 present after mux: " + finalVideoPath);
                }
                else
                {
                    Debug.LogError("[PerformStep11] Preview mp4 not found after GeneratePreviewVideoFromFrames: " + generatedVideoPath);
                }
            }
            else
            {
                Debug.LogError("[PerformStep11] Video generation SKIPPED - m_FinalFrameFolderPath was invalid or empty.");
            }

            // Turn off video generation color
            if (m_Widget != null)
            {
                m_Widget.TintForVideoGeneration(false);
                Debug.LogError("[PerformStep11] TintForVideoGeneration(false) called. Color reverted.");
            }
        }

        // =====================================================================
        // MAIN ENTRY POINTS
        // =====================================================================

        // Update 2029
        // Update 3001 CameraPathCaptureRig.RecordPath
        // Added logging for capture resolution override fields at the start of recording.
        // This makes it immediately visible in the console whether captureWidthOverride / captureHeightOverride
        // are active, what values are set, and whether the system will use them for still-frame capture.
        // This is useful for 4K testing and confirming the override is being read before capture begins.
        public void RecordPath()
        {
            Debug.LogError("[CameraPathCaptureRig.RecordPath] Entry - Evaluating recording mode.");

            // === CAPTURE RESOLUTION OVERRIDE LOGGING ===
            if (captureWidthOverride > 0 && captureHeightOverride > 0)
            {
                Debug.LogError($"[CameraPathCaptureRig] CAPTURE RESOLUTION OVERRIDE ACTIVE: {captureWidthOverride}x{captureHeightOverride}");
            }
            else if (App.UserConfig.Video.OfflineResolution > 0)
            {
                Debug.LogError($"[CameraPathCaptureRig] Using OfflineResolution from config: {App.UserConfig.Video.OfflineResolution}");
            }
            else
            {
                Debug.LogError($"[CameraPathCaptureRig] Using default Resolution from config: {App.UserConfig.Video.Resolution}");
            }
            // === END RESOLUTION OVERRIDE LOGGING ===

            bool useTwoPass = App.UserConfig.Video.ScaleSpeedToFrameCount && useFrameLimit && targetDurationSeconds > 0f;
            if (!useTwoPass)
            {
                Debug.LogError("[CameraPathCaptureRig.RecordPath] Two-pass disabled. Taking normal single-pass path.");
                PerformNormalSinglePassRecording();
                return;
            }
            if (m_FirstPassActualFrames > 0)
            {
                Debug.LogError("[CameraPathCaptureRig.RecordPath] Routing to Step 6 (Second Pass).");
                PerformStep6_StartSecondPassRecording();
                return;
            }
            Debug.LogError("[CameraPathCaptureRig.RecordPath] Starting linear sequence at Step 1.");
            PerformStep1_StartFirstPassRecording();
        }

        // Update 3002 CameraPathCaptureRig.PerformNormalSinglePassRecording
        // Added resolution source logging for consistency with RecordPath().
        // Ensures that even when two-pass mode is disabled, it is still obvious
        // which resolution (override, OfflineResolution, or default) will be used
        // during still-frame capture.
        private void PerformNormalSinglePassRecording()
        {
            Debug.LogError("[CameraPathCaptureRig.PerformNormalSinglePassRecording] Starting normal single-pass recording.");

            // === CAPTURE RESOLUTION SOURCE LOGGING ===
            if (captureWidthOverride > 0 && captureHeightOverride > 0)
            {
                Debug.LogError($"[CameraPathCaptureRig] CAPTURE RESOLUTION OVERRIDE ACTIVE: {captureWidthOverride}x{captureHeightOverride}");
            }
            else if (App.UserConfig.Video.OfflineResolution > 0)
            {
                Debug.LogError($"[CameraPathCaptureRig] Using OfflineResolution from config: {App.UserConfig.Video.OfflineResolution}");
            }
            else
            {
                Debug.LogError($"[CameraPathCaptureRig] Using default Resolution from config: {App.UserConfig.Video.Resolution}");
            }
            // === END RESOLUTION SOURCE LOGGING ===

            if (useFrameLimit && targetDurationSeconds > 0f)
            {
                int calculatedFrames = VideoRecorderUtils.GetTargetFrameCount(targetDurationSeconds);
                VideoRecorderUtils.SetTargetFrameCount(calculatedFrames);
            }
            else
            {
                VideoRecorderUtils.SetTargetFrameCount(-1);
            }

            m_Widget.ResetToPathStart();
            m_Widget.TintForRecording(true);
            UpdateCameraTransform(m_Widget.transform);
            WidgetManager.m_Instance.FollowingPath = true;
            SketchSurfacePanel.m_Instance.EnableSpecificTool(BaseTool.ToolType.CameraPathTool);
            App.Switchboard.TriggerCameraPathModeChanged(CameraPathTool.Mode.Recording);
            VideoRecorderUtils.StartVideoCapture(
                MultiCamTool.GetSaveName(MultiCamStyle.Video),
                m_Manager.GetComponent<VideoRecorder>(),
                m_VideoUsdSerializer);
        }

        public void StopRecordingPath(bool saveCapture)
        {
            Debug.LogError("[CameraPathCaptureRig.StopRecordingPath] Entry - saveCapture: "
                + saveCapture +
                " | FirstPassActualFrames: " + m_FirstPassActualFrames);

            bool useTwoPass = App.UserConfig.Video.ScaleSpeedToFrameCount
                && useFrameLimit
                && targetDurationSeconds > 0f;

            // Two-pass only: companion .openbrushaudio (classic single-pass must not write this).
            if (useTwoPass
                && m_FirstPassActualFrames == 0
                && saveCapture
                && !App.Config.OfflineRender
                && AudioCaptureManager.m_Instance != null)
            {
                if (AudioCaptureManager.m_Instance.m_OnlineAudioMode
                    == AudioCaptureManager.OnlineAudioPreviewMode.Standard_Hardware_Audio)
                {
                    string currentVideoPath = VideoRecorderUtils.UsdPath;
                    if (!string.IsNullOrEmpty(currentVideoPath) && VisualizerManager.m_Instance != null)
                    {
                        string directory = System.IO.Path.GetDirectoryName(currentVideoPath);
                        string filename = System.IO.Path.GetFileNameWithoutExtension(currentVideoPath);
                        string companionAudioPath = System.IO.Path.Combine(
                            directory, filename + ".openbrushaudio");
                        VisualizerManager.m_Instance.SaveBakedAudioCache(companionAudioPath);
                        Debug.LogError(
                            "[Live Audio Sync] Two-pass companion bake: " + companionAudioPath);
                    }
                }
            }

            void ExitRecordingToolMode()
            {
                WidgetManager.m_Instance.FollowingPath = false;
                if (m_Widget != null)
                {
                    m_Widget.ResetToPathStart();
                    m_Widget.TintForRecording(false);
                }
                SketchSurfacePanel.m_Instance.EnableSpecificTool(BaseTool.ToolType.CameraPathTool);
                App.Switchboard.TriggerCameraPathModeChanged(CameraPathTool.Mode.Idle);
            }

            if (!saveCapture)
            {
                Debug.LogError(
                    "[StopRecordingPath] USER ABORT SEQUENCE DETECTED. " +
                    "Cleaning active video capture and resetting parallel loops.");
                VideoRecorderUtils.StopVideoCapture(false);
                PerformStep10_BlockFurtherPasses();
                CancelledRecordingCleanup();
                m_FirstPassActualFrames = 0;
                ExitRecordingToolMode();
                string cancelMessage = m_PathCancelled.GetLocalizedStringAsync().Result;
                OutputWindowScript.m_Instance.CreateInfoCardAtController(
                    InputManager.ControllerName.Brush, cancelMessage);
                Debug.LogError("[StopRecordingPath] Exit - Function complete (user abort).");
                return;
            }

            if (!useTwoPass)
            {
                int frames = 0;
                string filePath = null;
                bool stillFallback = VideoRecorderUtils.IsUsingStillFrameFallback;

                if (VideoRecorderUtils.ActiveVideoRecording != null)
                {
                    frames = VideoRecorderUtils.ActiveVideoRecording.FrameCount;
                    filePath = VideoRecorderUtils.ActiveVideoRecording.FilePath;
                }
                else if (VideoRecorderUtils.ActiveStillFrameExporter != null)
                {
                    frames = VideoRecorderUtils.ActiveStillFrameExporter.FrameCount;
                    filePath = VideoRecorderUtils.ActiveStillFrameExporter.FilePath;
                }

                bool fileExists = !string.IsNullOrEmpty(filePath) && System.IO.File.Exists(filePath);
                long fileBytes = 0;
                if (fileExists)
                {
                    try { fileBytes = new System.IO.FileInfo(filePath).Length; }
                    catch { /* ignore */ }
                }

                Debug.LogError(
                    "[StopRecordingPath] Single-pass pre-stop: frames=" + frames +
                    " stillFallback=" + stillFallback +
                    " file=" + (filePath ?? "(null)") +
                    " exists=" + fileExists +
                    " bytes=" + fileBytes +
                    " activeVideo=" + (VideoRecorderUtils.ActiveVideoRecording != null) +
                    " activeStill=" + (VideoRecorderUtils.ActiveStillFrameExporter != null));

                bool captureLooksValid = frames > 0;

                VideoRecorderUtils.StopVideoCapture(captureLooksValid);
                m_FirstPassActualFrames = 0;
                ExitRecordingToolMode();

                if (captureLooksValid)
                {
                    string message = stillFallback
                        ? "Camera path exported as still frame sequence."
                        : m_PathRecorded.GetLocalizedStringAsync().Result;
                    OutputWindowScript.m_Instance.CreateInfoCardAtController(
                        InputManager.ControllerName.Brush, message);
                    if (filePath != null)
                    {
                        ControllerConsoleScript.m_Instance.AddNewLine(filePath);
                    }
                    Debug.LogError("[StopRecordingPath] Exit - Function complete (single-pass OK).");
                }
                else
                {
                    string failMessage = m_PathCancelled.GetLocalizedStringAsync().Result;
                    OutputWindowScript.m_Instance.CreateInfoCardAtController(
                        InputManager.ControllerName.Brush, failMessage);
                    Debug.LogError(
                        "[StopRecordingPath] Exit - single-pass FAILED: 0 video frames.");
                }
                return;
            }

            if (m_FirstPassActualFrames == 0 && saveCapture)
            {
                int framesWritten = 0;
                if (VideoRecorderUtils.ActiveStillFrameExporter != null)
                {
                    framesWritten = VideoRecorderUtils.ActiveStillFrameExporter.FrameCount;
                }
                else if (VideoRecorderUtils.ActiveVideoRecording != null)
                {
                    framesWritten = VideoRecorderUtils.ActiveVideoRecording.FrameCount;
                }

                if (framesWritten <= 0)
                {
                    Debug.LogError(
                        "[StopRecordingPath] FIRST PASS produced 0 frames after start. " +
                        "Treating as failed/cancelled recording to prevent infinite loop.");
                    VideoRecorderUtils.StopVideoCapture(false);
                    PerformStep10_BlockFurtherPasses();
                    CancelledRecordingCleanup();
                    m_FirstPassActualFrames = 0;
                    ExitRecordingToolMode();
                    string failMessage = m_PathCancelled.GetLocalizedStringAsync().Result;
                    OutputWindowScript.m_Instance.CreateInfoCardAtController(
                        InputManager.ControllerName.Brush, failMessage);
                    Debug.LogError("[StopRecordingPath] Exit - Function complete (0-frame guard).");
                    return;
                }

                Debug.LogError("[StopRecordingPath] Taking FIRST PASS completion path. framesWritten="
                    + framesWritten);
                PerformStep2_StopFirstPassAndCollectData();
                PerformStep3_EnsureFirstPassFramesAreWritten();
                PerformStep4_CalculateSecondPassSpeed();
                PerformStep5_ShowWaitForSecondPass();
                VideoRecorderUtils.StopVideoCapture(saveCapture);
                WidgetManager.m_Instance.FollowingPath = false;
                if (m_Widget != null)
                {
                    m_Widget.ResetToPathStart();
                    m_Widget.TintForRecording(false);
                }
                return;
            }

            if (m_FirstPassActualFrames > 0 && saveCapture)
            {
                Debug.LogError("[StopRecordingPath] Taking SECOND PASS completion path.");
                PerformStep7_StopSecondPass();
                WidgetManager.m_Instance.FollowingPath = false;
                PerformStep8_EnsureSecondPassFramesAreWritten();
            }

            string endMessage = saveCapture
                ? (VideoRecorderUtils.IsUsingStillFrameFallback
                    ? "Camera path exported as still frame sequence."
                    : m_PathRecorded.GetLocalizedStringAsync().Result)
                : m_PathCancelled.GetLocalizedStringAsync().Result;
            OutputWindowScript.m_Instance.CreateInfoCardAtController(
                InputManager.ControllerName.Brush, endMessage);

            if (saveCapture)
            {
                string filePath = null;
                if (VideoRecorderUtils.ActiveVideoRecording != null)
                {
                    filePath = VideoRecorderUtils.ActiveVideoRecording.FilePath;
                }
                else if (VideoRecorderUtils.ActiveStillFrameExporter != null)
                {
                    filePath = VideoRecorderUtils.ActiveStillFrameExporter.FilePath;
                }
                if (filePath != null)
                {
                    ControllerConsoleScript.m_Instance.AddNewLine(filePath);
                }
            }

            ExitRecordingToolMode();
            Debug.LogError("[StopRecordingPath] Exit - Function complete.");
        }



        private void CancelledRecordingCleanup()
        {
            // Never wipe files during offline / headless rendering
            if (App.Config != null && App.Config.OfflineRender)
            {
                Debug.LogError("[CancelledRecordingCleanup] Skipped — offline render mode.");
                ClearSessionPathState();
                return;
            }

            // Resolve any known session path so we can extract the Untitled_### prefix
            string anyPath = m_CurrentRecordingBasePath;
            if (string.IsNullOrEmpty(anyPath))
                anyPath = m_FirstPassFolderPath;
            if (string.IsNullOrEmpty(anyPath))
                anyPath = m_CurrentFramesFolderPath;
            if (string.IsNullOrEmpty(anyPath) && VideoRecorderUtils.ActiveStillFrameExporter != null)
                anyPath = VideoRecorderUtils.ActiveStillFrameExporter.FilePath;

            if (string.IsNullOrEmpty(anyPath))
            {
                Debug.LogError("[CancelledRecordingCleanup] No session path available; nothing to wipe.");
                ClearSessionPathState();
                return;
            }

            string videosDir = Path.GetDirectoryName(anyPath);
            string fileName = Path.GetFileNameWithoutExtension(anyPath);   // e.g. "Untitled_812_05"

            // Extract the sketch prefix: "Untitled_812"
            string prefix = fileName;
            int lastUnderscore = fileName.LastIndexOf('_');
            if (lastUnderscore > 0)
            {
                prefix = fileName.Substring(0, lastUnderscore);
            }

            if (string.IsNullOrEmpty(videosDir) || !Directory.Exists(videosDir) || string.IsNullOrEmpty(prefix))
            {
                Debug.LogError("[CancelledRecordingCleanup] Could not resolve Videos dir or prefix; nothing to wipe.");
                ClearSessionPathState();
                return;
            }

            Debug.LogError($"[CancelledRecordingCleanup] Wiping all artifacts starting with \"{prefix}\" in: {videosDir}");

            // Delete matching files
            try
            {
                foreach (string file in Directory.GetFiles(videosDir, prefix + "*", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        File.Delete(file);
                        Debug.LogError($"[CancelledRecordingCleanup] Deleted file: {file}");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[CancelledRecordingCleanup] Failed to delete file {file}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CancelledRecordingCleanup] Error enumerating files: {ex.Message}");
            }

            // Delete matching folders
            try
            {
                foreach (string dir in Directory.GetDirectories(videosDir, prefix + "*", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        Directory.Delete(dir, true);
                        Debug.LogError($"[CancelledRecordingCleanup] Deleted folder: {dir}");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[CancelledRecordingCleanup] Failed to delete folder {dir}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CancelledRecordingCleanup] Error enumerating folders: {ex.Message}");
            }

            ClearSessionPathState();
        }

        private void ClearSessionPathState()
        {
            m_CurrentRecordingBasePath = "";
            m_CurrentFramesFolderPath = "";
            m_FirstPassFolderPath = "";
            m_FinalFrameFolderPath = "";
            m_SecondPassStartFrameCount = 0;
        }


        // Update 2026
        public void ForceResetTwoPassState()
        {
            Debug.LogError("[CameraPathCaptureRig.ForceResetTwoPassState] ============================================");
            Debug.LogError("[ForceResetTwoPassState] FORCE RESET REQUESTED - This will clear all two-pass state.");
            Debug.LogError("[ForceResetTwoPassState] Delegating to PerformStep10_BlockFurtherPasses()...");
            Debug.LogError("[CameraPathCaptureRig.ForceResetTwoPassState] ============================================");

            PerformStep10_BlockFurtherPasses();
        }

        // =====================================================================
        // VISIBILITY / HELPERS
        // =====================================================================

        void RefreshVisibility()
        {
            if (m_Object != null)
            {
                var currentPath = WidgetManager.m_Instance.GetCurrentCameraPath();
                m_Object.SetActive(
                    currentPath != null &&
                    currentPath.m_WidgetObject.activeInHierarchy &&
                    WidgetManager.m_Instance.CameraPathsVisible
                );
            }
            m_Widget.Show(WidgetManager.m_Instance.CanRecordCurrentCameraPath());
        }

        void OnPoseChanged(TrTransform prev, TrTransform current)
        {
            m_CameraComponent.nearClipPlane = m_CameraClipPlanesBase.x * current.scale;
            m_CameraComponent.farClipPlane = m_CameraClipPlanesBase.y * current.scale;
        }

        void OnToolChanged()
        {
            if (SketchSurfacePanel.m_Instance.GetCurrentToolType() == BaseTool.ToolType.MultiCamTool)
            {
                WidgetManager.m_Instance.FollowingPath = false;
            }
            RefreshVisibility();
        }


        /// <summary>
        /// Flow:
        /// StartOfflineRender()
        ///     -> Offline05_EnsureUsdPathSerializer()          - Ensure UsdPathSerializer is available and initialized
        ///             -> Offline07_PrepareSerializerForPlayback()   - Reset time + Deserialize first frame
        ///                     -> Offline08_StartFirstPassCapture()      - Start capture + create output folder (self-contained)
        ///                             -> Offline10_StartFirstPass()             - Final setup + launch driver
        ///                                     -> Offline15_WaitForPathToEnd()             - Drive path + camera every frame until end
        ///                                     -> Offline20_CollectFirstPassFrameCount()   - Collect measured frame count
        ///                                     -> Offline25_StopFirstPassCapture()         - Stop first capture + wait for finalization + rename to _Calibration
        ///                                     -> Offline30_CalculateSecondPassTimeScale() - Calculate required time scale
        ///                                     -> Offline35_PrepareSecondPass()            - Prepare for clean output folder
        ///                                     -> Offline38_GatherRenderParameters()       - Centralize final render parameters
        ///                                     -> Offline40_StartSecondPassCapture()       - Start second pass capture into clean folder
        ///                                     -> Offline42_AdvanceScaledTime()            - Advance time with calculated scale
        ///                                     -> Offline45_WaitForSecondPassToEnd()       - Transition after second pass completes
        ///                                     -> Offline47_GenerateFinalVideo()           - Generate final .mp4 from second pass frames
        ///                                     -> Offline50_Cleanup()                      - Final reset of all offline state
        /// </summary>



        /// <summary>
        /// CameraPathCaptreRig.StartOfflineRender
        /// Entry point for headless two-pass offline rendering.
        /// Called from SketchControlsScript when App.Config.OfflineRender is true.
        /// </summary>
        /// 


        public void StartOfflineRender()
        {
            Debug.LogError("[CameraPathCaptureRig] Starting headless two-pass render.");

            if (App.Config != null)
            {
                Debug.LogError($"[StartOfflineRender] App.Config.OfflineRender={App.Config.OfflineRender}");
            }

            // =================================================================
            // MASTER HEADLESS AUDIO DROPDOWN INITIALIZATION HOOK
            // =================================================================
            if (AudioCaptureManager.m_Instance != null)
            {
                AudioCaptureManager.OfflineAudioRenderMode activeMode = AudioCaptureManager.m_Instance.m_OfflineAudioMode;
                if (activeMode == AudioCaptureManager.OfflineAudioRenderMode.Force_Simulated_BPM)
                {
                    // Force global shader variables and material parameters to wake up
                    Shader.EnableKeyword("AUDIO_REACTIVE");
                    if (App.Instance != null)
                    {
                        App.Instance.AudioReactiveBrushesActive(true);
                    }
                    // Force the simulation state to engage
                    AudioCaptureManager.m_Instance.EnableSimulatedBPM(AudioCaptureManager.m_Instance.m_SimulatedBPM);
                    Debug.LogError($"[Audio Pipeline] HEADLESS BOOT: Forcing Simulated BPM engine at: {AudioCaptureManager.m_Instance.m_SimulatedBPM} BPM.");
                }
                else if (activeMode == AudioCaptureManager.OfflineAudioRenderMode.Use_PreRecorded_Data)
                {
                    Shader.EnableKeyword("AUDIO_REACTIVE");
                    if (App.Instance != null)
                    {
                        App.Instance.AudioReactiveBrushesActive(true);
                    }
                    Debug.LogError("[Audio Pipeline] HEADLESS BOOT: Active mode set to pre-recorded data tracking.");
                }
                else
                {
                    // Strict baseline enforcement: Kill all material switches
                    Shader.DisableKeyword("AUDIO_REACTIVE");
                    if (App.Instance != null)
                    {
                        App.Instance.AudioReactiveBrushesActive(false);
                    }
                    Debug.LogError("[Audio Pipeline] HEADLESS BOOT: Audio reactivity explicitly disabled.");
                }
            }
            if (App.Config != null && App.Config.OfflineRender)
            {
                QualitySettings.vSyncCount = 0;
                Debug.LogError("[CameraPathCaptureRig] VSync disabled for accurate offline frame capture.");
            }
            // Route through the dedicated serializer initialization steps
            Offline05_EnsureUsdPathSerializer();
        }


        /// <summary>
        /// Offline05 - Ensures the UsdPathSerializer exists and is loaded from the command line path.
        ///
        /// This method is responsible ONLY for creation and loading.
        /// Preparation for playback (resetting time + Deserialize) is handled in Offline07.
        /// </summary>
        private void Offline05_EnsureUsdPathSerializer()
        {
            Debug.LogError("[CameraPathCaptureRig] ============================================");
            Debug.LogError("[CameraPathCaptureRig] Offline05: Ensuring USD Path Serializer is available.");
            Debug.LogError("[CameraPathCaptureRig] ============================================");

            string targetPath = App.Config.m_VideoPathToRender;
            Debug.LogError($"[Offline05] App.Config.m_VideoPathToRender = {(string.IsNullOrEmpty(targetPath) ? "NONE" : targetPath)}");

            // Check if a serializer already exists from another system
            m_OfflineUsdSerializer = VideoRecorderUtils.m_UsdPathSerializer;

            if (m_OfflineUsdSerializer != null)
            {
                Debug.LogError("[Offline05] Found existing UsdPathSerializer in VideoRecorderUtils. Reusing it.");
            }
            else if (!string.IsNullOrEmpty(targetPath))
            {
                Debug.LogError("[Offline05] No existing serializer found. Attempting to create and load from command line path...");

                if (!System.IO.File.Exists(targetPath))
                {
                    Debug.LogError($"[Offline05] ERROR: .usda file does not exist at path: {targetPath}");
                }
                else
                {
                    try
                    {
                        // Create as a child of this rig for proper ownership and lifetime
                        GameObject serializerGO = new GameObject("OfflineUsdPathSerializer");
                        serializerGO.transform.SetParent(this.transform);

                        m_OfflineUsdSerializer = serializerGO.AddComponent<UsdPathSerializer>();

                        Debug.LogError("[Offline05] Loading camera path from .usda file. This may take some time depending on path length...");

                        if (m_OfflineUsdSerializer.Load(targetPath))
                        {
                            VideoRecorderUtils.m_UsdPathSerializer = m_OfflineUsdSerializer;
                            m_OfflineUsdSerializer.StartPlayback();

                            Debug.LogError("[Offline05] SUCCESS: Created and loaded UsdPathSerializer from: " + targetPath);
                        }
                        else
                        {
                            Debug.LogError($"[Offline05] ERROR: UsdPathSerializer.Load() failed for file: {targetPath}");
                            Destroy(serializerGO);
                            m_OfflineUsdSerializer = null;
                        }
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError("[Offline05] EXCEPTION while creating/loading UsdPathSerializer:");
                        Debug.LogError("[Offline05] Message: " + e.Message);
                        Debug.LogError("[Offline05] StackTrace:\n" + e.StackTrace);
                        m_OfflineUsdSerializer = null;
                    }
                }
            }
            else
            {
                Debug.LogError("[Offline05] No path provided and no existing serializer found.");
            }

            // Proceed to capture component resolution
            Offline06_EnsureCaptureComponents();
        }

        /// <summary>
        /// Offline06 - Ensures we have valid VideoRecorder and ScreenshotManager references
        /// for offline capture. This step searches for existing, properly configured components
        /// in the scene (preferably ones that already have a Camera).
        ///
        /// This replaces the previous approach of dynamically adding components to the rig.
        /// </summary>

        /// <summary>
        /// Offline06 - Ensures we have valid VideoRecorder and ScreenshotManager references
        /// for offline capture. This version follows the same logic as Init() to find the
        /// correct ScreenshotManager used by normal still-frame recording.
        /// </summary>
        /// 

        private void Offline06_EnsureCaptureComponents()
        {
            Debug.LogError("[CameraPathCaptureRig] ============================================");
            Debug.LogError("[CameraPathCaptureRig] Offline06: Ensuring VideoRecorder and ScreenshotManager for offline capture.");
            Debug.LogError("[CameraPathCaptureRig] ============================================");

            ScreenshotManager chosenManager = null;

            if (m_Manager != null)
            {
                chosenManager = m_Manager;
                Debug.LogError("[Offline06] Using m_Manager (already set in Init()).");
            }
            else if (m_Camera != null)
            {
                chosenManager = m_Camera.GetComponentInChildren<ScreenshotManager>(true);
                if (chosenManager != null)
                {
                    Debug.LogError("[Offline06] Found ScreenshotManager under m_Camera hierarchy.");
                }
            }

            if (chosenManager == null)
            {
                Debug.LogError("[Offline06] ERROR: Could not find the correct ScreenshotManager using m_Manager or m_Camera.");
                Debug.LogError("[Offline06] Aborting offline render to avoid using the wrong camera.");
                m_OfflineScreenshotManager = null;
                m_OfflineVideoRecorder = null;
                return;
            }

            m_OfflineScreenshotManager = chosenManager;

            string fullPath = GetFullHierarchyPath(chosenManager.transform);
            Debug.LogError($"[Offline06] DIAGNOSTIC - Selected ScreenshotManager path: {fullPath}");
            Debug.LogError($"[Offline06] DIAGNOSTIC - GameObject name: {chosenManager.gameObject.name}");

            m_OfflineVideoRecorder = m_OfflineScreenshotManager.GetComponent<VideoRecorder>();
            if (m_OfflineVideoRecorder == null)
            {
                m_OfflineVideoRecorder = m_OfflineScreenshotManager.gameObject.AddComponent<VideoRecorder>();
                Debug.LogError("[Offline06] Added VideoRecorder to the selected ScreenshotManager.");
            }
            else
            {
                Debug.LogError("[Offline06] Using existing VideoRecorder on the selected ScreenshotManager.");
            }

            // === Explicit assignment to avoid Awake/GetComponent timing issues in headless mode ===
            if (m_OfflineVideoRecorder != null)
            {
                StillFrameSequenceExporter exporter = m_OfflineVideoRecorder.GetComponent<StillFrameSequenceExporter>();
                if (exporter != null)
                {
                    exporter.SetScreenshotManager(m_OfflineScreenshotManager);
                }
                else
                {
                    Debug.LogError("[Offline06] StillFrameSequenceExporter not present yet (will be created later). Assignment will happen in StartVideoCapture if needed.");
                }
            }

            Debug.LogError("[Offline06] Capture components successfully resolved.");
            Debug.LogError("[CameraPathCaptureRig] ============================================");

            Offline07_PrepareSerializerForPlayback();
        }



        /// <summary>
        /// Returns the full hierarchy path of a Transform (e.g. "CameraPathCaptureRig > Camera > VideoCamera").
        /// This version is iterative (no recursion) for safety and efficiency.
        /// </summary>
        private string GetFullHierarchyPath(Transform t)
        {
            if (t == null)
                return "NULL";

            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            while (t != null)
            {
                if (sb.Length > 0)
                    sb.Insert(0, " > ");

                sb.Insert(0, t.name);
                t = t.parent;
            }

            return sb.ToString();
        }


        /// <summary>
        /// Offline07 - Prepares the UsdPathSerializer for offline playback.
        ///
        /// This step resets the timeline and deserializes the first frame.
        /// It should only be called after Offline05 has successfully ensured a valid serializer.
        /// </summary>
        private void Offline07_PrepareSerializerForPlayback()
        {
            if (m_OfflineUsdSerializer != null)
            {
                Debug.LogError("[Offline07] Preparing UsdPathSerializer for playback...");

                m_OfflineUsdSerializer.Time = 0;
                m_OfflineUsdSerializer.Deserialize();

                Debug.LogError("[Offline07] Serializer reset to Time = 0 and deserialized. Ready for offline playback.");
            }
            else
            {
                Debug.LogError("[Offline07] WARNING: m_OfflineUsdSerializer is null after Offline05.");
                Debug.LogError("[Offline07] Camera path playback will not be available.");
            }

            Offline08_StartFirstPassCapture();
        }

        /// <summary>
        /// Offline08 - Dedicated step to start the first pass capture.
        /// 
        /// This step ensures a VideoRecorder component exists on the rig (self-contained flow),
        /// builds the output path using a descriptive name (resolution, FPS, duration + _OfflineCalibration),
        /// starts the still-frame capture, and creates the output folder
        /// before we begin driving the path.
        /// 
        /// This keeps capture startup completely separate from both setup (Offline10) and driving (Offline15).
        /// </summary>
        private void Offline08_StartFirstPassCapture()
        {
            Debug.LogError("[CameraPathCaptureRig] ============================================");
            Debug.LogError("[CameraPathCaptureRig] Offline08: Starting first pass capture.");
            Debug.LogError("[CameraPathCaptureRig] ============================================");

            if (m_OfflineUsdSerializer == null)
            {
                Debug.LogError("[Offline08] ERROR: m_OfflineUsdSerializer is null. Cannot start capture.");
                Debug.LogError("[Offline08] ABORTING offline render due to missing serializer.");
                return;
            }

            if (m_OfflineVideoRecorder == null)
            {
                Debug.LogError("[Offline08] ERROR: m_OfflineVideoRecorder is null. " +
                               "This should have been resolved in Offline06. Aborting first pass.");
                return;
            }

            VideoRecorderUtils.SetTargetFrameCount(-1);

            // =================================================================
            // HEADLESS VISUALIZER SYNC: SET BAKE MODE (PASS 1)
            // =================================================================
            VideoRecorderUtils.SetVisualizerPlaybackPass(false);

            // === Build descriptive output path (matching design spec) ===
            string sourcePath = App.Config.m_VideoPathToRender;
            string baseName = "Untitled_OfflineFirstPass";

            Debug.LogError($"[Offline08] Input .usda path from config: {(string.IsNullOrEmpty(sourcePath) ? "NONE" : sourcePath)}");

            if (!string.IsNullOrEmpty(sourcePath))
            {
                string originalName = Path.GetFileNameWithoutExtension(sourcePath);

                // Build descriptive name using parameters available at this stage
                string resolutionPart = (captureWidthOverride > 0 && captureHeightOverride > 0)
                    ? $"{captureWidthOverride}x{captureHeightOverride}"
                    : (App.UserConfig.Video.OfflineResolution > 0
                        ? $"{App.UserConfig.Video.OfflineResolution}x0"
                        : "default");

                float targetFps = (offlineCaptureFPS > 0) ? offlineCaptureFPS : App.UserConfig.Video.OfflineFPS;
                string fpsPart = $"{targetFps}FPS";
                string durationPart = $"{targetDurationSeconds:F0}s";

                baseName = $"{originalName}_{resolutionPart}_{fpsPart}_{durationPart}_OfflineCalibration";
            }

            Debug.LogError($"[Offline08] Requested first pass folder name: {baseName}");

            string videosRoot = Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments),
                "Open Brush", "Videos");

            string firstPassFullPath = Path.Combine(videosRoot, baseName);
            Debug.LogError($"[Offline08] First pass full output path: {firstPassFullPath}");

            // === Start capture ===
            bool started = VideoRecorderUtils.StartVideoCapture(
                firstPassFullPath,
                m_OfflineVideoRecorder,
                m_OfflineUsdSerializer,
                true);

            if (!started)
            {
                Debug.LogError("[Offline08] ============================================");
                Debug.LogError("[Offline08] ERROR: VideoRecorderUtils.StartVideoCapture() returned false.");
                Debug.LogError($"[Offline08] Path was: {firstPassFullPath}");
                Debug.LogError("[Offline08] First pass capture FAILED to start. ABORTING.");
                Debug.LogError("[Offline08] ============================================");
                return;
            }

            if (VideoRecorderUtils.ActiveStillFrameExporter == null)
            {
                Debug.LogError("[Offline08] ============================================");
                Debug.LogError("[Offline08] ERROR: ActiveStillFrameExporter is null after StartVideoCapture.");
                Debug.LogError("[Offline08] ABORTING offline render.");
                Debug.LogError("[Offline08] ============================================");
                return;
            }

            // === CRITICAL: Explicitly assign ScreenshotManager now that the exporter exists ===
            StillFrameSequenceExporter exporter = m_OfflineVideoRecorder.GetComponent<StillFrameSequenceExporter>();
            if (exporter != null && m_OfflineScreenshotManager != null)
            {
                exporter.SetScreenshotManager(m_OfflineScreenshotManager);
                Debug.LogError("[Offline08] Explicitly assigned ScreenshotManager to exporter after creation.");
            }
            else if (exporter == null)
            {
                Debug.LogError("[Offline08] WARNING: Could not find StillFrameSequenceExporter on m_OfflineVideoRecorder after StartVideoCapture.");
            }

            // Re-synchronize serializer (defensive)
            if (VideoRecorderUtils.m_UsdPathSerializer != null)
            {
                m_OfflineUsdSerializer = VideoRecorderUtils.m_UsdPathSerializer;
            }

            m_OfflineFirstPassFolderPath = VideoRecorderUtils.ActiveStillFrameExporter.FilePath;

            Debug.LogError($"[Offline08] First pass capture started successfully.");
            Debug.LogError($"[Offline08] Output folder: {m_OfflineFirstPassFolderPath}");
            Debug.LogError($"[Offline08] Serializer re-synchronized. IsNull = {m_OfflineUsdSerializer == null}");
            Debug.LogError("[Offline08] Capture initialized successfully. Handing off to Offline10.");
            Debug.LogError("[CameraPathCaptureRig] ============================================");

            Offline10_StartFirstPass();
        }



        /// <summary>
        /// OFFLINE RENDER PARAMETER PRIORITY ORDER (as of July 2026)
        ///
        /// 1. Fake Command Line (Highest Priority)
        /// Values explicitly passed via --CameraPathCaptureRig.fieldName take precedence.
        /// Example: --CameraPathCaptureRig.targetDurationSeconds 12
        ///
        /// 2. Inspector Values (CameraPathCaptureRig component)
        /// Values set directly in the Unity Inspector on this rig.
        ///
        /// 3. Internal Fallback Logic (Lowest Priority / Safety Net)
        /// Used only when no value is provided via command line or Inspector.
        ///
        /// CURRENT STATUS:
        /// - offlineCaptureFPS -> Has explicit fallback to Video.OfflineFPS (Offline30 / Offline40)
        /// - targetDurationSeconds -> Has fallback + ALERT log in Offline10 (defaults to 8 seconds)
        /// - offlineOutputFPS -> Has fallback in Offline47 (defaults to 30 fps)
        /// - offlineVideoQualityCRF -> Has fallback in Offline47 (defaults to 18)
        /// - offlineVideoQualityPreset -> Has fallback in Offline47 (defaults to "veryslow")
        /// - captureWidth/HeightOverride -> Has fallback via Video.OfflineResolution in Offline38
        ///
        /// NOT YET IMPLEMENTED (for future consideration):
        /// - Explicit command-line override detection for targetDurationSeconds, offlineOutputFPS,
        /// offlineVideoQualityCRF, and offlineVideoQualityPreset.
        /// Currently relies on Unity's field assignment + our fallback safety nets.
        /// A more robust system would track whether a value came from command line vs Inspector.
        ///
        /// This structure ensures we always have valid values and prevents silent failures during testing.
        /// </summary>
        /// 



        /// <summary>
        /// Offline10 - Final first-pass setup and driver launch.
        /// 
        /// Responsibilities:
        /// - Perform final safety checks and diagnostics
        /// - Capture original file path for diagnostics/restart
        /// - Launch the first pass driver (Offline15)
        /// 
        /// Note: Capture startup (VideoRecorder + folder creation) now lives in Offline08.
        /// This method no longer acquires VideoRecorder or starts capture.
        /// </summary>
        private void Offline10_StartFirstPass()
        {
            Debug.LogError("[CameraPathCaptureRig] ============================================");
            Debug.LogError("[CameraPathCaptureRig] Offline10: Final first-pass setup and driver launch.");
            Debug.LogError("[CameraPathCaptureRig] ============================================");

            // === Serializer Safety Check ===
            if (m_OfflineUsdSerializer == null)
            {
                Debug.LogError("[Offline10] ============================================");
                Debug.LogError("[Offline10] ERROR: m_OfflineUsdSerializer is null after Offline07/Offline08.");
                Debug.LogError("[Offline10] Cannot start first pass. Camera path playback is unavailable.");
                Debug.LogError("[Offline10] ============================================");
                return;
            }

            // Store original file path for diagnostics and potential second pass restart
            if (VideoRecorderUtils.ActiveStillFrameExporter != null)
            {
                m_OfflineOriginalFilePath = VideoRecorderUtils.ActiveStillFrameExporter.FilePath;
                Debug.LogError($"[Offline10] Original file path captured: {m_OfflineOriginalFilePath}");
            }

            // === Resolution diagnostics ===
            if (captureWidthOverride > 0 && captureHeightOverride > 0)
            {
                Debug.LogError($"[Offline10] Using capture override resolution: {captureWidthOverride}x{captureHeightOverride}");
            }
            else if (App.UserConfig.Video.OfflineResolution > 0)
            {
                Debug.LogError($"[Offline10] Using OfflineResolution from config: {App.UserConfig.Video.OfflineResolution}");
            }
            else
            {
                Debug.LogError($"[Offline10] Using default Resolution: {App.UserConfig.Video.Resolution}");
            }

            // === Safety fallback for targetDurationSeconds ===
            if (targetDurationSeconds <= 0f)
            {
                Debug.LogError("[CameraPathCaptureRig] ============================================");
                Debug.LogError("[Offline10] ALERT: No Target Duration set, falling back to 8 seconds");
                Debug.LogError("[Offline10] Please set 'Target Duration Seconds' in the Inspector for future renders.");
                Debug.LogError("[CameraPathCaptureRig] ============================================");
                targetDurationSeconds = 8.0f;
            }

            // === Confirmation and handoff ===
            Debug.LogError("[CameraPathCaptureRig] ============================================");
            Debug.LogError("[CameraPathCaptureRig] Offline10: Setup complete. Launching first pass driver (Offline15)...");
            Debug.LogError("[CameraPathCaptureRig] ============================================");

            if (m_OfflineCompletionCheckCoroutine != null)
                StopCoroutine(m_OfflineCompletionCheckCoroutine);

            m_OfflineCompletionCheckCoroutine = StartCoroutine(Offline15_WaitForPathToEnd());
        }


        /// <summary>
        /// Offline 15 - Dedicated first pass driver + waiter.
        /// Advances the UsdPathSerializer in fixed path-time steps (1 / offlineFPS),
        /// applies its transform to the camera, captures a calibration still each step,
        /// and waits until the path naturally reaches the end or a safety timeout occurs.
        ///
        /// Fixed steps avoid hitch-sized Time.deltaTime jumps (common at 4K) that under-sample
        /// the path and poison Offline30 time scale.
        ///
        /// Includes defensive re-acquisition of the serializer in case it was overwritten
        /// during capture startup.
        /// </summary>
        private System.Collections.IEnumerator Offline15_WaitForPathToEnd()
        {
            Debug.LogError("[CameraPathCaptureRig] Offline15: Starting first pass path playback and camera drive...");

            const float safetyTimeoutSeconds = 3600f;
            float startTime = Time.realtimeSinceStartup;
            bool firstFrameCaptured = false;

            // Tracker to prevent logging the exact same frame count multiple times
            int lastLoggedFrameCount = -1;

            // === Fixed path-time step (calibration density independent of capture hitch size) ===
            // Same FPS priority as Offline30: rig offlineCaptureFPS, else UserConfig.Video.OfflineFPS.
            float targetFps;
            if (offlineCaptureFPS > 0)
            {
                targetFps = offlineCaptureFPS;
            }
            else
            {
                targetFps = App.UserConfig.Video.OfflineFPS;
            }
            if (targetFps <= 0f)
            {
                targetFps = 60f;
                Debug.LogError("[Offline15] WARNING: Invalid offline FPS. Using fallback 60 for path steps.");
            }
            double pathStepSeconds = 1.0 / targetFps;
            Debug.LogError($"[Offline15] Fixed path step = {pathStepSeconds:F6}s ({targetFps} FPS). " +
                           "Path advances by this amount each calibration sample (not by Time.deltaTime).");

            // TEMP DIAGNOSTIC — REMOVE AFTER INVESTIGATION (double-to-float cast trace)
            int castDiagCallCount = 0;

            while (true)
            {
                // Defensive re-acquisition of serializer
                if (m_OfflineUsdSerializer == null && VideoRecorderUtils.m_UsdPathSerializer != null)
                {
                    m_OfflineUsdSerializer = VideoRecorderUtils.m_UsdPathSerializer;
                    Debug.LogError("[Offline15] WARNING: Serializer was null. Re-acquired from VideoRecorderUtils.");
                }

                if (m_OfflineUsdSerializer == null)
                {
                    Debug.LogError("[Offline15] ERROR: m_OfflineUsdSerializer is null during first pass. Aborting.");
                    Offline20_CollectFirstPassFrameCount();
                    yield break;
                }

                // Drive path and camera — fixed path dt (not wall-clock deltaTime)
                m_OfflineUsdSerializer.Time += pathStepSeconds;
                m_OfflineUsdSerializer.Deserialize();
                UpdateCameraTransform(m_OfflineUsdSerializer.transform);

                // === Capture one calibration sample at this path time ===
                if (VideoRecorderUtils.ActiveStillFrameExporter != null)
                {
                    // Lightweight safety check only (do not add components)
                    if (m_OfflineVideoRecorder != null &&
                        m_OfflineVideoRecorder.GetComponent<ScreenshotManager>() == null)
                    {
                        Debug.LogError("[Offline15] WARNING: ScreenshotManager is missing on m_OfflineVideoRecorder during first pass.");
                    }

                    // TEMP DIAGNOSTIC — REMOVE AFTER INVESTIGATION
                    if (castDiagCallCount < 10)
                    {
                        castDiagCallCount++;
                        double rawDouble = m_OfflineUsdSerializer.Time;
                        float castFloat = (float)rawDouble;
                        double roundTripLoss = (double)castFloat - rawDouble;
                        Debug.LogError(
                            $"[CastDiag] call={castDiagCallCount} " +
                            $"Time(double)={rawDouble:F10} " +
                            $"pathStepSeconds(double)={pathStepSeconds:F10} " +
                            $"(float)Time={castFloat:F10} " +
                            $"roundTripLoss=(float)Time-double={roundTripLoss:F10}");
                    }
                    // END TEMP DIAGNOSTIC

                    var calibrationSw = System.Diagnostics.Stopwatch.StartNew();
                    VideoRecorderUtils.ActiveStillFrameExporter.CaptureFrame((float)m_OfflineUsdSerializer.Time);
                    calibrationSw.Stop();
                    long calibrationCaptureTimeMs = calibrationSw.ElapsedMilliseconds;

                    int currentFrameCount = VideoRecorderUtils.ActiveStillFrameExporter.FrameCount;

                    // === CLEAN DIAGNOSTIC THROTTLE ===
                    // Instead of spamming every second, only log once every 60 successfully written frames
                    if (currentFrameCount % 60 == 0 && currentFrameCount != lastLoggedFrameCount)
                    {
                        Debug.LogError($"[Offline15] Progress: Frame {currentFrameCount} | " +
                                       $"Path Time: {m_OfflineUsdSerializer.Time:F3}s / {m_OfflineUsdSerializer.Duration:F2}s | " +
                                       $"Finished: {m_OfflineUsdSerializer.IsFinished}");

                        // Structured, greppable per-frame timing sample (comma-separated, not prose)
                        // so a run can be filtered to just these lines and graphed later.
                        Debug.LogError(
                            $"[OfflineFrameTiming] phase=Calibration,frame={currentFrameCount}," +
                            $"res={VideoRecorderUtils.ActiveStillFrameExporter.TargetWidth}x{VideoRecorderUtils.ActiveStillFrameExporter.TargetHeight}," +
                            $"captureTimeMs={calibrationCaptureTimeMs}");

                        lastLoggedFrameCount = currentFrameCount;
                    }

                    // Log first successful frame
                    if (!firstFrameCaptured && currentFrameCount > 0)
                    {
                        Debug.LogError("[Offline15] First frame successfully captured.");
                        firstFrameCaptured = true;
                        lastLoggedFrameCount = currentFrameCount;
                    }
                }

                if (m_OfflineUsdSerializer.IsFinished)
                {
                    int finalFrames = VideoRecorderUtils.ActiveStillFrameExporter != null
                        ? VideoRecorderUtils.ActiveStillFrameExporter.FrameCount
                        : 0;

                    // === DEFINITIVE FIRST PASS FINAL STATE LOG ===
                    Debug.LogError("[Offline15] ============================================");
                    Debug.LogError($"[Offline15] TERMINATION SUMMARY (IsFinished) | " +
                                   $"Total Calibration Frames: {finalFrames} | " +
                                   $"Final Path Time: {m_OfflineUsdSerializer.Time:F4}s | " +
                                   $"Final Pos: {m_OfflineUsdSerializer.transform.position}");
                    Debug.LogError("[Offline15] ============================================");

                    Debug.LogError("[CameraPathCaptureRig] Offline15: First pass path reached end (IsFinished). Handing off to Offline20.");
                    Offline20_CollectFirstPassFrameCount();
                    yield break;
                }

                if (Time.realtimeSinceStartup - startTime > safetyTimeoutSeconds)
                {
                    int finalFrames = VideoRecorderUtils.ActiveStillFrameExporter != null
                        ? VideoRecorderUtils.ActiveStillFrameExporter.FrameCount
                        : 0;

                    // === DEFINITIVE FIRST PASS TIMEOUT LOG ===
                    Debug.LogError("[Offline15] ============================================");
                    Debug.LogError($"[Offline15] TERMINATION SUMMARY (TIMEOUT) | " +
                                   $"Total Calibration Frames: {finalFrames} | " +
                                   $"Final Path Time: {m_OfflineUsdSerializer.Time:F4}s");
                    Debug.LogError("[Offline15] ============================================");

                    Debug.LogError("[CameraPathCaptureRig] Offline15: WARNING - Safety timeout reached.");
                    Offline20_CollectFirstPassFrameCount();
                    yield break;
                }

                yield return null;
            }
        }





        /// <summary>
        /// Offline 15 - Dedicated first pass driver + waiter.
        /// Advances the UsdPathSerializer every frame, applies its transform to the camera,
        /// and waits until the path naturally reaches the end or a safety timeout occurs.
        /// 
        /// Includes defensive re-acquisition of the serializer in case it was overwritten
        /// during capture startup.
        /// </summary>
        /// <summary>
        /// Offline 15 - Dedicated first pass driver + waiter.
        /// Advances the UsdPathSerializer every frame, applies its transform to the camera,
        /// and waits until the path naturally reaches the end or a safety timeout occurs.
        /// 
        /// Includes defensive re-acquisition of the serializer in case it was overwritten
        /// during capture startup.
        /// </summary>

        /*
        private System.Collections.IEnumerator Offline15_WaitForPathToEnd()
        {
            Debug.LogError("[CameraPathCaptureRig] Offline15: Starting first pass path playback and camera drive...");

            const float safetyTimeoutSeconds = 3600f;
            float startTime = Time.realtimeSinceStartup;
            bool firstFrameCaptured = false;

            // Tracker to prevent logging the exact same frame count multiple times
            int lastLoggedFrameCount = -1;

            while (true)
            {
                // Defensive re-acquisition of serializer
                if (m_OfflineUsdSerializer == null && VideoRecorderUtils.m_UsdPathSerializer != null)
                {
                    m_OfflineUsdSerializer = VideoRecorderUtils.m_UsdPathSerializer;
                    Debug.LogError("[Offline15] WARNING: Serializer was null. Re-acquired from VideoRecorderUtils.");
                }

                if (m_OfflineUsdSerializer == null)
                {
                    Debug.LogError("[Offline15] ERROR: m_OfflineUsdSerializer is null during first pass. Aborting.");
                    Offline20_CollectFirstPassFrameCount();
                    yield break;
                }

                // Drive path and camera
                m_OfflineUsdSerializer.Time += Time.deltaTime;
                m_OfflineUsdSerializer.Deserialize();
                UpdateCameraTransform(m_OfflineUsdSerializer.transform);

                // === Capture frame every frame ===
                if (VideoRecorderUtils.ActiveStillFrameExporter != null)
                {
                    // Lightweight safety check only (do not add components)
                    if (m_OfflineVideoRecorder != null &&
                        m_OfflineVideoRecorder.GetComponent<ScreenshotManager>() == null)
                    {
                        Debug.LogError("[Offline15] WARNING: ScreenshotManager is missing on m_OfflineVideoRecorder during first pass.");
                    }

                    VideoRecorderUtils.ActiveStillFrameExporter.CaptureFrame((float)m_OfflineUsdSerializer.Time);

                    int currentFrameCount = VideoRecorderUtils.ActiveStillFrameExporter.FrameCount;

                    // === CLEAN DIAGNOSTIC THROTTLE ===
                    // Instead of spamming every second, only log once every 60 successfully written frames
                    if (currentFrameCount % 60 == 0 && currentFrameCount != lastLoggedFrameCount)
                    {
                        Debug.LogError($"[Offline15] Progress: Frame {currentFrameCount} | " +
                                       $"Path Time: {m_OfflineUsdSerializer.Time:F3}s / {m_OfflineUsdSerializer.Duration:F2}s | " +
                                       $"Finished: {m_OfflineUsdSerializer.IsFinished}");
                        lastLoggedFrameCount = currentFrameCount;
                    }

                    // Log first successful frame
                    if (!firstFrameCaptured && currentFrameCount > 0)
                    {
                        Debug.LogError("[Offline15] First frame successfully captured.");
                        firstFrameCaptured = true;
                        lastLoggedFrameCount = currentFrameCount;
                    }
                }

                if (m_OfflineUsdSerializer.IsFinished)
                {
                    int finalFrames = VideoRecorderUtils.ActiveStillFrameExporter != null ? VideoRecorderUtils.ActiveStillFrameExporter.FrameCount : 0;

                    // === DEFINITIVE FIRST PASS FINAL STATE LOG ===
                    Debug.LogError("[Offline15] ============================================");
                    Debug.LogError($"[Offline15] TERMINATION SUMMARY (IsFinished) | " +
                                   $"Total Calibration Frames: {finalFrames} | " +
                                   $"Final Path Time: {m_OfflineUsdSerializer.Time:F4}s | " +
                                   $"Final Pos: {m_OfflineUsdSerializer.transform.position}");
                    Debug.LogError("[Offline15] ============================================");

                    Debug.LogError("[CameraPathCaptureRig] Offline15: First pass path reached end (IsFinished). Handing off to Offline20.");
                    Offline20_CollectFirstPassFrameCount();
                    yield break;
                }

                if (Time.realtimeSinceStartup - startTime > safetyTimeoutSeconds)
                {
                    int finalFrames = VideoRecorderUtils.ActiveStillFrameExporter != null ? VideoRecorderUtils.ActiveStillFrameExporter.FrameCount : 0;

                    // === DEFINITIVE FIRST PASS TIMEOUT LOG ===
                    Debug.LogError("[Offline15] ============================================");
                    Debug.LogError($"[Offline15] TERMINATION SUMMARY (TIMEOUT) | " +
                                   $"Total Calibration Frames: {finalFrames} | " +
                                   $"Final Path Time: {m_OfflineUsdSerializer.Time:F4}s");
                    Debug.LogError("[Offline15] ============================================");

                    Debug.LogError("[CameraPathCaptureRig] Offline15: WARNING - Safety timeout reached.");
                    Offline20_CollectFirstPassFrameCount();
                    yield break;
                }

                yield return null;
            }
        }
        */


        /// <summary>
        /// Offline 20 - Collect the actual number of frames produced during the first pass.
        /// </summary>
        private void Offline20_CollectFirstPassFrameCount()
        {
            Debug.LogError("[CameraPathCaptureRig] Offline20: Collecting first pass frame count.");

            if (VideoRecorderUtils.ActiveStillFrameExporter != null)
            {
                m_OfflineFirstPassFrameCount = VideoRecorderUtils.ActiveStillFrameExporter.FrameCount;
                m_OfflineFirstPassFolderPath = VideoRecorderUtils.ActiveStillFrameExporter.FilePath;

                Debug.LogError($"[Offline20] First pass complete → Frames: {m_OfflineFirstPassFrameCount} | Folder: {m_OfflineFirstPassFolderPath}");

                if (m_OfflineFirstPassFrameCount == 0)
                {
                    Debug.LogError("[Offline20] WARNING: First pass frame count is 0. This is unexpected and may indicate a capture failure.");
                }
                else if (m_OfflineFirstPassFrameCount < 10)
                {
                    Debug.LogError($"[Offline20] WARNING: First pass frame count is very low ({m_OfflineFirstPassFrameCount}). Path may be extremely short or capture may have failed.");
                }
            }
            else
            {
                Debug.LogError("[Offline20] ERROR: ActiveStillFrameExporter is null. Cannot collect frame count.");
                m_OfflineFirstPassFrameCount = 0;
            }

            // Start dedicated stop + finalization entry
            StartCoroutine(Offline25_StopFirstPassCapture());
        }

        /// <summary>
        /// Offline 25 - Dedicated entry to stop the first pass capture cleanly.
        /// Stops the capture and polls until it is fully finalized before proceeding.
        /// 
        /// Note: Folder is now created with the correct descriptive name (_OfflineCalibration) in Offline08,
        /// so no rename step is performed here anymore.
        /// Offline 25 - Stop first pass capture cleanly and store the end position
        /// for comparison against the second pass starting position.
        /// </summary>

        private System.Collections.IEnumerator Offline25_StopFirstPassCapture()
        {
            Debug.LogError("[CameraPathCaptureRig] Offline25: Stopping first pass capture.");

            if (VideoRecorderUtils.ActiveStillFrameExporter == null)
            {
                Debug.LogError("[Offline25] WARNING: No active capture to stop.");
                Offline30_CalculateSecondPassTimeScale();
                yield break;
            }

            VideoRecorderUtils.StopVideoCapture(true);
            Debug.LogError("[Offline25] StopVideoCapture(true) called. Polling for full finalization...");

            const float safetyTimeoutSeconds = 30f;
            float startTime = Time.realtimeSinceStartup;

            while (VideoRecorderUtils.ActiveStillFrameExporter != null)
            {
                if (Time.realtimeSinceStartup - startTime > safetyTimeoutSeconds)
                {
                    Debug.LogError($"[Offline25] WARNING: Timed out waiting for finalization after {safetyTimeoutSeconds} seconds. Proceeding anyway.");
                    break;
                }
                yield return new WaitForSeconds(0.1f);
            }

            // === First pass is now finalized ===
            if (VideoRecorderUtils.ActiveStillFrameExporter == null)
            {
                float elapsed = Time.realtimeSinceStartup - startTime;
                Debug.LogError($"[Offline25] SUCCESS: First pass capture finalized in {elapsed:F2} seconds.");

                // === NEW: Store end position of first pass for second-pass diagnostics ===
                if (m_OfflineUsdSerializer != null)
                {
                    m_FirstPassEndPosition = m_OfflineUsdSerializer.transform.position;
                    m_FirstPassEndRotation = m_OfflineUsdSerializer.transform.rotation;

                    Debug.LogError("[Offline25] ============================================");
                    Debug.LogError($"[Offline25] First pass END position: {m_FirstPassEndPosition}");
                    Debug.LogError($"[Offline25] First pass END rotation: {m_FirstPassEndRotation.eulerAngles}");
                    Debug.LogError("[Offline25] ============================================");
                }
            }
            else
            {
                Debug.LogError("[Offline25] FAILURE: First pass capture did NOT fully finalize.");
            }

            // Continue to time scale calculation
            Offline30_CalculateSecondPassTimeScale();
        }


        /// <summary>
        /// Offline 30 - Calculate the time scale needed for the second pass in headless offline mode.
        ///
        /// FPS Source Priority (explicit override behavior):
        ///
        /// 1. If offlineCaptureFPS > 0 on this rig, it is used as an explicit override.
        ///    This is the intended path for advanced users controlling timing from the Inspector
        ///    or via the fake command line in the editor.
        ///
        /// 2. If offlineCaptureFPS is 0, the system uses App.UserConfig.Video.OfflineFPS instead.
        ///    This value can come from:
        ///    - The real command line (--Video.OfflineFPS)
        ///    - The fake command line in the editor (m_FakeCommandLineArgsInEditor)
        ///    - The UserConfig file
        ///
        /// This design allows advanced users to take full control via the rig while preserving
        /// compatibility with normal Open Brush offline rendering workflows.
        /// </summary>
        private void Offline30_CalculateSecondPassTimeScale()
        {
            Debug.LogError("[CameraPathCaptureRig] Offline30: Calculating second pass time scale.");

            if (m_OfflineUsdSerializer == null)
            {
                Debug.LogError("[Offline30] ERROR: m_OfflineUsdSerializer is null.");
                m_OfflineTimeScale = 1.0f;
                Offline35_PrepareSecondPass();
                return;
            }

            float duration = (float)m_OfflineUsdSerializer.Duration;

            // === OFFLINE CAPTURE FPS - EXPLICIT OVERRIDE LOGIC ===
            float targetFps;

            if (offlineCaptureFPS > 0)
            {
                // Rig value is set — use it as an explicit override (advanced control)
                targetFps = offlineCaptureFPS;
                Debug.LogError($"[Offline30] Using explicit override from rig: offlineCaptureFPS = {targetFps}");
            }
            else
            {
                // Rig value is 0 — use value from UserConfig (command line or config file)
                targetFps = App.UserConfig.Video.OfflineFPS;
                Debug.LogError($"[Offline30] offlineCaptureFPS is 0. Using UserConfig.Video.OfflineFPS = {targetFps}");
            }

            float idealFrames = duration * targetFps;
            float traversalPercent = secondPassTraversalPercent / 100f;

            if (traversalPercent <= 0f) traversalPercent = 1.0f;

            // === SAFETY: refuse sparse calibration (does not create second-pass folder) ===
            // Fixed-step Offline15 should land near idealFrames. If samples are still far below,
            // do not compute a poisoned scale or start second pass.
            const float minSampleRatio = 0.5f;
            if (idealFrames > 0 && m_OfflineFirstPassFrameCount < idealFrames * minSampleRatio)
            {
                Debug.LogError("[Offline30] ============================================");
                Debug.LogError($"[Offline30] ERROR: Calibration under-sampled. " +
                               $"Frames={m_OfflineFirstPassFrameCount} | Ideal≈{idealFrames:F0} | " +
                               $"Minimum accepted={idealFrames * minSampleRatio:F0} ({minSampleRatio:P0} of ideal).");
                Debug.LogError("[Offline30] Aborting offline job before second pass. Shutting down (Offline52).");
                Debug.LogError("[Offline30] ============================================");
                Offline52_ShutdownHeadlessProcess();
                return;
            }

            if (m_OfflineFirstPassFrameCount > 0 && idealFrames > 0)
            {
                // FIX: Multiply by traversalPercent instead of dividing.
                // This scales the playhead speed down proportionately (e.g., 0.96x)
                // so the camera travels exactly 96% of the track over your target frame count.
                m_OfflineTimeScale = (m_OfflineFirstPassFrameCount / idealFrames) * traversalPercent;
            }
            else
            {
                Debug.LogError("[Offline30] WARNING: Could not calculate valid time scale. Defaulting to 1.0x");
                m_OfflineTimeScale = 1.0f;
            }

            Debug.LogError($"[Offline30] Calculation complete. " +
                           $"Duration: {duration:F2}s | TargetFPS: {targetFps} | " +
                           $"IdealFrames: {idealFrames:F0} | Traversal: {traversalPercent:P0} | " +
                           $"Final Time Scale: {m_OfflineTimeScale:F4}x");

            Offline35_PrepareSecondPass();
        }



        /// <summary>
        /// Offline 35 - Prepare for the second pass to write to a clean, separate folder.
        /// This step owns the responsibility of preparing for a new output location
        /// so the second pass does not mix frames with the first pass folder.
        /// </summary>

        private void Offline35_PrepareSecondPass()
        {
            Debug.LogError("[CameraPathCaptureRig] Offline35: Preparing second pass for clean output folder.");
            if (!string.IsNullOrEmpty(m_OfflineFirstPassFolderPath))
            {
                Debug.LogError($"[Offline35] First pass folder preserved for verification: {m_OfflineFirstPassFolderPath}");
            }
            // =================================================================
            // AUTOMATED TEXTURE PASSPORT COPY PIPELINE (STRICT DRIFT ALIGNMENT)
            // =================================================================
            if (AudioCaptureManager.m_Instance != null && AudioCaptureManager.m_Instance.m_OfflineAudioMode == AudioCaptureManager.OfflineAudioRenderMode.Use_PreRecorded_Data)
            {
                try
                {
                    // 1. Resolve the current running path target compiled by the tracker engine (_01)
                    string incomingCommandLinePath = App.Config.m_VideoPathToRender;
                    string directory = System.IO.Path.GetDirectoryName(incomingCommandLinePath);
                    string targetFilename = System.IO.Path.GetFileNameWithoutExtension(incomingCommandLinePath);
                    // 2. Derive the exact string address of your true original recording file (_00)
                    string sourceFilename = targetFilename;
                    if (sourceFilename.EndsWith("_01"))
                    {
                        sourceFilename = sourceFilename.Substring(0, sourceFilename.Length - 3) + "_00";
                    }
                    string sourceAudioPath = System.IO.Path.Combine(directory, sourceFilename + ".openbrushaudio");
                    string destinationAudioPath = System.IO.Path.Combine(directory, targetFilename + ".openbrushaudio");
                    Debug.LogError("[Audio Pipeline] Offline35 evaluating file copy variables:");
                    Debug.LogError($"[Audio Pipeline] Source Asset (Original Recording) : {sourceAudioPath}");
                    Debug.LogError($"[Audio Pipeline] Target Asset (Calibrated Run) : {destinationAudioPath}");
                    // 3. Duplicate the asset safely across the sequence barrier if the original data exists
                    if (System.IO.File.Exists(destinationAudioPath))
                    {
                        Debug.LogError($"[Audio Pipeline] Target already present, copy skipped: {targetFilename}.openbrushaudio");
                    }
                    else if (System.IO.File.Exists(sourceAudioPath))
                    {
                        System.IO.File.Copy(sourceAudioPath, destinationAudioPath);
                        Debug.LogError($"[Audio Pipeline] SUCCESS: Duplicated data asset for name-alignment continuity: {targetFilename}.openbrushaudio");
                    }
                    else
                    {
                        Debug.LogError($"[Audio Pipeline] WARNING: Root recording file not found at: {sourceAudioPath}. Copy skipped.");
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[Audio Pipeline] ERROR duplicating asset passport layers: {e.Message}");
                }
            }
            // =================================================================
            Debug.LogError("[Offline35] Second pass will write to a new, clean folder (separate from first pass).");
            // === Linear handoff: Gather parameters, then start capture ===
            Debug.LogError("[Offline35] Handing off to Offline38 to gather render parameters.");
            Offline38_GatherRenderParameters();
        }



        /// <summary>
        /// Offline 38 - Gather the final render parameters we will actually use.
        /// This centralizes the logic so Offline40 and Offline47 use the same values.
        /// </summary>
        private void Offline38_GatherRenderParameters()
        {
            // === FPS Resolution ===
            m_ResolvedOfflineFPS = (offlineCaptureFPS > 0)
                ? offlineCaptureFPS
                : (int)App.UserConfig.Video.OfflineFPS;

            // === Duration ===
            m_ResolvedDurationSeconds = targetDurationSeconds;

            // === Resolution ===
            if (captureWidthOverride > 0 && captureHeightOverride > 0)
            {
                m_ResolvedWidth = captureWidthOverride;
                m_ResolvedHeight = captureHeightOverride;
            }
            else if (App.UserConfig.Video.OfflineResolution > 0)
            {
                m_ResolvedHeight = App.UserConfig.Video.OfflineResolution;
                m_ResolvedWidth = 0;
            }
            else
            {
                m_ResolvedWidth = 0;
                m_ResolvedHeight = 0;
            }

            // === AUTOMATIC DIAGNOSTIC LOGGING ===
            Debug.LogError("[Offline38] ============================================");
            Debug.LogError("[Offline38] Resolved Render Parameters:");
            Debug.LogError($"[Offline38] FPS = {m_ResolvedOfflineFPS}");
            Debug.LogError($"[Offline38] Duration = {m_ResolvedDurationSeconds:F2} seconds");
            Debug.LogError($"[Offline38] Width = {(m_ResolvedWidth > 0 ? m_ResolvedWidth.ToString() : "Not overridden / using default")}");
            Debug.LogError($"[Offline38] Height = {(m_ResolvedHeight > 0 ? m_ResolvedHeight.ToString() : "Not overridden / using default")}");
            Debug.LogError("[Offline38] ============================================");

            // === Linear handoff to Offline40 ===
            // === Linear handoff to Offline40 (Prepare Second Pass Folder) ===
            Debug.LogError("[Offline38] Handing off to Offline40 to prepare second pass output folder.");
            Offline40_PrepareSecondPassFolder();
        }




        /// <summary>
        /// Offline40 - Start the second pass capture into its own clean folder.
        /// Sets target frame count using the same FPS priority as Offline30,
        /// resets path, starts fresh capture, and launches scaled advancement.
        ///
        /// === OFFLINE TARGET FRAME COUNT - EXPLICIT OVERRIDE LOGIC ===
        /// Use the same FPS priority as Offline30 for consistency:
        /// 1. If offlineCaptureFPS > 0 on this rig → use it as explicit override
        /// 2. Otherwise → fall back to App.UserConfig.Video.OfflineFPS
        ///
        /// Key responsibilities:
        /// - Set target frame count using the same FPS priority logic as Offline30
        ///   (rig override takes precedence over App.UserConfig.Video.OfflineFPS)
        /// - Reset the UsdPathSerializer to time 0 for a clean second pass
        /// - Perform safety validation on required components (VideoRecorder, ScreenshotManager, UsdSerializer)
        /// - Build a clean, descriptive output folder name (baseName_resolution_FPS_duration)
        /// - Start the second pass capture via VideoRecorderUtils.StartVideoCapture()
        /// - Robustly discover the actual _frames subfolder created by the capture system
        /// - Launch the scaled time advancement coroutine (Offline42)
        /// </summary>
        /// 

        /// <summary>
        /// Offline40 - Prepare second pass output folder.
        /// Builds the final folder name using resolved parameters from Offline38,
        /// checks for conflicts, and stores the clean path.
        /// This step happens early so the folder is ready before capture starts.
        /// </summary>
        /// 


        private void Offline40_PrepareSecondPassFolder()
        {
            Debug.LogError("[CameraPathCaptureRig] Offline40: Preparing second pass output folder.");

            // =================================================================
            // HEADLESS VISUALIZER SYNC: SET PLAYBACK MODE (PASS 2)
            // =================================================================
            VideoRecorderUtils.SetVisualizerPlaybackPass(true);

            // === Derive base name from the ORIGINAL .usda file, not the first pass folder ===
            string sourcePath = App.Config.m_VideoPathToRender;
            string baseName;
            if (!string.IsNullOrEmpty(sourcePath))
            {
                baseName = Path.GetFileNameWithoutExtension(sourcePath);
            }
            else
            {
                baseName = "Untitled";
                Debug.LogError("[Offline40] WARNING: m_VideoPathToRender was empty. Using fallback base name.");
            }

            string resolutionPart = (m_ResolvedWidth > 0 && m_ResolvedHeight > 0)
                ? $"{m_ResolvedWidth}x{m_ResolvedHeight}" : "default";
            string fpsPart = $"{m_ResolvedOfflineFPS}FPS";
            string durationPart = $"{m_ResolvedDurationSeconds:F0}s";

            // Second pass folder (distinct from first pass …_OfflineCalibration)
            string smartFolderName = $"{baseName}_{resolutionPart}_{fpsPart}_{durationPart}";
            string videosRoot = Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments),
                "Open Brush", "Videos");
            m_OfflineSecondPassFolderPath = Path.Combine(videosRoot, smartFolderName);

            // Option B: if this output already exists, do not redo second pass / encode.
            // Exit offline session cleanly so batch can advance (no delete, no loop).
            if (Directory.Exists(m_OfflineSecondPassFolderPath))
            {
                string existingMp4 = m_OfflineSecondPassFolderPath + ".mp4";
                bool mp4Exists = File.Exists(existingMp4);

                Debug.LogError("[Offline40] ============================================");
                Debug.LogError($"[Offline40] Output folder already exists: {m_OfflineSecondPassFolderPath}");
                Debug.LogError($"[Offline40] Matching mp4 present: {mp4Exists} ({existingMp4})");

                if (mp4Exists)
                {
                    Debug.LogError("[Offline40] SKIP: Second pass already complete (folder + mp4). Not re-encoding.");
                }
                else
                {
                    Debug.LogError("[Offline40] SKIP: Folder exists but mp4 missing. Not deleting or overwriting.");
                    Debug.LogError("[Offline40] Remove or rename the folder manually if you want a fresh second pass.");
                }

                Debug.LogError("[Offline40] Shutting down offline session (Offline52).");
                Debug.LogError("[Offline40] ============================================");
                Offline52_ShutdownHeadlessProcess();
                return;
            }

            Debug.LogError($"[Offline40] Prepared clean second pass output folder: {m_OfflineSecondPassFolderPath}");
            Debug.LogError("[Offline40] Handing off to Offline41 to start second pass capture.");
            Offline41_StartSecondPassCapture();
        }






        /// <summary>
        /// Offline41 - Start second pass capture.
        /// 
        /// Now uses a dedicated m_SecondPassUsdSerializer instance (freshly loaded from the original .usda)
        /// instead of reusing m_OfflineUsdSerializer. This eliminates any leftover state from the first pass.
        /// 
        /// Includes explicit reset to Time = 0 + Deserialize() after StartPlayback() to guarantee
        /// the camera starts at the beginning of the path.
        /// 
        /// Offline41 - Start second pass capture.
        ///
        /// Uses a dedicated m_SecondPassUsdSerializer (fresh instance) to avoid any leftover state from the first pass.
        /// 
        /// Reset sequence:
        /// - Time = 0 + Deserialize() is performed TWICE:
        ///   1. Immediately after Load() + before StartPlayback()
        ///   2. Immediately before StartVideoCapture() (final safety reset)
        /// </summary>
        /// 


        private void Offline41_StartSecondPassCapture()
        {
            Debug.LogError("[CameraPathCaptureRig] Offline41: Starting second pass capture.");

            float targetFps = (offlineCaptureFPS > 0) ? offlineCaptureFPS : App.UserConfig.Video.OfflineFPS;
            m_OfflineTargetFrameCount = Mathf.RoundToInt(targetDurationSeconds * targetFps);
            VideoRecorderUtils.SetTargetFrameCount(m_OfflineTargetFrameCount);
            Debug.LogError($"[Offline41] Target frame count: {m_OfflineTargetFrameCount} frames @ {targetFps} FPS");

            if (string.IsNullOrEmpty(App.Config.m_VideoPathToRender))
            {
                Debug.LogError("[Offline41] ERROR: m_VideoPathToRender is empty.");
                return;
            }

            // =================================================================
            // INTEGRATED OFFLINE AUDIO TEXTURE SHEET PRE-LOAD PIPELINE
            // =================================================================
            if (AudioCaptureManager.m_Instance != null && AudioCaptureManager.m_Instance.m_OfflineAudioMode == AudioCaptureManager.OfflineAudioRenderMode.Use_PreRecorded_Data)
            {
                string targetUsdPath = App.Config.m_VideoPathToRender;
                string directory = System.IO.Path.GetDirectoryName(targetUsdPath);
                string filename = System.IO.Path.GetFileNameWithoutExtension(targetUsdPath);
                string companionAudioPath = System.IO.Path.Combine(directory, filename + ".openbrushaudio");

                Debug.LogError($"[Audio Pipeline] Headless Flow evaluating current audio track name: {companionAudioPath}");

                if (System.IO.File.Exists(companionAudioPath))
                {
                    Shader.EnableKeyword("AUDIO_REACTIVE");
                    if (App.Instance != null)
                    {
                        App.Instance.AudioReactiveBrushesActive(true);
                    }

                    if (VisualizerManager.m_Instance != null)
                    {
                        VisualizerManager.m_Instance.SetPlaybackMode(true);
                        VisualizerManager.m_Instance.LoadBakedAudioCache(companionAudioPath);
                    }
                    Debug.LogError($"[Audio Pipeline] SUCCESS: Verified and loaded offline companion texture file into RAM: {companionAudioPath}");
                }
                else
                {
                    Shader.DisableKeyword("AUDIO_REACTIVE");
                    if (App.Instance != null)
                    {
                        App.Instance.AudioReactiveBrushesActive(false);
                    }

                    if (VisualizerManager.m_Instance != null)
                    {
                        VisualizerManager.m_Instance.SetPlaybackMode(false);
                    }
                    Debug.LogError($"[Audio Pipeline] WARNING: Companion audio file missing on disk at: {companionAudioPath}. Defaulting to flat rendering mode.");
                }
            }
            // =================================================================

            try
            {
                if (m_SecondPassUsdSerializer == null)
                {
                    GameObject targetVideoCameraGO = null;

                    // === CRASH-FREE DIRECT SCALED MEMORY SCAN ===
                    var allGameObjects = Resources.FindObjectsOfTypeAll<GameObject>();
                    foreach (var go in allGameObjects)
                    {
                        if (go.name == "VideoCamera")
                        {
                            targetVideoCameraGO = go;
                            break;
                        }
                    }

                    if (targetVideoCameraGO != null)
                    {
                        m_SecondPassUsdSerializer = targetVideoCameraGO.AddComponent<UsdPathSerializer>();
                        Debug.LogError($"[Offline41] SUCCESS: Targeted active camera blueprint context. Isolated serializer successfully attached to: {targetVideoCameraGO.name}");
                    }
                    else
                    {
                        Debug.LogError("[Offline41] CRITICAL ERROR: The physical VideoCamera asset could not be found anywhere in asset layers. Aborting pass.");
                        return;
                    }
                }

                m_SecondPassUsdSerializer.Load(App.Config.m_VideoPathToRender);
                m_SecondPassUsdSerializer.StartPlayback();
                StartCoroutine(ResetAndStartSecondPassAfterYield());
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Offline41] EXCEPTION initializing second pass: {e.Message}");
                m_SecondPassUsdSerializer = null;
            }
        }




        private IEnumerator ResetAndStartSecondPassAfterYield()
        {
            if (m_SecondPassUsdSerializer == null) yield break;

            // 1. Call the stock function to turn off recording mode safely
            m_SecondPassUsdSerializer.Stop();

            // 2. FORCE THE TIMELINE REWIND USING THE FACTORY PROPERTY
            // Setting this to 0.0 tells the stock PC block to point its internal 
            // USD clock variables back to the start line.
            m_SecondPassUsdSerializer.Time = 0.0;

            // 3. Wait one frame for Unity's matrix pipeline to process the time shift
            yield return null;

            // 4. Call the stock deserializer function.
            // In the standard PC block, Deserialize() reads the current 'Time' value 
            // and physically updates the camera's GameObject position/rotation matrices.
            m_SecondPassUsdSerializer.Deserialize();

            // 5. Force an immediate transform matrix update so your distance logs read correctly
            Physics.SyncTransforms();

            Debug.LogError($"[Offline41] Post-reset position: {m_SecondPassUsdSerializer.transform.position}");

            if (m_FirstPassEndPosition != Vector3.zero)
            {
                float dist = Vector3.Distance(m_SecondPassUsdSerializer.transform.position, m_FirstPassEndPosition);
                Debug.LogError($"[Offline41] Distance from first-pass END: {dist:F4}");
                if (dist < 1.0f)
                    Debug.LogError("[Offline41] WARNING: Still near first-pass end after reset!");
            }

            if (m_OfflineVideoRecorder == null || string.IsNullOrEmpty(m_OfflineSecondPassFolderPath))
            {
                Debug.LogError("[Offline41] ERROR: Missing components or folder path.");
                yield break;
            }

            // 6. Fully arm the system for playback right before starting capture utilities
            m_SecondPassUsdSerializer.StartPlayback();
            yield return null;

            Debug.LogError($"[Offline41] Starting capture to: {m_OfflineSecondPassFolderPath}");
            const bool appendFramesSuffix = false;

            bool started = VideoRecorderUtils.StartVideoCapture(
                m_OfflineSecondPassFolderPath,
                m_OfflineVideoRecorder,
                m_SecondPassUsdSerializer,
                true,
                appendFramesSuffix);

            if (started)
            {
                Debug.LogError("[Offline41] Second pass capture started.");
                m_OfflineFinalFrameFolderPath = m_OfflineSecondPassFolderPath;

                if (m_OfflineScaledTimeCoroutine != null)
                    StopCoroutine(m_OfflineScaledTimeCoroutine);

                m_OfflineScaledTimeCoroutine = StartCoroutine(Offline42_AdvanceScaledTime());
            }
            else
            {
                Debug.LogError("[Offline41] FAILED to start second pass capture.");
            }
        }

        /// <summary>
        /// Offline42 setup: validate mapping, ratio, N, fps before the advance loop.
        /// Does not check pathDuration (checked in Offline42_AdvanceScaledTime).
        /// Hard-fail: log + return false. Do not clamp silently.
        /// </summary>
        private bool Offline42_ValidateInputs(
            float targetFps,
            int targetFrameCount,
            out float traversalRatio)
        {
            traversalRatio = 0f;

            if (m_OfflinePathMapping != OfflinePathMapping.Prefix &&
                m_OfflinePathMapping != OfflinePathMapping.Compress)
            {
                Debug.LogError(
                    "[Offline42] HARD-FAIL: Unknown OfflinePathMapping value " +
                    ((int)m_OfflinePathMapping) + ". Aborting second pass.");
                return false;
            }

            if (targetFps <= 0f)
            {
                Debug.LogError("[Offline42] HARD-FAIL: targetFps <= 0. Aborting second pass.");
                return false;
            }

            if (targetFrameCount <= 0)
            {
                Debug.LogError(
                    "[Offline42] HARD-FAIL: target frame count N <= 0 (N=" +
                    targetFrameCount + "). Aborting second pass.");
                return false;
            }

            traversalRatio = secondPassTraversalPercent / 100f;
            if (traversalRatio <= 0f || traversalRatio > 1f)
            {
                Debug.LogError(
                    "[Offline42] HARD-FAIL: secondPassTraversalPercent must be in 1..100 " +
                    "(ratio in (0, 1]). Got percent=" + secondPassTraversalPercent +
                    " ratio=" + traversalRatio.ToString("F4") + ". Aborting second pass.");
                traversalRatio = 0f;
                return false;
            }

            Debug.LogError(
                "[Offline42] Path mapping inputs OK so far: mode=" + m_OfflinePathMapping +
                " traversalPercent=" + secondPassTraversalPercent +
                " ratio=" + traversalRatio.ToString("F4") +
                " N=" + targetFrameCount +
                " targetFps=" + targetFps.ToString("F2") +
                " (pathDuration checked next in Offline42).");
            return true;
        }

        private IEnumerator Offline42_AdvanceScaledTime()
        {
            Debug.LogError("[CameraPathCaptureRig] ============================================");
            Debug.LogError("[CameraPathCaptureRig] Offline42: Production time advancement started.");
            Debug.LogError("[CameraPathCaptureRig] ============================================");

            bool firstFrameCaptured = false;
            bool loggedStartingPosition = false;
            int diagnosticFrameCounter = 0;
            // EnableSimulatedBPM at most once if enum says Forced Sim but property was cleared.
            bool simRecoveredThisPass = false;
            // Compress + pure black: log once that pad is a no-op.
            bool loggedCompressBlackNoOp = false;

            // -------------------------------------------------------------------------
            // Offline42 setup: fps, N, mapping validation (hard-fail, no silent clamp)
            // -------------------------------------------------------------------------
            float targetFps = (offlineCaptureFPS > 0) ? offlineCaptureFPS : App.UserConfig.Video.OfflineFPS;
            if (targetFps <= 0f) targetFps = 60f;

            float traversalRatio;
            if (!Offline42_ValidateInputs(targetFps, m_OfflineTargetFrameCount, out traversalRatio))
            {
                Debug.LogError("[Offline42] Aborting before advance loop (validation hard-fail).");
                Offline52_ShutdownHeadlessProcess();
                yield break;
            }

            if (m_SecondPassUsdSerializer == null)
            {
                Debug.LogError("[Offline42] HARD-FAIL: m_SecondPassUsdSerializer is null. Aborting second pass.");
                Offline52_ShutdownHeadlessProcess();
                yield break;
            }

            float pathDuration = (float)m_SecondPassUsdSerializer.Duration;
            if (pathDuration <= 0f)
            {
                Debug.LogError(
                    "[Offline42] HARD-FAIL: pathDuration <= 0 (Duration=" +
                    pathDuration.ToString("F4") + "). Aborting second pass.");
                Offline52_ShutdownHeadlessProcess();
                yield break;
            }

            // -------------------------------------------------------------------------
            // Offline42 path: Prefix vs Compress step and safety limits
            // m_OfflineTimeScale is NOT used for advancement (Offline30 legacy/passport only).
            // -------------------------------------------------------------------------
            double pathStepSeconds;
            float pathSpan = pathDuration * traversalRatio;
            int motionEndFrame = Mathf.FloorToInt(m_OfflineTargetFrameCount * traversalRatio);
            if (motionEndFrame < 0) motionEndFrame = 0;
            if (motionEndFrame > m_OfflineTargetFrameCount) motionEndFrame = m_OfflineTargetFrameCount;

            // Prefix: path-time hard max stays near video scale (2 * T).
            // Compress: do NOT use 2*T (false-aborts). Use pathSpan + epsilon only.
            const float kPathSpanEpsilon = 0.001f;
            float scaledTimeHardMax = targetDurationSeconds * 2.0f;
            bool usePrefixPathHardMax = (m_OfflinePathMapping == OfflinePathMapping.Prefix);
            bool useCompressPathSpanGuard = (m_OfflinePathMapping == OfflinePathMapping.Compress);

            if (m_OfflinePathMapping == OfflinePathMapping.Prefix)
            {
                // Blotch fix: uniform 1/fps while moving. NEVER multiply step by traversal.
                pathStepSeconds = 1.0 / targetFps;
                Debug.LogError(
                    "[Offline42] Prefix path: pathStep=" + pathStepSeconds.ToString("F6") +
                    "s (1/fps, no traversal scale) motionEndFrame=" + motionEndFrame +
                    "/" + m_OfflineTargetFrameCount +
                    " hardMaxPathTime=" + scaledTimeHardMax.ToString("F2") + "s");
            }
            else
            {
                // Compress: fit pathSpan into all N frames.
                pathStepSeconds = (m_OfflineTargetFrameCount > 0)
                    ? (double)pathSpan / (double)m_OfflineTargetFrameCount
                    : 0.0;
                Debug.LogError(
                    "[Offline42] Compress path: pathDuration=" + pathDuration.ToString("F4") +
                    "s pathSpan=" + pathSpan.ToString("F4") +
                    "s pathStep=" + pathStepSeconds.ToString("F6") +
                    "s N=" + m_OfflineTargetFrameCount +
                    " spanGuard=pathSpan+eps (" + kPathSpanEpsilon.ToString("F4") + "s)");
            }

            // Wall-clock no-progress timeout (both modes).
            // Protects against slow-but-returning captures. Does not protect against a true
            // hard freeze inside CaptureFrame (that call is fully synchronous).
            const float kNoProgressTimeoutSeconds = 15f;
            float lastSuccessfulWriteRealtime = Time.realtimeSinceStartup;

            if (App.Config != null)
            {
                Debug.LogError("[Offline42] App.Config.OfflineRender=" + App.Config.OfflineRender);
            }

            // Reactivity diagnostics at start.
            {
                var acm = AudioCaptureManager.m_Instance;
                bool forceSim = acm != null &&
                    acm.m_OfflineAudioMode == AudioCaptureManager.OfflineAudioRenderMode.Force_Simulated_BPM;
                bool simFlag = acm != null && acm.IsSimulatedBPMModeActive;
                bool playback = VisualizerManager.m_Instance != null &&
                    VisualizerManager.m_Instance.IsPlaybackModeActive;
                Debug.LogError(
                    "[Offline42] Reactivity at start: mode=" +
                    (acm != null ? acm.m_OfflineAudioMode.ToString() : "null") +
                    " forceSim=" + forceSim + " simFlag=" + simFlag + " playback=" + playback);
            }

            Debug.LogError(
                "[Offline42] Second-pass serializer initial state -> Time: " +
                m_SecondPassUsdSerializer.Time.ToString("F3") +
                " Duration: " + pathDuration.ToString("F3"));

            // CaptureFrame/ShouldCapture must always see a 1/fps clock (not path time).
            // Path time drives pose only. Compress pathStep != 1/fps; Prefix pad freezes path time.
            Debug.LogError(
                "[Offline42] CaptureFrame currentTime uses video capture clock (frame+1)/fps, " +
                "not path serializer Time (ShouldCapture gate).");

            while (true)
            {
                if (!loggedStartingPosition)
                {
                    Debug.LogError(
                        "[Offline42] STARTING POSITION: " +
                        m_SecondPassUsdSerializer.transform.position);
                    loggedStartingPosition = true;
                }

                double timeBefore = m_SecondPassUsdSerializer.Time;

                int currentFrame = VideoRecorderUtils.ActiveStillFrameExporter != null
                    ? VideoRecorderUtils.ActiveStillFrameExporter.FrameCount
                    : 0;

                // -------------------------------------------------------------------------
                // Offline42 path advance (fixed step; independent of wall-clock capture cost)
                // -------------------------------------------------------------------------
                if (m_OfflinePathMapping == OfflinePathMapping.Prefix)
                {
                    // Move only before motionEndFrame; then freeze path time (pad / hold).
                    if (currentFrame < motionEndFrame)
                    {
                        m_SecondPassUsdSerializer.Time += pathStepSeconds;
                        m_SecondPassUsdSerializer.Deserialize();
                    }
                }
                else
                {
                    // Compress: advance every frame across full N.
                    m_SecondPassUsdSerializer.Time += pathStepSeconds;
                    m_SecondPassUsdSerializer.Deserialize();
                }

                if (m_SecondPassUsdSerializer.transform.hasChanged)
                {
                    m_SecondPassUsdSerializer.transform.hasChanged = false;
                }

                // Video / capture clock: always 1/fps paced for ShouldCapture and BPM.
                // (currentFrame + 1) so the first write sees ~1/fps, matching prior Prefix behavior.
                float captureClockSeconds = (targetFps > 0f)
                    ? ((float)(currentFrame + 1) / targetFps)
                    : 0f;
                float videoClockSeconds = (targetFps > 0f)
                    ? ((float)currentFrame / targetFps)
                    : 0f;

                // -------------------------------------------------------------------------
                // Offline42 react: bake progress + BPM on video clock (file timeline)
                // -------------------------------------------------------------------------
                var acmLoop = AudioCaptureManager.m_Instance;
                var vis = VisualizerManager.m_Instance;
                bool forceSimLoop = acmLoop != null &&
                    acmLoop.m_OfflineAudioMode == AudioCaptureManager.OfflineAudioRenderMode.Force_Simulated_BPM;
                bool simFlagLoop = acmLoop != null && acmLoop.IsSimulatedBPMModeActive;
                bool playbackLoop = vis != null && vis.IsPlaybackModeActive;

                if (forceSimLoop && vis != null)
                {
                    if (!simFlagLoop && !simRecoveredThisPass)
                    {
                        Debug.LogError(
                            "[Offline42-SimBPM] RECOVERY: simFlag was false at diagnosticFrame=" +
                            diagnosticFrameCounter + ", calling EnableSimulatedBPM once this pass.");
                        acmLoop.EnableSimulatedBPM(acmLoop.m_SimulatedBPM);
                        simRecoveredThisPass = true;
                    }

                    // Force_Simulated_BPM: phase from video/file clock (not path time).
                    // Do not use UpdateOfflineSimulationStep (would BakeCurrentFrame every step).
                    vis.UpdateSimulatedBeats(videoClockSeconds);

                    if (diagnosticFrameCounter % 60 == 0)
                    {
                        Debug.LogError(
                            "[Offline42-SimBPM] videoClock=" + videoClockSeconds.ToString("F3") +
                            " captureClock=" + captureClockSeconds.ToString("F3") +
                            " pathTime=" + m_SecondPassUsdSerializer.Time.ToString("F3") +
                            " enumForceSim=" + forceSimLoop +
                            " simFlag=" + acmLoop.IsSimulatedBPMModeActive +
                            " recovered=" + simRecoveredThisPass);
                    }
                }
                else if (playbackLoop && vis != null)
                {
                    float bakeProgress;
                    if (m_OfflinePathMapping == OfflinePathMapping.Prefix)
                    {
                        // Progress over the motion window only; hold 1 in pad.
                        if (motionEndFrame <= 0)
                        {
                            bakeProgress = 1f;
                        }
                        else if (currentFrame >= motionEndFrame)
                        {
                            bakeProgress = 1f;
                        }
                        else
                        {
                            bakeProgress = (float)currentFrame / (float)motionEndFrame;
                        }
                    }
                    else
                    {
                        // Compress: progress over full frame budget.
                        bakeProgress = (m_OfflineTargetFrameCount > 0)
                            ? ((float)currentFrame / (float)m_OfflineTargetFrameCount)
                            : 0f;
                    }

                    if (bakeProgress < 0f) bakeProgress = 0f;
                    if (bakeProgress > 1f) bakeProgress = 1f;

                    vis.InjectTwoPassBakedFrame(bakeProgress);

                    if (diagnosticFrameCounter % 60 == 0)
                    {
                        Debug.LogError(
                            "[Offline42-Bake] progress=" + bakeProgress.ToString("F4") +
                            " pathTime=" + m_SecondPassUsdSerializer.Time.ToString("F3") +
                            " videoClock=" + videoClockSeconds.ToString("F3") +
                            " mode=" + m_OfflinePathMapping);
                    }
                }
                else if (diagnosticFrameCounter % 60 == 0)
                {
                    Debug.LogError(
                        "[Offline42-React] NO inject - forceSim=" + forceSimLoop +
                        " simFlag=" + simFlagLoop + " playback=" + playbackLoop);
                }

                // Pure black pad: Prefix only. Compress has no pad region.
                bool isCustomBlackPaddingFrame = false;
                if (m_OfflinePathMapping == OfflinePathMapping.Prefix)
                {
                    isCustomBlackPaddingFrame = m_WritePaddingAsPureBlack
                        && (currentFrame >= motionEndFrame);
                }
                else if (m_WritePaddingAsPureBlack && !loggedCompressBlackNoOp)
                {
                    Debug.LogError(
                        "[Offline42] WritePaddingAsPureBlack is ON but mapping is Compress: " +
                        "pad is a no-op (no freeze region). Continuing full-span motion frames.");
                    loggedCompressBlackNoOp = true;
                }

                if (VideoRecorderUtils.ActiveStillFrameExporter != null)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();

                    // CRITICAL: ShouldCapture gates on this value. Must be 1/fps-paced in both modes.
                    // Do not pass path serializer Time (Compress step and Prefix pad break that clock).
                    VideoRecorderUtils.ActiveStillFrameExporter.CaptureFrame(
                        captureClockSeconds,
                        isCustomBlackPaddingFrame
                    );

                    sw.Stop();
                    long captureTimeMs = sw.ElapsedMilliseconds;

                    int writtenAfter = VideoRecorderUtils.ActiveStillFrameExporter.FrameCount;
                    if (writtenAfter > currentFrame)
                    {
                        lastSuccessfulWriteRealtime = Time.realtimeSinceStartup;
                    }

                    if (diagnosticFrameCounter % 60 == 0)
                    {
                        Debug.LogError(
                            "[Offline42] Progress Cycle: " + diagnosticFrameCounter +
                            " | Written Frames: " + currentFrame + "/" + m_OfflineTargetFrameCount +
                            " | pathTime: " + timeBefore.ToString("F4") + "->" +
                            m_SecondPassUsdSerializer.Time.ToString("F4") +
                            " | captureClock: " + captureClockSeconds.ToString("F4") +
                            " | Pos: " + m_SecondPassUsdSerializer.transform.position);

                        int exportWrittenFrameCount = VideoRecorderUtils.ActiveStillFrameExporter.FrameCount;
                        Debug.LogError(
                            "[OfflineFrameTiming] phase=Export,frame=" + exportWrittenFrameCount +
                            ",res=" + VideoRecorderUtils.ActiveStillFrameExporter.TargetWidth +
                            "x" + VideoRecorderUtils.ActiveStillFrameExporter.TargetHeight +
                            ",captureTimeMs=" + captureTimeMs);
                    }

                    if (!firstFrameCaptured && writtenAfter > 0)
                    {
                        Debug.LogError("[Offline42] First frame successfully captured in second pass.");
                        firstFrameCaptured = true;
                    }
                }

                // -------------------------------------------------------------------------
                // Offline42 exit guards
                // -------------------------------------------------------------------------
                int verifiedWrittenCount = VideoRecorderUtils.ActiveStillFrameExporter != null
                    ? VideoRecorderUtils.ActiveStillFrameExporter.FrameCount
                    : currentFrame;

                // Primary normal exit: exact frame-count budget
                if (verifiedWrittenCount >= m_OfflineTargetFrameCount && m_OfflineTargetFrameCount > 0)
                {
                    Debug.LogError(
                        "[Offline42] SUCCESS: All requested target frames written to disk: " +
                        verifiedWrittenCount + "/" + m_OfflineTargetFrameCount + ". Terminating loop.");
                    break;
                }

                // Wall-clock no-progress timeout (both modes)
                if (Time.realtimeSinceStartup - lastSuccessfulWriteRealtime > kNoProgressTimeoutSeconds)
                {
                    Debug.LogError(
                        "[Offline42] NO-PROGRESS TIMEOUT: No new frame written for more than " +
                        kNoProgressTimeoutSeconds.ToString("F0") + " real seconds. " +
                        "Written=" + verifiedWrittenCount + "/" + m_OfflineTargetFrameCount + ". Aborting.");
                    break;
                }

                // Prefix only: path-time hard max (2 * T)
                if (usePrefixPathHardMax &&
                    m_SecondPassUsdSerializer.Time >= scaledTimeHardMax)
                {
                    Debug.LogError(
                        "[Offline42] PATH-TIME HARD MAX reached (" + scaledTimeHardMax.ToString("F2") +
                        "s). Written=" + verifiedWrittenCount + "/" + m_OfflineTargetFrameCount +
                        ". Aborting.");
                    break;
                }

                // Compress only: pathSpan + epsilon overrun guard
                if (useCompressPathSpanGuard &&
                    m_SecondPassUsdSerializer.Time > (pathSpan + kPathSpanEpsilon))
                {
                    Debug.LogError(
                        "[Offline42] COMPRESS PATH SPAN GUARD: pathTime=" +
                        m_SecondPassUsdSerializer.Time.ToString("F4") +
                        " > pathSpan+eps=" + (pathSpan + kPathSpanEpsilon).ToString("F4") +
                        ". Written=" + verifiedWrittenCount + "/" + m_OfflineTargetFrameCount +
                        ". Aborting.");
                    break;
                }

                if (m_SecondPassUsdSerializer.IsFinished)
                {
                    Debug.LogError("[Offline42] Path IsFinished. Aborting.");
                    break;
                }

                diagnosticFrameCounter++;
                yield return null;
            }

            // -------------------------------------------------------------------------
            // Offline42 shutdown (normal or mid-loop abort after frames may exist)
            // -------------------------------------------------------------------------
            Debug.LogError("[Offline42] Sequence limits reached. Shutting down recording pipeline components...");

            if (VideoRecorderUtils.ActiveStillFrameExporter != null)
            {
                VideoRecorderUtils.StopVideoCapture(true);
            }

            float cleanupTimer = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - cleanupTimer < 1.0f)
            {
                yield return null;
            }

            Debug.LogError("[Offline42] ============================================");
            Debug.LogError(
                "[Offline42] TERMINATION SUMMARY COMPLETE | Final Time: " +
                m_SecondPassUsdSerializer.Time.ToString("F4") +
                "s | mode=" + m_OfflinePathMapping);
            Debug.LogError("[Offline42] Handing off cleanly to final video generation threads.");
            Debug.LogError("[Offline42] ============================================");

            if (VisualizerManager.m_Instance != null)
            {
                VisualizerManager.m_Instance.CleanUpTwoPassScratchAssets();
            }

            Offline45_WaitForSecondPassToEnd();
        }


        private void Offline45_WaitForSecondPassToEnd()
        {
            Debug.LogError("[CameraPathCaptureRig] Offline45: Second pass completed. Proceeding to final video generation.");

            if (m_OfflineScaledTimeCoroutine != null)
            {
                Debug.LogError("[Offline45] Stopping scaled time coroutine.");
                StopCoroutine(m_OfflineScaledTimeCoroutine);
                m_OfflineScaledTimeCoroutine = null;
            }

            // Generate final video
            Offline47_GenerateFinalVideo();

            // Final status check
            if (!string.IsNullOrEmpty(m_OfflineFinalFrameFolderPath) && Directory.Exists(m_OfflineFinalFrameFolderPath))
            {
                Debug.LogError($"[Offline45] Second pass frames folder still exists: {m_OfflineFinalFrameFolderPath}");
            }
            else
            {
                Debug.LogError("[Offline45] WARNING: Final frames folder path is no longer valid after video generation.");
            }

            Offline50_Cleanup();
        }



        /// <summary>
        /// Offline47 - Generates the final high-quality .mp4 video from the second-pass frames.
        /// This step is fully self-contained. It handles parameter validation, path derivation,
        /// ffmpeg execution, and result verification without relying on any preview utilities.
        /// Uses offline-specific quality settings (CRF + Preset) and the flat folder structure.
        /// Optionally muxes a companion live .wav when m_OfflineMuxCompanionWav is enabled.
        /// </summary>
        /// <summary>
        /// Offline47 - Generates the final high-quality .mp4 video from the second-pass frames.
        /// This step is fully self-contained. It handles parameter validation, path derivation,
        /// ffmpeg execution, and result verification without relying on any preview utilities.
        /// Uses offline-specific quality settings (CRF + Preset) and the flat folder structure.
        /// Optionally muxes a companion live .wav when m_OfflineMuxCompanionWav is enabled.
        /// </summary>
        private void Offline47_GenerateFinalVideo()
        {
            Debug.LogError("[CameraPathCaptureRig] ============================================");
            Debug.LogError("[CameraPathCaptureRig] Offline47: Generating final video from second pass frames.");
            Debug.LogError("[CameraPathCaptureRig] ============================================");
            if (string.IsNullOrEmpty(m_OfflineFinalFrameFolderPath) ||
                !Directory.Exists(m_OfflineFinalFrameFolderPath))
            {
                Debug.LogError("[Offline47] Cannot generate final video. Frames folder is invalid or missing: " +
                               (m_OfflineFinalFrameFolderPath ?? "NULL"));
                return;
            }
            Debug.LogError($"[Offline47] Using frames folder: {m_OfflineFinalFrameFolderPath}");
            // === Safety fallbacks for all video generation parameters ===
            int finalOutputFPS = (offlineOutputFPS > 0) ? offlineOutputFPS : 30;
            int finalCRF = (offlineVideoQualityCRF > 0) ? offlineVideoQualityCRF : 18;
            string finalPreset = !string.IsNullOrEmpty(offlineVideoQualityPreset) ? offlineVideoQualityPreset : "veryslow";
            if (finalOutputFPS != offlineOutputFPS)
                Debug.LogError($"[Offline47] WARNING: Invalid offlineOutputFPS detected. Using fallback FPS = {finalOutputFPS}");
            if (finalCRF != offlineVideoQualityCRF)
                Debug.LogError($"[Offline47] WARNING: Invalid CRF value detected. Using fallback CRF = {finalCRF}");
            if (finalPreset != offlineVideoQualityPreset)
                Debug.LogError($"[Offline47] WARNING: Invalid Preset detected. Using fallback Preset = {finalPreset}");
            Debug.LogError($"[Offline47] Using Output FPS = {finalOutputFPS}, CRF = {finalCRF}, Preset = {finalPreset}");
            // === Derive clean final video path ===
            string framesFolderName = Path.GetFileName(m_OfflineFinalFrameFolderPath);
            string parentFolder = Path.GetDirectoryName(m_OfflineFinalFrameFolderPath);
            string finalVideoPath = Path.Combine(parentFolder, framesFolderName + ".mp4");
            Debug.LogError($"[Offline47] Derived final video path: {finalVideoPath}");
            // === Optional companion wav (from live recording, next to source .usda) ===
            string companionWavPath = null;
            bool muxAudio = false;
            if (m_OfflineMuxCompanionWav)
            {
                string sourcePath = App.Config != null ? App.Config.m_VideoPathToRender : null;
                if (!string.IsNullOrEmpty(sourcePath))
                {
                    string sourceDir = Path.GetDirectoryName(sourcePath);
                    string sourceBase = Path.GetFileNameWithoutExtension(sourcePath);
                    companionWavPath = Path.Combine(sourceDir, sourceBase + ".wav");
                }
                if (!string.IsNullOrEmpty(companionWavPath) && File.Exists(companionWavPath))
                {
                    long wavBytes = new FileInfo(companionWavPath).Length;
                    if (wavBytes > 0)
                    {
                        muxAudio = true;
                        Debug.LogError($"[Offline47] Companion wav found for mux: \"{companionWavPath}\" ({wavBytes} bytes)");
                    }
                    else
                    {
                        Debug.LogError($"[Offline47] Companion wav exists but is empty; skipping audio mux: \"{companionWavPath}\"");
                    }
                }
                else
                {
                    Debug.LogError($"[Offline47] m_OfflineMuxCompanionWav is ON but no companion wav found at: \"{companionWavPath ?? "NULL"}\". Encoding video-only.");
                }
            }
            else
            {
                Debug.LogError("[Offline47] m_OfflineMuxCompanionWav is OFF. Encoding video-only.");
            }
            // === Output duration cap (IntegratedPlan Part 1) ===
            // Force container length to the offline job target T so a longer companion wav
            // cannot extend the mp4 past the frame sequence. Do not use -shortest.
            int outputFrameBudget = (m_OfflineTargetFrameCount > 0)
                ? m_OfflineTargetFrameCount
                : Mathf.RoundToInt(
                    ((m_ResolvedDurationSeconds > 0f) ? m_ResolvedDurationSeconds : targetDurationSeconds)
                    * finalOutputFPS);
            float outputDurationSeconds;
            if (m_OfflineTargetFrameCount > 0 && finalOutputFPS > 0)
            {
                outputDurationSeconds = (float)m_OfflineTargetFrameCount / (float)finalOutputFPS;
            }
            else if (m_ResolvedDurationSeconds > 0f)
            {
                outputDurationSeconds = m_ResolvedDurationSeconds;
            }
            else if (targetDurationSeconds > 0f)
            {
                outputDurationSeconds = targetDurationSeconds;
            }
            else
            {
                outputDurationSeconds = 8.0f;
                Debug.LogError("[Offline47] WARNING: No valid duration source; using fallback outputDurationSeconds = 8.0");
            }
            if (outputFrameBudget <= 0)
            {
                outputFrameBudget = Mathf.Max(1, Mathf.RoundToInt(outputDurationSeconds * finalOutputFPS));
            }
            Debug.LogError(
                $"[Offline47] Output duration cap: -t {outputDurationSeconds:F6}s " +
                $"(frameBudget={outputFrameBudget}, fps={finalOutputFPS}). " +
                "Does not use -shortest.");

            // === Pad-region audio policy (companion wav mux) ===
            // Black padding uses motionEndFrameThreshold = N * traversalRatio in Offline42.
            // AllowAudioDuringPadding only affected the HiFi buffer path before; mux ignored it.
            // When OFF and traversal < 100%, force volume=0 on the wav from content end to T.
            bool silencePadAudio = false;
            float contentEndSeconds = outputDurationSeconds;
            string audioPadFilter = "";
            if (muxAudio)
            {
                bool allowAudioDuringPadding = true;
                if (AudioCaptureManager.m_Instance != null)
                {
                    allowAudioDuringPadding = AudioCaptureManager.m_Instance.AllowAudioDuringPadding;
                }
                float traversalRatio = secondPassTraversalPercent / 100f;
                if (traversalRatio <= 0f)
                {
                    traversalRatio = 1f;
                }
                if (!allowAudioDuringPadding && traversalRatio < 0.999f)
                {
                    int contentFrames = Mathf.RoundToInt(outputFrameBudget * traversalRatio);
                    if (contentFrames < 1)
                    {
                        contentFrames = 1;
                    }
                    if (contentFrames >= outputFrameBudget)
                    {
                        contentFrames = outputFrameBudget;
                    }
                    contentEndSeconds = (finalOutputFPS > 0)
                        ? (contentFrames / (float)finalOutputFPS)
                        : (outputDurationSeconds * traversalRatio);
                    if (contentEndSeconds > 0f && contentEndSeconds < outputDurationSeconds)
                    {
                        silencePadAudio = true;
                        string contentEndToken = contentEndSeconds.ToString(
                            "F6", System.Globalization.CultureInfo.InvariantCulture);
                        // Mute from content end through the rest of -t (matches black pad).
                        audioPadFilter = $" -af \"volume=enable='gte(t,{contentEndToken})':volume=0\"";
                        Debug.LogError(
                            $"[Offline47] Pad audio silence ON: AllowAudioDuringPadding=false | " +
                            $"traversal={traversalRatio:P0} | contentFrames={contentFrames}/{outputFrameBudget} | " +
                            $"contentEnd={contentEndSeconds:F6}s of {outputDurationSeconds:F6}s");
                    }
                    else
                    {
                        Debug.LogError(
                            $"[Offline47] Pad audio silence skipped (no pad region): " +
                            $"contentEnd={contentEndSeconds:F6}s duration={outputDurationSeconds:F6}s");
                    }
                }
                else
                {
                    Debug.LogError(
                        $"[Offline47] Pad audio silence OFF: AllowAudioDuringPadding={allowAudioDuringPadding} | " +
                        $"traversal={traversalRatio:P0}");
                }
            }

            Debug.LogError("[Offline47] Starting ffmpeg encoding...");
            // === Build ffmpeg command (self-contained) ===
            // Standard = current line (rollback).
            // WindowsPlayerSafe = -fps_mode cfr + -movflags +faststart + -bf 0 (no B-frames).
            // Does not change resolution, CRF, preset, -t, path, or capture timing.
            string inputPattern = Path.Combine(m_OfflineFinalFrameFolderPath, framesFolderName + "_frame_%06d.png");
            string durationToken = outputDurationSeconds.ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
            bool windowsPlayerSafe = (offlineFfmpegEncodePath == OfflineFfmpegEncodePath.WindowsPlayerSafe);
            string ffmpegCommand;
            if (muxAudio)
            {
                // Frames + companion wav. Duration follows job target, not the wav. Do not use -shortest.
                // Optional -af mutes pad region when AllowAudioDuringPadding is false.
                if (windowsPlayerSafe)
                {
                    ffmpegCommand = string.Format(
                        "-y -framerate {0} -i \"{1}\" -i \"{2}\" -t {3} -c:v libx264 -pix_fmt yuv420p -crf {4} -preset {5} -c:a aac -b:a 320k{6} -fps_mode cfr -movflags +faststart -bf 0 \"{7}\"",
                        finalOutputFPS,
                        inputPattern,
                        companionWavPath,
                        durationToken,
                        finalCRF,
                        finalPreset,
                        audioPadFilter,
                        finalVideoPath
                    );
                }
                else
                {
                    ffmpegCommand = string.Format(
                        "-y -framerate {0} -i \"{1}\" -i \"{2}\" -t {3} -c:v libx264 -pix_fmt yuv420p -crf {4} -preset {5} -c:a aac -b:a 320k{6} -vsync 0 \"{7}\"",
                        finalOutputFPS,
                        inputPattern,
                        companionWavPath,
                        durationToken,
                        finalCRF,
                        finalPreset,
                        audioPadFilter,
                        finalVideoPath
                    );
                }
            }
            else
            {
                if (windowsPlayerSafe)
                {
                    ffmpegCommand = string.Format(
                        "-y -framerate {0} -i \"{1}\" -t {2} -c:v libx264 -pix_fmt yuv420p -crf {3} -preset {4} -fps_mode cfr -movflags +faststart -bf 0 \"{5}\"",
                        finalOutputFPS,
                        inputPattern,
                        durationToken,
                        finalCRF,
                        finalPreset,
                        finalVideoPath
                    );
                }
                else
                {
                    ffmpegCommand = string.Format(
                        "-y -framerate {0} -i \"{1}\" -t {2} -c:v libx264 -pix_fmt yuv420p -crf {3} -preset {4} -vsync 0 \"{5}\"",
                        finalOutputFPS,
                        inputPattern,
                        durationToken,
                        finalCRF,
                        finalPreset,
                        finalVideoPath
                    );
                }
            }
            Debug.LogError($"[Offline47] Encode path: {offlineFfmpegEncodePath}");
            Debug.LogError("[Offline47] Running ffmpeg: " + ffmpegCommand);
            bool generationSucceeded = false;
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
                        Debug.LogError("[Offline47] ffmpeg completed successfully.");
                        generationSucceeded = true;
                    }
                    else
                    {
                        Debug.LogError("[Offline47] ffmpeg failed with exit code: " + process.ExitCode);
                        Debug.LogError("[Offline47] ffmpeg output:\n" + output);
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError("[Offline47] EXCEPTION while running ffmpeg:");
                Debug.LogError("[Offline47] Message: " + e.Message);
                Debug.LogError("[Offline47] Stack trace:\n" + e.ToString());
            }
            // === Final result reporting ===
            if (generationSucceeded)
            {
                Debug.LogError("[CameraPathCaptureRig] Offline47: Final video generation completed successfully.");
                if (File.Exists(finalVideoPath))
                {
                    Debug.LogError("[Offline47] Final video file created: " + finalVideoPath);
                    Debug.LogError("[Offline47] Audio muxed: " + (muxAudio ? "yes" : "no"));
                    if (muxAudio && silencePadAudio)
                    {
                        Debug.LogError(
                            $"[Offline47] Audio pad silenced after {contentEndSeconds:F6}s " +
                            $"(remainder of {outputDurationSeconds:F6}s is silent).");
                    }
                    Debug.LogError($"[Offline47] Requested output duration cap: {outputDurationSeconds:F6}s");
                    // Automated duration check (ffprobe). Does not fail the job if probe is missing.
                    VerifyOffline47OutputDuration(finalVideoPath, outputDurationSeconds, finalOutputFPS);
                }
                else
                {
                    Debug.LogError("[Offline47] WARNING: Expected final video not found at: " + finalVideoPath);
                }
            }
            else
            {
                Debug.LogError("[CameraPathCaptureRig] Offline47: Final video generation FAILED.");
            }
        }

        /// <summary>
        /// Reads container duration via ffprobe and compares to the offline job target T.
        /// Logs PASS / WARNING / SKIP. Does not throw; encode success is unchanged.
        /// </summary>
        private void VerifyOffline47OutputDuration(string finalVideoPath, float expectedDurationSeconds, int outputFps)
        {
            if (string.IsNullOrEmpty(finalVideoPath) || !File.Exists(finalVideoPath))
            {
                Debug.LogError("[Offline47] Duration check SKIP: final video path missing.");
                return;
            }

            if (expectedDurationSeconds <= 0f)
            {
                Debug.LogError("[Offline47] Duration check SKIP: expected duration is invalid.");
                return;
            }

            float toleranceSeconds = (outputFps > 0) ? (2.0f / (float)outputFps) : 0.05f;
            if (toleranceSeconds < 0.01f)
            {
                toleranceSeconds = 0.01f;
            }

            string probeArgs =
                "-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"" +
                finalVideoPath + "\"";

            try
            {
                System.Diagnostics.ProcessStartInfo probeInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ffprobe",
                    Arguments = probeArgs,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (System.Diagnostics.Process probe = System.Diagnostics.Process.Start(probeInfo))
                {
                    string stdout = probe.StandardOutput.ReadToEnd();
                    string stderr = probe.StandardError.ReadToEnd();
                    probe.WaitForExit();

                    if (probe.ExitCode != 0)
                    {
                        Debug.LogError(
                            "[Offline47] Duration check SKIP: ffprobe exit code " + probe.ExitCode +
                            (string.IsNullOrEmpty(stderr) ? "" : (" | " + stderr.Trim())));
                        return;
                    }

                    string token = (stdout ?? "").Trim();
                    if (string.IsNullOrEmpty(token))
                    {
                        Debug.LogError("[Offline47] Duration check SKIP: ffprobe returned empty duration.");
                        return;
                    }

                    // ffprobe may print one line; take the first token.
                    int lineBreak = token.IndexOfAny(new char[] { '\r', '\n' });
                    if (lineBreak >= 0)
                    {
                        token = token.Substring(0, lineBreak).Trim();
                    }

                    double actualDuration;
                    if (!double.TryParse(
                            token,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out actualDuration))
                    {
                        Debug.LogError("[Offline47] Duration check SKIP: could not parse duration '" + token + "'.");
                        return;
                    }

                    double delta = System.Math.Abs(actualDuration - (double)expectedDurationSeconds);
                    if (delta <= (double)toleranceSeconds)
                    {
                        Debug.LogError(
                            $"[Offline47] Duration check PASS: expected {expectedDurationSeconds:F6}s, " +
                            $"actual {actualDuration:F6}s (delta={delta:F6}s, tolerance={toleranceSeconds:F6}s).");
                    }
                    else
                    {
                        Debug.LogError(
                            $"[Offline47] Duration check WARNING: expected {expectedDurationSeconds:F6}s, " +
                            $"actual {actualDuration:F6}s (delta={delta:F6}s, tolerance={toleranceSeconds:F6}s). " +
                            "Container length may not match job T.");
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError("[Offline47] Duration check SKIP: ffprobe exception: " + e.Message);
            }
        }





        /// <summary>
        /// Offline50 - Final cleanup after second pass completes.
        /// Resets all offline state so the system is clean for future renders.
        /// </summary>
        /// <summary>
        /// Offline50 - Final cleanup after second pass completes.
        /// Resets all offline state so the system is clean for future renders.
        /// </summary>
        private void Offline50_Cleanup()
        {
            Debug.LogError("[CameraPathCaptureRig] Offline50: Starting cleanup.");

            // === NEW: Compile parameters to disk before memory registers are wiped ===
            WriteOfflineRenderMetadataPassport();

            VideoRecorderUtils.SetTargetFrameCount(-1);

            // Optional: Delete first pass folder during testing
            if (m_DeleteFirstPassFolder && !string.IsNullOrEmpty(m_OfflineFirstPassFolderPath))
            {
                try
                {
                    if (System.IO.Directory.Exists(m_OfflineFirstPassFolderPath))
                    {
                        System.IO.Directory.Delete(m_OfflineFirstPassFolderPath, true);
                        Debug.LogError($"[Offline50] Deleted first pass folder: {m_OfflineFirstPassFolderPath}");
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError("[Offline50] Failed to delete first pass folder.");
                    Debug.LogError("[Offline50] Exception message: " + e.Message);
                    Debug.LogError("[Offline50] Stack trace:\n" + e.ToString());
                }
            }

            // Reset all offline state
            m_OfflineFirstPassFrameCount = 0;
            m_OfflineFirstPassFolderPath = "";
            m_OfflineTimeScale = 1.0f;
            m_OfflineTargetFrameCount = 0;
            m_OfflineUsdSerializer = null;
            m_OfflineVideoRecorder = null;
            m_OfflineOriginalFilePath = "";

            // === NEW: Clean up dedicated second-pass serializer ===
            m_SecondPassUsdSerializer = null;

            Debug.LogError("[CameraPathCaptureRig] Offline50: Cleanup complete. All offline state has been reset.");

            // === NEW: Cascade execution down to the shutdown sequence ===
            Offline52_ShutdownHeadlessProcess();
        }



        /// <summary>
        /// Offline52 - Headless automation process termination sequence.
        /// Safely severs the system thread and returns control to the batch shell.
        /// </summary>
        private void Offline52_ShutdownHeadlessProcess()
        {
            Debug.LogError("[CameraPathCaptureRig] Offline52: Evaluating headless loop termination variables.");

            if (App.Config != null && App.Config.OfflineRender)
            {
                Debug.LogError("[CameraPathCaptureRig] Offline52: Task complete. Executing Application.Quit().");

                // Cleanly shuts down the standalone project or exits play mode in the Unity editor
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }
            else
            {
                Debug.LogError("[CameraPathCaptureRig] Offline52: Not in headless mode. Preserving editor runtime.");
            }
        }


        /// <summary>
        /// Compiles active headless variables and writes twin metadata files (JSON + TXT)
        /// right before the offline cleanup process wipes the active memory states.
        /// </summary>

        // =================================================================
        // INSPECTOR-ALIGNED HEADLESS METADATA STRUCTURES (PART 1 OF 3)
        // =================================================================
        [System.Serializable]
        private class OfflineMetadataPassport
        {
            public string timestamp;
            public string renderModeContext;
            public CameraPathRigInspectorData cameraRigSettings;
            public AudioDropdownInspectorData audioModeSettings;
            public VisualizerManagerInspectorData audioTuningSettings;
        }

        [System.Serializable]
        private class CameraPathRigInspectorData
        {
            public string outputFilePath;
            public float targetDurationSeconds;
            public bool useFrameLimit;
            public int secondPassTraversalPercent;
            public bool m_WritePaddingAsPureBlack;
            public int videoQualityCRF;
            public string videoQualityPreset;
            public int liveCaptureFPS;
            public int liveOutputFramerate;
            public int offlineCaptureFPS;
            public int offlineOutputFPS;
            public int captureWidthOverride;
            public int captureHeightOverride;
            public int offlineVideoQualityCRF;
            public string offlineVideoQualityPreset;
            public int finalTargetFrameCount;
            public float currentSpeedMultiplier;
        }

        [System.Serializable]
        private class AudioDropdownInspectorData
        {
            public string activeOfflineMode;
            public float m_ReactivityStrength;
            public float m_SimulatedBPM;
        }

        [System.Serializable]
        private class VisualizerManagerInspectorData
        {
            public float m_BandPeakDecay;
            public float m_NormalizedBandPeakLerp;
            public float m_FFTPeakDecay;
            public float m_FFTScale;
            public float m_FFTPowerScale;
            public float m_FFTPower;
            public float m_WeirdWaveformLerp;
            public double m_HighPassFreq;
            public double m_LowPassFreq;
            public float m_MaxReactivityStrength;
            public float m_BeatSharpness;
            public float m_AmplitudeModAmount;
            public float m_FftReactivityMultiplier;
            public float m_BeatAccumScale;
            public float m_VolumeMultiplier;
            public float m_BandLowMultiplier;
            public float m_BandLowMidMultiplier;
            public float m_BandMidMultiplier;
            public float m_BandHighMidMultiplier;
            public float m_BeatPulseWidth;
            public float m_AccumLerpSpeed;
            public float m_FftChannelR_Strength;
            public float m_FftChannelG_Strength;
            public float m_FftChannelB_Strength;
            public float m_FftChannelA_Strength;
            public float m_FftBlueChannelSpikeSharpness;
            public float m_FftBlueChannelFrequency;
            public float m_WaveformChannelR_Strength;
            public float m_WaveformChannelG_Strength;
            public float m_WaveformChannelB_Strength;
            public float m_WaveformChannelA_Strength;
        }

        // =================================================================
        // CORE COMPILER & PARAMETER MAPPING (PART 2 OF 3)
        // =================================================================
        private void WriteOfflineRenderMetadataPassport()
        {
            string targetUsdPath = App.Config.m_VideoPathToRender;
            if (string.IsNullOrEmpty(targetUsdPath))
            {
                Debug.LogError("[Offline Passport] Cannot generate metadata sheet: m_VideoPathToRender is empty.");
                return;
            }

            try
            {
                string directory = System.IO.Path.GetDirectoryName(targetUsdPath);
                string baseName = System.IO.Path.GetFileNameWithoutExtension(targetUsdPath);
                string jsonOutputPath = System.IO.Path.Combine(directory, baseName + "_metadata.json");
                string txtOutputPath = System.IO.Path.Combine(directory, baseName + "_metadata.txt");

                var audioMgr = AudioCaptureManager.m_Instance;
                var visMgr = VisualizerManager.m_Instance;

                // Safe reflection mapping loops for reading private fields
                System.Reflection.FieldInfo GetVisField(string name) =>
                    typeof(VisualizerManager).GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                float FetchFloat(string name, float defaultVal) =>
                    (visMgr != null && GetVisField(name) != null) ? (float)GetVisField(name).GetValue(visMgr) : defaultVal;

                double FetchDouble(string name, double defaultVal) =>
                    (visMgr != null && GetVisField(name) != null) ? (double)GetVisField(name).GetValue(visMgr) : defaultVal;

                OfflineMetadataPassport passport = new OfflineMetadataPassport
                {
                    timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    renderModeContext = "Headless Command-Line Run",

                    // 1. Group One: CameraPathCaptureRig Properties
                    cameraRigSettings = new CameraPathRigInspectorData
                    {
                        outputFilePath = m_OfflineOriginalFilePath,
                        targetDurationSeconds = targetDurationSeconds,
                        useFrameLimit = useFrameLimit,
                        secondPassTraversalPercent = secondPassTraversalPercent,
                        m_WritePaddingAsPureBlack = m_WritePaddingAsPureBlack,
                        videoQualityCRF = videoQualityCRF,
                        videoQualityPreset = videoQualityPreset,
                        liveCaptureFPS = liveCaptureFPS,
                        liveOutputFramerate = liveOutputFramerate,
                        offlineCaptureFPS = offlineCaptureFPS,
                        offlineOutputFPS = offlineOutputFPS,
                        captureWidthOverride = captureWidthOverride,
                        captureHeightOverride = captureHeightOverride,
                        offlineVideoQualityCRF = offlineVideoQualityCRF,
                        offlineVideoQualityPreset = offlineVideoQualityPreset,
                        finalTargetFrameCount = m_OfflineTargetFrameCount,
                        currentSpeedMultiplier = m_OfflineTimeScale
                    },



                    audioModeSettings = new AudioDropdownInspectorData
                    {
                        activeOfflineMode = audioMgr != null ? audioMgr.m_OfflineAudioMode.ToString() : "Unknown",
                        m_ReactivityStrength = audioMgr != null ? audioMgr.m_ReactivityStrength : 1.0f,
                        m_SimulatedBPM = audioMgr != null ? audioMgr.m_SimulatedBPM : 128f
                    },

                    audioTuningSettings = new VisualizerManagerInspectorData
                    {
                        m_BandPeakDecay = FetchFloat("m_BandPeakDecay", 0.9f),
                        m_NormalizedBandPeakLerp = FetchFloat("m_NormalizedBandPeakLerp", 0.9f),
                        m_FFTPeakDecay = FetchFloat("m_FFTPeakDecay", 0.9f),
                        m_FFTScale = FetchFloat("m_FFTScale", 2.0f),
                        m_FFTPowerScale = FetchFloat("m_FFTPowerScale", 2.0f),
                        m_FFTPower = FetchFloat("m_FFTPower", 1.5f),
                        m_WeirdWaveformLerp = FetchFloat("m_WeirdWaveformLerp", 0.7f),
                        m_HighPassFreq = FetchDouble("m_HighPassFreq", 2000),
                        m_LowPassFreq = FetchDouble("m_LowPassFreq", 150),
                        m_MaxReactivityStrength = FetchFloat("m_MaxReactivityStrength", 10f),
                        m_BeatSharpness = FetchFloat("m_BeatSharpness", 5.0f),
                        m_AmplitudeModAmount = FetchFloat("m_AmplitudeModAmount", 0.5f),
                        m_FftReactivityMultiplier = FetchFloat("m_FftReactivityMultiplier", 1.0f),
                        m_BeatAccumScale = FetchFloat("m_BeatAccumScale", 0.08f),
                        m_VolumeMultiplier = FetchFloat("m_VolumeMultiplier", 1.0f),
                        m_BandLowMultiplier = FetchFloat("m_BandLowMultiplier", 1.0f),
                        m_BandLowMidMultiplier = FetchFloat("m_BandLowMidMultiplier", 0.90f),
                        m_BandMidMultiplier = FetchFloat("m_BandMidMultiplier", 0.55f),
                        m_BandHighMidMultiplier = FetchFloat("m_BandHighMidMultiplier", 0.30f),
                        m_BeatPulseWidth = FetchFloat("m_BeatPulseWidth", 1.0f),
                        m_AccumLerpSpeed = FetchFloat("m_AccumLerpSpeed", 0.08f),
                        m_FftChannelR_Strength = FetchFloat("m_FftChannelR_Strength", 1.0f),
                        m_FftChannelG_Strength = FetchFloat("m_FftChannelG_Strength", 0.9f),
                        m_FftChannelB_Strength = FetchFloat("m_FftChannelB_Strength", 2.2f),
                        m_FftChannelA_Strength = FetchFloat("m_FftChannelA_Strength", 1.0f),
                        m_FftBlueChannelSpikeSharpness = FetchFloat("m_FftBlueChannelSpikeSharpness", 9.0f),
                        m_FftBlueChannelFrequency = FetchFloat("m_FftBlueChannelFrequency", 5.5f),
                        m_WaveformChannelR_Strength = FetchFloat("m_WaveformChannelR_Strength", 1.0f),
                        m_WaveformChannelG_Strength = FetchFloat("m_WaveformChannelG_Strength", 1.0f),
                        m_WaveformChannelB_Strength = FetchFloat("m_WaveformChannelB_Strength", 1.0f),
                        m_WaveformChannelA_Strength = FetchFloat("m_WaveformChannelA_Strength", 1.0f)
                    }
                };

                // Write Clean JSON Data Sheet
                string jsonString = JsonUtility.ToJson(passport, true);
                System.IO.File.WriteAllText(jsonOutputPath, jsonString);


                // =================================================================
                // TEXT BUILDER & SYSTEM WRITE OPERATIONS (PART 3 OF 3)
                // =================================================================
                System.Text.StringBuilder txt = new System.Text.StringBuilder();
                txt.AppendLine("=========================================================================");
                txt.AppendLine("         OPEN BRUSH PRODUCTION RUN PASSPORT (ALIGNED STATUS)             ");
                txt.AppendLine("=========================================================================");
                txt.AppendLine($"Render Timestamp : {passport.timestamp}");
                txt.AppendLine($"Execution Context: {passport.renderModeContext}");
                txt.AppendLine($"Destination Path : {passport.cameraRigSettings.outputFilePath}");
                txt.AppendLine("-------------------------------------------------------------------------");
                txt.AppendLine("1. CAMERAPATHCAPTURERIG COMPONENT PANEL:");
                txt.AppendLine($"   - Target Duration Seconds     : {passport.cameraRigSettings.targetDurationSeconds:F2}s");
                txt.AppendLine($"   - Use Frame Limit             : {passport.cameraRigSettings.useFrameLimit}");
                txt.AppendLine($"   - Second Pass Traversal %     : {passport.cameraRigSettings.secondPassTraversalPercent}%");
                txt.AppendLine($"   - Write Padding As Pure Black : {passport.cameraRigSettings.m_WritePaddingAsPureBlack}");
                txt.AppendLine($"   - Video Quality CRF           : {passport.cameraRigSettings.videoQualityCRF}");
                txt.AppendLine($"   - Video Quality Preset        : {passport.cameraRigSettings.videoQualityPreset}");
                txt.AppendLine($"   - Live Capture FPS            : {passport.cameraRigSettings.liveCaptureFPS}");
                txt.AppendLine($"   - Live Output Framerate       : {passport.cameraRigSettings.liveOutputFramerate}");
                txt.AppendLine($"   - Offline Capture FPS         : {passport.cameraRigSettings.offlineCaptureFPS}");
                txt.AppendLine($"   - Offline Output FPS          : {passport.cameraRigSettings.offlineOutputFPS}");
                txt.AppendLine($"   - Capture Width Override      : {passport.cameraRigSettings.captureWidthOverride}px");
                txt.AppendLine($"   - Capture Height Override     : {passport.cameraRigSettings.captureHeightOverride}px");
                txt.AppendLine($"   - Offline Video Quality CRF   : {passport.cameraRigSettings.offlineVideoQualityCRF}");
                txt.AppendLine($"   - Offline Video Quality Preset: {passport.cameraRigSettings.offlineVideoQualityPreset}");
                txt.AppendLine($"   - ACTUAL RENDERED FRAMES      : {passport.cameraRigSettings.finalTargetFrameCount}");
                txt.AppendLine($"   - DERIVED TIME EXPANSION SCALE: {passport.cameraRigSettings.currentSpeedMultiplier:F4}x Speed");
                txt.AppendLine("-------------------------------------------------------------------------");
                txt.AppendLine("2. AUDIOCAPTUREMANAGER COMPONENT PANEL:");
                txt.AppendLine($"   - Two-Pass Offline Config Mode: {passport.audioModeSettings.activeOfflineMode}");
                txt.AppendLine($"   - Reactivity Strength (Main)  : {passport.audioModeSettings.m_ReactivityStrength:F2}");
                txt.AppendLine($"   - Simulated BPM               : {passport.audioModeSettings.m_SimulatedBPM} BPM");
                txt.AppendLine("-------------------------------------------------------------------------");
                txt.AppendLine("3. VISUALIZERMANAGER COMPONENT PANEL (SHADERS TUNING MATRIX):");
                txt.AppendLine("   [Band Levels]");
                txt.AppendLine($"   - Band Peak Decay             : {passport.audioTuningSettings.m_BandPeakDecay:F2}");
                txt.AppendLine($"   - Normalized Band Peak Lerp   : {passport.audioTuningSettings.m_NormalizedBandPeakLerp:F2}");
                txt.AppendLine("   [FFT]");
                txt.AppendLine($"   - FFT Peak Decay              : {passport.audioTuningSettings.m_FFTPeakDecay:F2}");
                txt.AppendLine($"   - FFT Scale                   : {passport.audioTuningSettings.m_FFTScale:F2}");
                txt.AppendLine($"   - FFT Power Scale             : {passport.audioTuningSettings.m_FFTPowerScale:F2}");
                txt.AppendLine($"   - FFT Power                   : {passport.audioTuningSettings.m_FFTPower:F2}");
                txt.AppendLine("   [Waveform]");
                txt.AppendLine($"   - Weird Waveform Lerp         : {passport.audioTuningSettings.m_WeirdWaveformLerp:F2}");
                txt.AppendLine($"   - High Pass Freq              : {passport.audioTuningSettings.m_HighPassFreq:F0} Hz");
                txt.AppendLine($"   - Low Pass Freq               : {passport.audioTuningSettings.m_LowPassFreq:F0} Hz");
                txt.AppendLine("   [Simulated BPM - Reactivity Tuning]");
                txt.AppendLine($"   - Max Reactivity Safety Limit : {passport.audioTuningSettings.m_MaxReactivityStrength:F2}");
                txt.AppendLine($"   - Beat Sharpness (Punch)      : {passport.audioTuningSettings.m_BeatSharpness:F2}");
                txt.AppendLine($"   - Amplitude Mod Amount (Wobble): {passport.audioTuningSettings.m_AmplitudeModAmount:F2}");
                txt.AppendLine($"   - FFT Reactivity Multiplier   : {passport.audioTuningSettings.m_FftReactivityMultiplier:F2}");
                txt.AppendLine($"   - Beat Accum Scale            : {passport.audioTuningSettings.m_BeatAccumScale:F4}");
                txt.AppendLine($"   - Volume Multiplier (Loudness): {passport.audioTuningSettings.m_VolumeMultiplier:F2}");
                txt.AppendLine("   [Simulated Band Levels (Techno Tuning)]");
                txt.AppendLine($"   - Band Low Multiplier (Bass)  : {passport.audioTuningSettings.m_BandLowMultiplier:F2}");
                txt.AppendLine($"   - Band Low-Mid Multiplier     : {passport.audioTuningSettings.m_BandLowMidMultiplier:F2}");
                txt.AppendLine($"   - Band Mid Multiplier         : {passport.audioTuningSettings.m_BandMidMultiplier:F2}");
                txt.AppendLine($"   - Band High-Mid Multiplier    : {passport.audioTuningSettings.m_BandHighMidMultiplier:F2}");
                txt.AppendLine("   [Simulated BPM - Beat Pulse]");
                txt.AppendLine($"   - Beat Pulse Width            : {passport.audioTuningSettings.m_BeatPulseWidth:F2}");
                txt.AppendLine("   [Simulated BPM - Accumulation]");
                txt.AppendLine($"   - Accum Lerp Speed            : {passport.audioTuningSettings.m_AccumLerpSpeed:F4}");
                txt.AppendLine("   [Simulated FFT Texture - Channel Strengths]");
                txt.AppendLine($"   - FFT Channel R Strength      : {passport.audioTuningSettings.m_FftChannelR_Strength:F2}");
                txt.AppendLine($"   - FFT Channel G Strength      : {passport.audioTuningSettings.m_FftChannelG_Strength:F2}");
                txt.AppendLine($"   - FFT Channel B Strength      : {passport.audioTuningSettings.m_FftChannelB_Strength:F2}");
                txt.AppendLine($"   - FFT Channel A Strength      : {passport.audioTuningSettings.m_FftChannelA_Strength:F2}");
                txt.AppendLine("   [Simulated FFT - Blue Channel Behavior (WaveformFFT)]");
                txt.AppendLine($"   - FFT Blue Spike Sharpness    : {passport.audioTuningSettings.m_FftBlueChannelSpikeSharpness:F2}");
                txt.AppendLine($"   - FFT Blue Channel Frequency  : {passport.audioTuningSettings.m_FftBlueChannelFrequency:F2}");
                txt.AppendLine("   [Simulated Waveform Texture - Channel Strengths]");
                txt.AppendLine($"   - Waveform Channel R Strength : {passport.audioTuningSettings.m_WaveformChannelR_Strength:F2}");
                txt.AppendLine($"   - Waveform Channel G Strength : {passport.audioTuningSettings.m_WaveformChannelG_Strength:F2}");
                txt.AppendLine($"   - Waveform Channel B Strength : {passport.audioTuningSettings.m_WaveformChannelB_Strength:F2}");
                txt.AppendLine($"   - Waveform Channel A Strength : {passport.audioTuningSettings.m_WaveformChannelA_Strength:F2}");
                txt.AppendLine("=========================================================================");

                System.IO.File.WriteAllText(txtOutputPath, txt.ToString());
                Debug.LogError($"[Offline Passport] Fully detailed passports written successfully to: {directory}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Offline Passport] ERROR generating deep metadata passport: {e.Message}");
            }
        }





    } // class complete


} // End of script



