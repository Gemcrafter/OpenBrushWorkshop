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

namespace TiltBrush
{

    public class CameraPathPreviewWidget : GrabWidget
    {
        [SerializeField] private Color m_RecordColor;
        [SerializeField] private Color m_SecondPassColor = Color.green;
        // Update 2056 CameraPathPreviewWidget  Add Video Generation Color field
        [SerializeField] private Color m_VideoGenerationColor = Color.blue;


        private CameraPathWidget m_CurrentPathWidget;
        private Vector3? m_LastRecordedInputXf;
        private PathT m_PathT;
        private PathT m_PreviewStartPathT;

        // If not null, the widget will use this PathT instead of m_PathT.
        private PathT? m_OverridePathT;

        // Update 1004 CameraPathPreviewWidget  addtioanl timing tracking data
        private float m_PreviewStartTime;
        private bool m_PreviewStarted;

        public PathT? OverridePathT { set { m_OverridePathT = value; } }

        override protected void OnShow()
        {
            base.OnShow();

            // When we turn this widget on, it may not be in the right spot.  We need to refresh our
            // position here because it takes a frame to get updated and we'll get a 1-frame pop.
            CacheCurrentPathWidget();
            ValidatePathT();
            if (m_CurrentPathWidget != null && m_CurrentPathWidget.Path.NumPositionKnots > 1)
            {
                transform.position = m_CurrentPathWidget.Path.GetPosition(m_PathT);
                if (m_CurrentPathWidget.Path.RotationKnots.Count > 0)
                {
                    transform.rotation = m_CurrentPathWidget.Path.GetRotation(m_PathT);
                }
            }
        }



        // Update 2001 CameraPathPreviewWidget - Add second pass visual indicator support
        // Purpose: Allow the widget to show a distinct color (e.g. green or blue) during the second pass
        // of a two-pass recording session. This provides clear visual feedback to the user that they are
        // in the final recording pass. The method follows the same pattern as TintForRecording().
        //
        // Phase 2 pad behavior:
        // - Pass 1: geometric complete → StopRecordingPath (unchanged via IsRecordingActive).
        // - Pass 2: geometric complete → NotifySecondPassPathContentComplete only; do not stop.
        // - Once in pad region: skip MoveAlongPath and hold last PathT until frame budget ends.
        override protected void OnUpdate()
        {
            base.OnUpdate();

            CacheCurrentPathWidget();
            ValidatePathT();

            if (m_CurrentPathWidget != null && m_CurrentPathWidget.Path.NumPositionKnots > 1)
            {
                bool isRecording = VideoRecorderUtils.ActiveVideoRecording != null ||
                                   VideoRecorderUtils.ActiveStillFrameExporter != null;

                if (m_OverridePathT == null && !m_UserInteracting &&
                    WidgetManager.m_Instance.FollowingPath)
                {
                    var rig = SketchControlsScript.m_Instance != null
                        ? SketchControlsScript.m_Instance.CameraPathCaptureRig
                        : null;

                    bool inPadRegion = rig != null && rig.IsSecondPassInPadRegion;

                    if (!inPadRegion)
                    {
                        float speed = Mathf.Max(m_CurrentPathWidget.Path.GetSpeed(m_PathT),
                            CameraPathSpeedKnot.kMinSpeed);

                        bool completed = m_CurrentPathWidget.Path.MoveAlongPath(speed * Time.deltaTime,
                            m_PathT, out m_PathT);

                        // Preview-only duration capture (not recording)
                        if (!isRecording)
                        {
                            if (!m_PreviewStarted)
                            {
                                m_PreviewStartTime = Time.time;
                                m_PreviewStartPathT = m_PathT;
                                m_PreviewStarted = true;
                            }

                            if (completed && m_PreviewStarted)
                            {
                                if (m_PreviewStartPathT.T < 0.15f && m_CurrentPathWidget.Path.LastPreviewDuration == null)
                                {
                                    float actualDuration = Time.time - m_PreviewStartTime;
                                    m_CurrentPathWidget.Path.LastPreviewDuration = actualDuration;
                                }
                                m_PreviewStarted = false;
                            }
                        }

                        // ====================================================================
                        // PHASE-LOCKED TWO-PASS COMPLETION BRANCHING
                        // ====================================================================
                        // Pass 1 (IsRecordingActive): geometric complete stops the pass.
                        // Pass 2 (UseFrameLimit, not IsRecordingActive): enter pad region only;
                        // frame budget in SerializerNewUsdFrame still owns the real stop.
                        if (isRecording && completed)
                        {
                            if (rig != null && rig.UseFrameLimit)
                            {
                                if (rig.IsRecordingActive)
                                {
                                    rig.StopRecordingPath(true);
                                }
                                else
                                {
                                    rig.NotifySecondPassPathContentComplete();
                                }
                            }
                            else if (rig != null)
                            {
                                // No frame-limit / legacy: stop on geometric complete
                                rig.StopRecordingPath(true);
                            }
                        }
                    }
                    // else: Pass 2 pad region — hold last m_PathT; do not advance path
                }
                else
                {
                    m_PreviewStarted = false;
                    m_PreviewStartPathT = new PathT(0);
                }

                // Always snap transform / camera to current PathT (moving or held)
                PathT t = m_OverridePathT != null ? m_OverridePathT.Value : m_PathT;
                transform.position = m_CurrentPathWidget.Path.GetPosition(t);

                if (m_CurrentPathWidget.Path.RotationKnots.Count > 0)
                {
                    transform.rotation = m_CurrentPathWidget.Path.GetRotation(t);
                }

                float fov = m_CurrentPathWidget.Path.GetFov(t);
                SketchControlsScript.m_Instance.CameraPathCaptureRig.SetFov(fov);
                SketchControlsScript.m_Instance.CameraPathCaptureRig.UpdateCameraTransform(transform);
            }
        }

