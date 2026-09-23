#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TiltBrush
{
    /// <summary>
    /// Sequential offline batch for the two-pass path.
    /// Fixed sizes: set CameraPathCaptureRig captureWidth/Height each job, then BASIC fake CLI + Play.
    /// All (three sizes): overrides are rewritten per job; original inspector values restored only at end/stop.
    /// Custom: do not touch rig overrides.
    /// Fake command line is cleared after every job and at batch end so the next ordinary Play
    /// session cannot re-trigger an offline render from a stale path.
    /// </summary>
    public class OfflineBatchRunner : EditorWindow
    {
        private enum QualityMode
        {
            FullHD_1080 = 0, // 1920 x 1080
            QuadHD_1440 = 1, // 2560 x 1440
            UHD_4K = 2, // 3840 x 2160
            AllThree = 3,
            Custom = 4
        }

        private QualityMode m_Quality = QualityMode.FullHD_1080;
        private Vector2 m_Scroll;
        private readonly List<string> m_LogLines = new List<string>();
        private readonly List<BatchJob> m_Queue = new List<BatchJob>();
        private int m_QueueIndex = -1;
        private bool m_Running;
        private bool m_WaitingForPlayExit;

        // Original inspector overrides (saved once, restored when batch ends or stops)
        private bool m_HasSavedOverrides;
        private int m_SavedWidthOverride;
        private int m_SavedHeightOverride;

        private static string JobsFolder =>
            Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments),
                "Open Brush", "VideosToProcess");

        private static string SketchesFolder =>
            Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments),
                "Open Brush", "Sketches");

        // Used for ensuring batch runner doesnt start before play is active
        private bool m_PlayEnteredForCurrentJob;
        private float m_PlayStartDeadline;
        private float m_NextPlayStartRetry;
        private int m_PlayStartAttemptIndex;
        private string m_LastPlayStartStrategy = "";
        private const float kPlayStartRetryInterval = 1.0f;
        private const float kPlayStartTimeoutSeconds = 300f;

        private struct BatchJob
        {
            public string UsdaPath;
            public string TiltPath;
            public string Stem;
            public string Label;
            public int Width;  // 0 = Custom (do not change rig)
            public int Height; // 0 = Custom (do not change rig)
        }

        [MenuItem("Advanced/Offline Batch Runner")]
        public static void OpenWindow()
        {
            var w = GetWindow<OfflineBatchRunner>("Offline Batch");
            w.minSize = new Vector2(420, 380);
        }
        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= OnEditorUpdate;
            m_Running = false;
            m_WaitingForPlayExit = false;
            m_PlayEnteredForCurrentJob = false;
        }

        /// <summary>
        /// While waiting for a job:
        /// - If Play has not started yet, retry enter-Play on an interval until success or timeout.
        /// - If Play was observed and then ended, finish the job (focus-loss exit path).
        /// Never treats "Play not started yet" as "job finished."
        /// </summary>
        private void OnEditorUpdate()
        {
            if (!m_WaitingForPlayExit)
            {
                return;
            }

            double now = EditorApplication.timeSinceStartup;

            if (EditorApplication.isPlaying)
            {
                if (!m_PlayEnteredForCurrentJob)
                {
                    m_PlayEnteredForCurrentJob = true;
                    AddBatchLine(
                        $"Play entered for job {m_QueueIndex + 1}/{m_Queue.Count} " +
                        $"(last strategy: {m_LastPlayStartStrategy})");
                }
                return;
            }

            if (!m_PlayEnteredForCurrentJob)
            {
                if (now >= m_PlayStartDeadline)
                {
                    AddBatchLine(
                        $"ERROR: Play failed to start within {kPlayStartTimeoutSeconds:F0}s " +
                        $"for job {m_QueueIndex + 1}/{m_Queue.Count} " +
                        $"(last strategy: {m_LastPlayStartStrategy}). Stopping batch.");
                    m_WaitingForPlayExit = false;
                    m_PlayEnteredForCurrentJob = false;
                    m_Running = false;
                    ClearFakeCommandLine();
                    RestoreRigOverridesIfNeeded();
                    Repaint();
                    return;
                }

                if (now >= m_NextPlayStartRetry)
                {
                    m_NextPlayStartRetry = (float)now + kPlayStartRetryInterval;
                    TryRequestPlayStart();
                }
                return;
            }

            TryHandleJobExit("update-poll");
        }

        /// <summary>
        /// Writes a marker file so an external watcher can activate Unity only when a job has finished.
        /// </summary>
        private void WriteJobDoneMarker(int jobNumberOneBased)
        {
            try
            {
                string dir = Path.Combine(JobsFolder, "BatchTriggers");
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string path = Path.Combine(dir, $"job_{jobNumberOneBased}_done.trigger");
                File.WriteAllText(path, $"done {DateTime.Now:O}");
                AddBatchLine($"Wrote job-done marker: {path}");
            }
            catch (Exception e)
            {
                AddBatchLine($"WARNING: Could not write job-done marker: {e.Message}");
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Open Brush - Offline Batch Runner", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Full HD / Quad HD / 4K UHD / All: set rig capture size each job, then fake CLI + Play.\n" +
                "All: size is updated for each of the three jobs; original overrides restored only when the batch ends.\n" +
                "Custom: leave rig overrides alone (inspector / Unity settings).\n" +
                "Offline52 must exit Play before the next job starts.\n" +
                "Fake command line is cleared after every job so ordinary Play cannot re-trigger offline render.",
                MessageType.Info);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Jobs folder", JobsFolder);
            EditorGUILayout.LabelField("Sketches folder", SketchesFolder);
            EditorGUILayout.Space(6);

            // Labels include type + pixels for at-a-glance reading
            string[] qualityLabels =
            {
                "Full HD (1920x1080)",
                "Quad HD (2560x1440)",
                "4K UHD (3840x2160)",
                "All (Full HD + Quad HD + 4K UHD)",
                "Custom (rig / Unity settings)"
            };
            m_Quality = (QualityMode)EditorGUILayout.Popup("Quality", (int)m_Quality, qualityLabels);

            EditorGUILayout.Space(8);

            using (new EditorGUI.DisabledScope(m_Running || EditorApplication.isPlaying))
            {
                if (GUILayout.Button("Start Batch", GUILayout.Height(32)))
                {
                    StartBatch();
                }
            }

            using (new EditorGUI.DisabledScope(!m_Running))
            {
                if (GUILayout.Button("Stop after current job", GUILayout.Height(24)))
                {
                    AddBatchLine("Stop requested - will halt after current job exits Play.");
                    m_Running = false;
                }
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(
                m_Running
                    ? $"Running ({Mathf.Max(0, m_QueueIndex) + 1}/{m_Queue.Count})"
                    : "Idle",
                EditorStyles.boldLabel);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Log", EditorStyles.boldLabel);
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll, GUILayout.ExpandHeight(true));
            foreach (var line in m_LogLines)
            {
                EditorGUILayout.LabelField(line, EditorStyles.wordWrappedLabel);
            }
            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// Requests Play using one strategy per call so the log can show which
        /// mechanism was in use when isPlaying finally became true.
        /// Strategies rotate: isPlaying only, Focus+isPlaying, Edit/Play menu.
        /// Skips entirely if Play is already running. Menu item is only invoked
        /// when still not playing so a toggle cannot stop Play.
        /// </summary>
        private void TryRequestPlayStart()
        {
            if (EditorApplication.isPlaying)
            {
                return;
            }

            int strategy = m_PlayStartAttemptIndex % 3;
            m_PlayStartAttemptIndex++;

            switch (strategy)
            {
                case 0:
                    m_LastPlayStartStrategy = "isPlaying=true";
                    EditorApplication.isPlaying = true;
                    break;

                case 1:
                    m_LastPlayStartStrategy = "Focus+isPlaying=true";
                    Focus();
                    EditorApplication.isPlaying = true;
                    break;

                default:
                    m_LastPlayStartStrategy = "Edit/Play menu";
                    Focus();
                    if (!EditorApplication.isPlaying)
                    {
                        EditorApplication.ExecuteMenuItem("Edit/Play");
                    }
                    else
                    {
                        m_LastPlayStartStrategy = "Edit/Play menu (skipped, already playing)";
                    }
                    break;
            }

            AddBatchLine($"Play start retry #{m_PlayStartAttemptIndex} via {m_LastPlayStartStrategy}");
        }

        /// <summary>
        /// Finishes the current job exactly once when Play has ended.
        /// Called from playModeStateChanged (EnteredEditMode) and from the
        /// EditorApplication.update poll. Flips m_WaitingForPlayExit before any
        /// delayCall so a second trigger is ignored and logged.
        /// </summary>
        /// <summary>
        /// Finishes the current job exactly once when Play has ended.
        /// Called from playModeStateChanged (EnteredEditMode) and from the
        /// EditorApplication.update poll. Flips m_WaitingForPlayExit before any
        /// delayCall so a second trigger is ignored and logged.
        /// </summary>

        private void TryHandleJobExit(string triggerSource)
        {
            if (!m_WaitingForPlayExit)
            {
                AddBatchLine(
                    $"Exit already handled (guard) - ignoring duplicate trigger from {triggerSource}");
                return;
            }

            m_WaitingForPlayExit = false;
            m_PlayEnteredForCurrentJob = false;

            AddBatchLine(
                $" session finished ({m_QueueIndex + 1}/{m_Queue.Count}) [{triggerSource}]");

            ClearFakeCommandLine();
            WriteJobDoneMarker(m_QueueIndex + 1);

            EditorApplication.delayCall += () =>
            {
                if (m_Running)
                {
                    BeginNextJob();
                }
                else
                {
                    ClearFakeCommandLine();
                    RestoreRigOverridesIfNeeded();
                    AddBatchLine("Batch stopped.");
                }
                Repaint();
            };
        }

        /*
        private void TryHandleJobExit(string triggerSource)
        {
            if (!m_WaitingForPlayExit)
            {
                AddBatchLine(
                    $"Exit already handled (guard) - ignoring duplicate trigger from {triggerSource}");
                return;
            }

            // Guard first so a simultaneous event + poll cannot both schedule work.
            m_WaitingForPlayExit = false;
            m_PlayEnteredForCurrentJob = false;

            AddBatchLine(
                $" session finished ({m_QueueIndex + 1}/{m_Queue.Count}) [{triggerSource}]");

            ClearFakeCommandLine();

            EditorApplication.delayCall += () =>
            {
                if (m_Running)
                {
                    BeginNextJob();
                }
                else
                {
                    ClearFakeCommandLine();
                    RestoreRigOverridesIfNeeded();
                    AddBatchLine("Batch stopped.");
                }
                Repaint();
            };
        }
        */

        private void StartBatch()
        {
            m_LogLines.Clear();
            m_Queue.Clear();
            m_QueueIndex = -1;
            m_WaitingForPlayExit = false;
            m_HasSavedOverrides = false;

            // Remove any leftover fake CLI from a previous batch/run so ordinary Play is safe.
            ClearFakeCommandLine();

            if (!Directory.Exists(JobsFolder))
            {
                AddBatchLine($"ERROR: Jobs folder not found:\n{JobsFolder}");
                return;
            }

            if (!Directory.Exists(SketchesFolder))
            {
                AddBatchLine($"ERROR: Sketches folder not found:\n{SketchesFolder}");
                return;
            }

            var usdas = Directory.GetFiles(JobsFolder, "*_01.usda", SearchOption.TopDirectoryOnly)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (usdas.Length == 0)
            {
                AddBatchLine($"No *_01.usda files in:\n{JobsFolder}");
                return;
            }

            foreach (var usda in usdas)
            {
                string stem = Path.GetFileNameWithoutExtension(usda);
                string tiltName = stem.EndsWith("_01", StringComparison.Ordinal)
                    ? stem.Substring(0, stem.Length - 3)
                    : stem;
                string tiltPath = Path.Combine(SketchesFolder, tiltName + ".tilt");

                if (!File.Exists(tiltPath))
                {
                    AddBatchLine($"[SKIP] {stem} - tilt not found: {tiltPath}");
                    continue;
                }

                switch (m_Quality)
                {
                    case QualityMode.FullHD_1080:
                        m_Queue.Add(MakeJob(usda, tiltPath, stem, "Full HD (1920x1080)", 1920, 1080));
                        break;
                    case QualityMode.QuadHD_1440:
                        m_Queue.Add(MakeJob(usda, tiltPath, stem, "Quad HD (2560x1440)", 2560, 1440));
                        break;
                    case QualityMode.UHD_4K:
                        m_Queue.Add(MakeJob(usda, tiltPath, stem, "4K UHD (3840x2160)", 3840, 2160));
                        break;
                    case QualityMode.AllThree:
                        m_Queue.Add(MakeJob(usda, tiltPath, stem, "Full HD (1920x1080)", 1920, 1080));
                        m_Queue.Add(MakeJob(usda, tiltPath, stem, "Quad HD (2560x1440)", 2560, 1440));
                        m_Queue.Add(MakeJob(usda, tiltPath, stem, "4K UHD (3840x2160)", 3840, 2160));
                        break;
                    case QualityMode.Custom:
                        m_Queue.Add(MakeJob(usda, tiltPath, stem, "Custom (rig / Unity settings)", 0, 0));
                        break;
                }
            }

            if (m_Queue.Count == 0)
            {
                AddBatchLine("No runnable jobs after tilt checks.");
                return;
            }

            AddBatchLine($"Queued {m_Queue.Count} play session(s) from {usdas.Length} USDA file(s).");
            m_Running = true;
            BeginNextJob();
        }

        private static BatchJob MakeJob(string usda, string tilt, string stem, string label, int width, int height)
        {
            return new BatchJob
            {
                UsdaPath = usda,
                TiltPath = tilt,
                Stem = stem,
                Label = label,
                Width = width,
                Height = height
            };
        }


        private void BeginNextJob()
        {
            if (!m_Running)
            {
                ClearFakeCommandLine();
                RestoreRigOverridesIfNeeded();
                AddBatchLine("Batch stopped.");
                return;
            }

            m_QueueIndex++;
            if (m_QueueIndex >= m_Queue.Count)
            {
                ClearFakeCommandLine();
                RestoreRigOverridesIfNeeded();
                m_Running = false;
                m_WaitingForPlayExit = false;
                m_PlayEnteredForCurrentJob = false;
                AddBatchLine("Batch complete.");
                Repaint();
                return;
            }

            var job = m_Queue[m_QueueIndex];

            if (job.Width > 0 && job.Height > 0)
            {
                if (!TrySetRigCaptureSize(job.Width, job.Height))
                {
                    AddBatchLine("ERROR: Could not set CameraPathCaptureRig capture size. Is the rig in the open scene?");
                    ClearFakeCommandLine();
                    m_Running = false;
                    return;
                }
                AddBatchLine($"[{job.Label}] rig capture set to {job.Width}x{job.Height}");
            }
            else
            {
                AddBatchLine($"[{job.Label}] leaving rig capture overrides unchanged");
            }

            string args = BuildFakeCommandLine(job);
            if (!TrySetFakeCommandLine(args))
            {
                AddBatchLine("ERROR: Could not set Config.m_FakeCommandLineArgsInEditor. Is Config in the open scene?");
                ClearFakeCommandLine();
                m_Running = false;
                return;
            }

            AddBatchLine($"[{job.Label}] {job.Stem}");
            AddBatchLine($" args: {args}");

            m_PlayEnteredForCurrentJob = false;
            m_WaitingForPlayExit = true;
            m_PlayStartAttemptIndex = 0;
            m_LastPlayStartStrategy = "initial isPlaying=true";
            m_PlayStartDeadline = (float)EditorApplication.timeSinceStartup + kPlayStartTimeoutSeconds;
            m_NextPlayStartRetry = (float)EditorApplication.timeSinceStartup + kPlayStartRetryInterval;

            EditorApplication.isPlaying = true;
            Repaint();
        }



        private static string BuildFakeCommandLine(BatchJob job)
        {
            // Fixed sizes: BASIC + FPS. Size comes from rig overrides set in BeginNextJob.
            if (job.Width > 0 && job.Height > 0)
            {
                return
                    $"--renderCameraPath \"{job.UsdaPath}\" " +
                    $"--Video.OfflineFPS 60 " +
                    $"\"{job.TiltPath}\"";
            }

            // Custom: path + tilt only - FPS and all other settings from the editor
            return
                $"--renderCameraPath \"{job.UsdaPath}\" " +
                $"\"{job.TiltPath}\"";
        }

        private bool TrySetRigCaptureSize(int width, int height)
        {
            var rig = UnityEngine.Object.FindObjectOfType<CameraPathCaptureRig>();
#if UNITY_2020_1_OR_NEWER
            if (rig == null)
            {
                rig = UnityEngine.Object.FindObjectOfType<CameraPathCaptureRig>(true);
            }
#endif
            if (rig == null)
            {
                return false;
            }

            // Save original once for the whole batch (All three keeps updating size per job)
            if (!m_HasSavedOverrides)
            {
                m_SavedWidthOverride = rig.captureWidthOverride;
                m_SavedHeightOverride = rig.captureHeightOverride;
                m_HasSavedOverrides = true;
                AddBatchLine($"Saved original rig overrides: {m_SavedWidthOverride}x{m_SavedHeightOverride}");
            }

            Undo.RecordObject(rig, "Batch Set Capture Size");
            rig.captureWidthOverride = width;
            rig.captureHeightOverride = height;
            EditorUtility.SetDirty(rig);
            return true;
        }

        private void RestoreRigOverridesIfNeeded()
        {
            if (!m_HasSavedOverrides)
            {
                return;
            }

            var rig = UnityEngine.Object.FindObjectOfType<CameraPathCaptureRig>();
#if UNITY_2020_1_OR_NEWER
            if (rig == null)
            {
                rig = UnityEngine.Object.FindObjectOfType<CameraPathCaptureRig>(true);
            }
#endif
            if (rig == null)
            {
                m_HasSavedOverrides = false;
                return;
            }

            Undo.RecordObject(rig, "Batch Restore Capture Size");
            rig.captureWidthOverride = m_SavedWidthOverride;
            rig.captureHeightOverride = m_SavedHeightOverride;
            EditorUtility.SetDirty(rig);
            AddBatchLine($"Restored rig capture overrides to {m_SavedWidthOverride}x{m_SavedHeightOverride}");
            m_HasSavedOverrides = false;
        }

        private bool TrySetFakeCommandLine(string args)
        {
            var config = UnityEngine.Object.FindObjectOfType<Config>();
#if UNITY_2020_1_OR_NEWER
            if (config == null)
            {
                config = UnityEngine.Object.FindObjectOfType<Config>(true);
            }
#endif
            if (config == null)
            {
                return false;
            }

            Undo.RecordObject(config, "Set Fake Command Line Args");
            config.m_FakeCommandLineArgsInEditor = args;
            EditorUtility.SetDirty(config);
            return true;
        }

        /// <summary>
        /// Clears the persisted fake command line so the next ordinary Play session
        /// cannot re-parse a stale --renderCameraPath and unexpectedly start offline render.
        /// Call after every job exit and when the batch ends or is stopped.
        /// Never restore a previous value - empty is the safe state.
        /// </summary>
        private void ClearFakeCommandLine()
        {
            var config = UnityEngine.Object.FindObjectOfType<Config>();
#if UNITY_2020_1_OR_NEWER
            if (config == null)
            {
                config = UnityEngine.Object.FindObjectOfType<Config>(true);
            }
#endif
            if (config == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(config.m_FakeCommandLineArgsInEditor))
            {
                return;
            }

            Undo.RecordObject(config, "Clear Fake Command Line Args");
            config.m_FakeCommandLineArgsInEditor = string.Empty;
            EditorUtility.SetDirty(config);
            AddBatchLine("Cleared Config.m_FakeCommandLineArgsInEditor");
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                if (m_WaitingForPlayExit)
                {
                    m_PlayEnteredForCurrentJob = true;
                    AddBatchLine(
                        $"Play entered for job {m_QueueIndex + 1}/{m_Queue.Count} " +
                        $"(playModeStateChanged, last strategy: {m_LastPlayStartStrategy})");
                }
                return;
            }

            if (!m_WaitingForPlayExit)
            {
                return;
            }

            if (state == PlayModeStateChange.EnteredEditMode)
            {
                TryHandleJobExit("playModeStateChanged");
            }
        }

        private void AddBatchLine(string msg)
        {
            string line = $"[{System.DateTime.Now:HH:mm:ss}] {msg}";
            m_LogLines.Add(line);
            Debug.Log("[OfflineBatch] " + msg);
            Repaint();
        }




    }
}
#endif