        /*
                override protected void OnUpdate()
                // Update 1005 CameraPathPreviewWidget.OnUpdate
                // cleans up stale timing functions
                {
                    base.OnUpdate();

                    CacheCurrentPathWidget();
                    ValidatePathT();

                    if (m_CurrentPathWidget != null && m_CurrentPathWidget.Path.NumPositionKnots > 1)
                    {
                        bool isRecording = VideoRecorderUtils.ActiveVideoRecording != null ||
                                           VideoRecorderUtils.ActiveStillFrameExporter != null;

                        if (m_OverridePathT == null && !m_UserInteracting &&
                            WidgetManager.m_Instance.FollowingPath)
                        {
                            float speed = Mathf.Max(m_CurrentPathWidget.Path.GetSpeed(m_PathT),
                                CameraPathSpeedKnot.kMinSpeed);

                            bool completed = m_CurrentPathWidget.Path.MoveAlongPath(speed * Time.deltaTime,
                                m_PathT, out m_PathT);

                            // Update 1005 CameraPathPreviewWidget.OnUpdate
                            // Purpose: Improve stale state handling for preview timing.
                            // Reset timing state when the user stops following the path and only
                            // accept full loop previews that started near the beginning of the path.
                            if (!isRecording)
                            {
                                if (!m_PreviewStarted)
                                {
                                    m_PreviewStartTime = Time.time;
                                    m_PreviewStartPathT = m_PathT;
                                    m_PreviewStarted = true;
                                }

                                if (completed && m_PreviewStarted)
                                {
                                    if (m_PreviewStartPathT.T < 0.15f && m_CurrentPathWidget.Path.LastPreviewDuration == null)
                                    {
                                        float actualDuration = Time.time - m_PreviewStartTime;
                                        m_CurrentPathWidget.Path.LastPreviewDuration = actualDuration;
                                    }
                                    m_PreviewStarted = false;
                                }
                            }

                            // ====================================================================
                            // PHASE-LOCKED TWO-PASS COMPLETION BRANCHING (CRITICAL TIMING FIX)
                            // ====================================================================
                            if (isRecording && completed)
                            {
                                var rig = SketchControlsScript.m_Instance.CameraPathCaptureRig;
                                bool safeToTriggerStop = true;

                                if (rig != null && rig.UseFrameLimit)
                                {
                                    // Ignore stale frame tick completion data if the rig is transitioning steps
                                    safeToTriggerStop = rig.IsRecordingActive;
                                }

                                if (safeToTriggerStop)
                                {
                                    SketchControlsScript.m_Instance.CameraPathCaptureRig.StopRecordingPath(true);
                                }
                            }
                        }
                        else
                        {
                            m_PreviewStarted = false;
                            m_PreviewStartPathT = new PathT(0);
                        }

                        PathT t = m_OverridePathT != null ? m_OverridePathT.Value : m_PathT;
                        transform.position = m_CurrentPathWidget.Path.GetPosition(t);

                        if (m_CurrentPathWidget.Path.RotationKnots.Count > 0)
                        {
                            transform.rotation = m_CurrentPathWidget.Path.GetRotation(t);
                        }

                        float fov = m_CurrentPathWidget.Path.GetFov(t);
                        SketchControlsScript.m_Instance.CameraPathCaptureRig.SetFov(fov);
                        SketchControlsScript.m_Instance.CameraPathCaptureRig.UpdateCameraTransform(transform);
                    }
                }
        */

        public void TintForSecondPass(bool active)
        {
            if (m_TintableMeshes == null) return;

            Color color = active ? m_SecondPassColor : m_InactiveGrey;

            for (int i = 0; i < m_TintableMeshes.Length; ++i)
            {
                if (m_TintableMeshes[i] != null && m_TintableMeshes[i].material != null)
                {
                    m_TintableMeshes[i].material.color = color;
                }
            }
        }

        // Update 2056 CameraPathPreviewWidget - Add TintForVideoGeneration method
        public void TintForVideoGeneration(bool active)
        {
            if (m_TintableMeshes == null) return;

            Color color = active ? m_VideoGenerationColor : m_InactiveGrey;

            for (int i = 0; i < m_TintableMeshes.Length; ++i)
            {
                if (m_TintableMeshes[i] != null && m_TintableMeshes[i].material != null)
                {
                    m_TintableMeshes[i].material.color = color;
                }
            }
        }

        void CacheCurrentPathWidget()
        {
            var data = WidgetManager.m_Instance.GetCurrentCameraPath();
            m_CurrentPathWidget = (data == null) ? null : data.WidgetScript;
        }

        void ValidatePathT()
        {
            if (m_CurrentPathWidget == null)
            {
                m_PathT.Zero();
            }
            else
            {
                m_PathT.Clamp(m_CurrentPathWidget.Path.PositionKnots.Count);
            }
        }

        override protected TrTransform GetDesiredTransform(TrTransform xf_GS)
        {
            if (m_CurrentPathWidget == null)
            {
                return xf_GS;
            }

            // Instead of testing the raw value that comes in from the controller position, test our
            // last valid spline position plus any translation that's happened the past frame.  This
            // method keeds the test positions near the spline, allowing continuous movement when the
            // user has moved beyond the intersection distance to the spline.
            Vector3 positionToProject = xf_GS.translation;
            if (m_LastRecordedInputXf.HasValue)
            {
                Vector3 translationDiff = xf_GS.translation - m_LastRecordedInputXf.Value;
                positionToProject = transform.position + translationDiff;
            }
            m_LastRecordedInputXf = xf_GS.translation;

            // Project transform on to the path to path t.
            Vector3 error = Vector3.zero;
            if (m_CurrentPathWidget.Path.ProjectPositionOnToPath(
                positionToProject, out PathT t, out error))
            {
                m_PathT = t;
            }
            m_LastRecordedInputXf -= error;
            return xf_GS;
        }

        override protected void OnUserEndInteracting()
        {
            base.OnUserEndInteracting();
            m_LastRecordedInputXf = null;
        }

        override public void RegisterHighlight()
        {
#if !(UNITY_ANDROID || UNITY_IOS)
            // Intentionally do not call base class.
            if (m_HighlightMeshFilters != null)
            {
                for (int i = 0; i < m_HighlightMeshFilters.Length; i++)
                {
                    if (m_HighlightMeshFilters[i].gameObject.activeInHierarchy)
                    {
                        App.Instance.SelectionEffect.RegisterMesh(m_HighlightMeshFilters[i]);
                    }
                }
            }
#endif
        }

        override public float GetActivationScore(
            Vector3 controllerPos, InputManager.ControllerName name)
        {
            if (VideoRecorderUtils.ActiveVideoRecording != null || m_OverridePathT != null)
            {
                return -1.0f;
            }
            return base.GetActivationScore(controllerPos, name);
        }

        public void ResetToPathStart()
        {
            m_PathT.Zero();
            transform.position = m_CurrentPathWidget.Path.GetPosition(m_PathT);
            if (m_CurrentPathWidget.Path.RotationKnots.Count > 0)
            {
                transform.rotation = m_CurrentPathWidget.Path.GetRotation(m_PathT);
            }
        }

        public void SetPathT(PathT pathT)
        {
            if (m_CurrentPathWidget != null)
            {
                m_PathT = pathT;
                m_PathT.Clamp(m_CurrentPathWidget.Path.PositionKnots.Count);
            }
        }

        public void SetCompletionAlongPath(float completion)
        {
            if (m_CurrentPathWidget != null)
            {
                SetPathT(new PathT(completion * (m_CurrentPathWidget.Path.PositionKnots.Count - 1)));
            }
        }

        /// Returns "completion" as a float, [0:1].
        public float? GetCompletionAlongPath()
        {
            if (m_CurrentPathWidget != null)
            {
                return m_CurrentPathWidget.Path.GetRatioToPathDistance(m_PathT);
            }
            return null;
        }

        public void TintForRecording(bool record)
        {
            if (m_TintableMeshes != null)
            {
                Color color = record ? m_RecordColor : m_InactiveGrey;
                for (int i = 0; i < m_TintableMeshes.Length; ++i)
                {
                    m_TintableMeshes[i].material.color = color;
                }
            }
        }
    }

} // namespace TiltBrush



// Update 1004  CameraPathPreviewWidget.OnUpdate
// stores only if a complete loop, checks starting position. 
/*
override protected void OnUpdate()
{
    base.OnUpdate();

    CacheCurrentPathWidget();
    ValidatePathT();

    if (m_CurrentPathWidget != null && m_CurrentPathWidget.Path.NumPositionKnots > 1)
    {
        bool isRecording = VideoRecorderUtils.ActiveVideoRecording != null ||
                           VideoRecorderUtils.ActiveStillFrameExporter != null;

        if (m_OverridePathT == null && !m_UserInteracting &&
            WidgetManager.m_Instance.FollowingPath)
        {
            float speed = Mathf.Max(m_CurrentPathWidget.Path.GetSpeed(m_PathT),
                CameraPathSpeedKnot.kMinSpeed);

            bool completed = m_CurrentPathWidget.Path.MoveAlongPath(speed * Time.deltaTime,
                m_PathT, out m_PathT);

            // === Update 1005: Only accept full loop previews that started near the beginning ===
            if (!isRecording)
            {
                if (!m_PreviewStarted)
                {
                    m_PreviewStartTime = Time.time;
                    m_PreviewStartPathT = m_PathT;           // Record where we started
                    m_PreviewStarted = true;
                }

                if (completed && m_PreviewStarted)
                {
                    // Only store duration if we started near the head of the path (full loop)
                    if (m_PreviewStartPathT.T < 0.15f && m_CurrentPathWidget.Path.LastPreviewDuration == null)
                    {
                        float actualDuration = Time.time - m_PreviewStartTime;
                        m_CurrentPathWidget.Path.LastPreviewDuration = actualDuration;
                    }

                    m_PreviewStarted = false; // Reset after first completion
                }
            }

            if (isRecording && completed)
            {
                SketchControlsScript.m_Instance.CameraPathCaptureRig.StopRecordingPath(true);
            }
        }
        else
        {
            // Update 1005 CameraPathPreviewWidget.OnUpdate
            // Purpose: Fully reset preview timing state when the user stops following the path.
            // This prevents stale start time and PathT values from being used in the next preview session.
            m_PreviewStarted = false;
            m_PreviewStartPathT = new PathT(0);
        }

        // Keep the preview widget locked to the path
        PathT t = m_OverridePathT != null ? m_OverridePathT.Value : m_PathT;
        transform.position = m_CurrentPathWidget.Path.GetPosition(t);

        if (m_CurrentPathWidget.Path.RotationKnots.Count > 0)
        {
            transform.rotation = m_CurrentPathWidget.Path.GetRotation(t);
        }

        float fov = m_CurrentPathWidget.Path.GetFov(t);
        SketchControlsScript.m_Instance.CameraPathCaptureRig.SetFov(fov);
        SketchControlsScript.m_Instance.CameraPathCaptureRig.UpdateCameraTransform(transform);
    }
}
*/

// Update 1004  CameraPathPreviewWidget.OnUpdate
// Note: The duration capture here is simplified for now. A more accurate version would track start time when preview begins. We can improve this in a follow-up if needed.
/*
override protected void OnUpdate()
{
    base.OnUpdate();

    CacheCurrentPathWidget();
    ValidatePathT();

    if (m_CurrentPathWidget != null && m_CurrentPathWidget.Path.NumPositionKnots > 1)
    {
        if (m_OverridePathT == null && !m_UserInteracting &&
            WidgetManager.m_Instance.FollowingPath)
        {
            // It's possible for Path.GetSpeed() to return a value <= 0, which makes the
            // camera path stop advancing. To correct this, ensure the minimum speed is
            // the lowest speed available for a speed knot.
            float speed = Mathf.Max(m_CurrentPathWidget.Path.GetSpeed(m_PathT),
                CameraPathSpeedKnot.kMinSpeed);

            bool completed = m_CurrentPathWidget.Path.MoveAlongPath(speed * Time.deltaTime,
                m_PathT, out m_PathT);

            // === Update 1004: Proper preview duration capture for calibration ===
            if (!m_PreviewStarted && !WidgetManager.m_Instance.FollowingPath)
            {
                // Starting a normal preview
                m_PreviewStartTime = Time.time;
                m_PreviewStarted = true;
            }

            if (completed && m_PreviewStarted && !WidgetManager.m_Instance.FollowingPath)
            {
                // Preview completed a full pass
                float actualDuration = Time.time - m_PreviewStartTime;
                if (m_CurrentPathWidget.Path != null)
                {
                    m_CurrentPathWidget.Path.LastPreviewDuration = actualDuration;
                }
                m_PreviewStarted = false; // Reset for next preview
            }

            // Stop recording if we reach the end during an active recording
            if ((VideoRecorderUtils.ActiveVideoRecording != null || VideoRecorderUtils.ActiveStillFrameExporter != null) && completed)
            {
                SketchControlsScript.m_Instance.CameraPathCaptureRig.StopRecordingPath(true);
            }
        }
        else
        {
            // Reset preview tracking if we're no longer following the path
            m_PreviewStarted = false;
        }

        // Stay locked on the path
        PathT t = m_OverridePathT != null ? m_OverridePathT.Value : m_PathT;
        transform.position = m_CurrentPathWidget.Path.GetPosition(t);
        if (m_CurrentPathWidget.Path.RotationKnots.Count > 0)
        {
            transform.rotation = m_CurrentPathWidget.Path.GetRotation(t);
        }
        float fov = m_CurrentPathWidget.Path.GetFov(t);
        SketchControlsScript.m_Instance.CameraPathCaptureRig.SetFov(fov);
        SketchControlsScript.m_Instance.CameraPathCaptureRig.UpdateCameraTransform(transform);
    }
}
*/

/*
override protected void OnUpdate()
{
    base.OnUpdate();

    CacheCurrentPathWidget();
    ValidatePathT();

    if (m_CurrentPathWidget != null && m_CurrentPathWidget.Path.NumPositionKnots > 1)
    {
        if (m_OverridePathT == null && !m_UserInteracting &&
            WidgetManager.m_Instance.FollowingPath)
        {
            // It's possible for Path.GetSpeed() to return a value <= 0, which makes the
            // camera path stop advancing.  To correct this, ensure the minimum speed is
            // the lowest speed available for a speed knot.
            float speed = Mathf.Max(m_CurrentPathWidget.Path.GetSpeed(m_PathT),
                CameraPathSpeedKnot.kMinSpeed);

            bool completed = m_CurrentPathWidget.Path.MoveAlongPath(speed * Time.deltaTime,
                m_PathT, out m_PathT);

            // Update 1004 - Capture preview duration for calibration when using ScaleSpeedToFrameCount
            if (completed && !WidgetManager.m_Instance.FollowingPath)
            {
                // Only store when doing a normal preview (not during recording)
                if (m_CurrentPathWidget.Path != null)
                {
                    m_CurrentPathWidget.Path.LastPreviewDuration = 0f; // Placeholder - will improve timing later
                }
            }

            if ((VideoRecorderUtils.ActiveVideoRecording != null || VideoRecorderUtils.ActiveStillFrameExporter != null) && completed)
            {
                SketchControlsScript.m_Instance.CameraPathCaptureRig.StopRecordingPath(true);
            }
        }

        // Stay locked on the path.
        PathT t = m_OverridePathT != null ? m_OverridePathT.Value : m_PathT;
        transform.position = m_CurrentPathWidget.Path.GetPosition(t);
        if (m_CurrentPathWidget.Path.RotationKnots.Count > 0)
        {
            transform.rotation = m_CurrentPathWidget.Path.GetRotation(t);
        }
        float fov = m_CurrentPathWidget.Path.GetFov(t);
        SketchControlsScript.m_Instance.CameraPathCaptureRig.SetFov(fov);
        SketchControlsScript.m_Instance.CameraPathCaptureRig.UpdateCameraTransform(transform);
    }
}
*/
