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

using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TiltBrush
{

    public class SelectionWidget : GrabWidget
    {
        public event System.Action<TrTransform> SelectionTransformed;

        [SerializeField] private CanvasScript m_SelectionCanvas;

        private TrTransform m_xfOriginal_SS = TrTransform.identity;
        private Bounds? m_SelectionBounds_CS;

        private InputManager.ControllerName? m_CurrentIntersectionController;
        private InputManager.ControllerName? m_NextIntersectionController;
        private GpuIntersector.FutureBatchResult m_IntersectionFuture;
        private Dictionary<InputManager.ControllerName, float> m_LastIntersectionResult;
        private int m_IntersectionFrame;
        private Vector2 m_SizeRange;

        Vector3 m_PlaneLockGrabStartPos_GS;
        Quaternion m_PlaneLockGrabStartRot_GS;
        float m_PlaneLockGrabStartSize;
        Vector3 m_PlaneLockPivot_GS;
        Vector3 m_PlaneLockNormal_GS;
        Vector3 m_PlaneLockForward_GS;
        Vector3 m_PlaneLockUp_GS;
        Vector3 m_PlaneLockGrabHandPos_GS;
        bool m_PlaneLockGrabCached;
        int m_PlaneLockFrameCount;
        float m_PlaneLockLastAlong;
        float m_PlaneLockLastSlide;
        bool m_PlaneLockLastApplied;

        protected override bool SnapButtonConflictsWithDuplicate => true;

        public override bool AllowDormancy
        {
            get
            {
                return InputManager.Brush.IsTrigger();
            }
        }

        public override float HapticDuration
        {
            get
            {
                return 0.05f;
            }
        }

        override protected void Awake()
        {
            base.Awake();
            m_LastIntersectionResult = new Dictionary<InputManager.ControllerName, float>();
            m_LastIntersectionResult[InputManager.ControllerName.Brush] = -1;
            m_LastIntersectionResult[InputManager.ControllerName.Wand] = -1;
            m_CustomShowHide = true;
            ResetSizeRange();
        }

        override protected void OnHide()
        {
            if (SelectionManager.m_Instance.HasSelection)
            {
                // Selection may already have been force-deleted in the case where a selection command was
                // issued before the selection widget was able to hide.
                SelectionManager.m_Instance.DeleteSelection();
            }
            gameObject.SetActive(true);
        }

        override protected void Start()
        {
            base.Start();
            App.Scene.PoseChanged += OnScenePoseChanged;
        }

        /// The transformation the user has performed on the widget box relative to the scene.
        public TrTransform SelectionTransform
        {
            get
            {
                return App.Scene.AsScene[transform] * m_xfOriginal_SS.inverse;
            }
            set
            {
                App.Scene.AsScene[transform] = value * m_xfOriginal_SS;
            }
        }

        public override void AssignControllerMaterials(InputManager.ControllerName controller)
        {
            if (!SketchControlsScript.m_Instance.IsUserTwoHandGrabbingWidget() &&
                SketchControlsScript.m_Instance.OneHandGrabController == controller)
            {
                InputManager.Controllers[(int)controller].Geometry.ShowDuplicateOption();
            }
        }

        public void SetSelectionBounds(Bounds bounds)
        {
            Debug.Assert(bounds.extents.magnitude > 0);

            m_SelectionBounds_CS = bounds;
            UpdateBoxCollider();
            gameObject.SetActive(true);
        }

        public void SelectionCleared()
        {
            m_SelectionBounds_CS = null;
            gameObject.SetActive(false);
            m_xfOriginal_SS = TrTransform.identity;
            App.Scene.AsScene[transform] = TrTransform.identity;
        }

        override public float GetSignedWidgetSize()
        {
            return transform.localScale.Max();
        }

        override protected void SetWidgetSizeInternal(float fSize)
        {
            bool scaleAboutPlane = PlaneLockScaleActive()
                && m_PlaneLockGrabCached
                && UserTwoHandGrabbing;
            transform.localScale = (transform.localScale / GetSignedWidgetSize()) * fSize;
            if (scaleAboutPlane)
            {
                float ratio = fSize / m_PlaneLockGrabStartSize;
                if (Mathf.Abs(m_PlaneLockGrabStartSize) < 1e-6f)
                {
                    ratio = 1.0f;
                }
                transform.position = m_PlaneLockPivot_GS
                    + ratio * (m_PlaneLockGrabStartPos_GS - m_PlaneLockPivot_GS);
            }
            if (SelectionTransformed != null)
            {
                SelectionTransformed(SelectionTransform);
            }
        }

        public override Vector2 GetWidgetSizeRange()
        {
            return m_SizeRange;
        }

        /// Updates the size limits to be the intersection of the current and passed through range.
        public void UpdateSizeRange(Vector2 range)
        {
            m_SizeRange.x = Mathf.Max(m_SizeRange.x, range.x);
            m_SizeRange.y = Mathf.Min(m_SizeRange.y, range.y);
        }

        public void ResetSizeRange()
        {
            m_SizeRange = base.GetWidgetSizeRange();
        }

        // TODO: We're disabling "duplicate on hover" until we've had a chance to UER test
        // the basic functionality. Once that's been tested, we can experiment with what we do
        // when the selection tool hovers over an existing selection.
        //override public bool HasHoverInteractions() { return true; }
        //
        //override public void AssignHoverControllerMaterials(InputManager.ControllerName controller) {
        //  // If we're intersecting with the brush hand, allow additional actions.
        //  if (controller == InputManager.ControllerName.Brush) {
        //    InputManager.GetControllerGeometry(controller).ShowSelectionOptions();
        //  }
        //}

        override public float GetActivationScore(
            Vector3 vControllerPos_GS, InputManager.ControllerName name)
        {
            if (!PointInCollider(vControllerPos_GS))
            {
                return -1;
            }

            // If the data we've got is old, delete it all.
            if ((Time.frameCount - m_IntersectionFrame) > 3)
            {
                m_CurrentIntersectionController = null;
                m_NextIntersectionController = null;
                m_IntersectionFuture = null;
            }

            // If we have an intersection in the pipe, set up the next one if it is of a different type.
            // This is so that if we're looking for both Wand and Brush, it will toggle between them.
            // If the results are ready, then store them off and clear the current intersection.
            if (m_CurrentIntersectionController.HasValue)
            {
                if (m_CurrentIntersectionController.Value != name)
                {
                    m_NextIntersectionController = name;
                }
                if (m_IntersectionFuture.IsReady)
                {
                    m_LastIntersectionResult[m_CurrentIntersectionController.Value] =
                        m_IntersectionFuture.HasAnyIntersections() ? 1 : -1;
                    m_CurrentIntersectionController = null;
                    m_IntersectionFuture = null;
                }
            }

            // If we don't have a current intersection in the pipe, grab the next one if there is one,
            // or just start off the intersection we have been asked for.
            if (!m_CurrentIntersectionController.HasValue)
            {
                if (!m_NextIntersectionController.HasValue)
                {
                    m_NextIntersectionController = name;
                }
                m_CurrentIntersectionController = m_NextIntersectionController.Value;
                m_NextIntersectionController = null;
                Debug.Assert(m_CurrentIntersectionController.Value == InputManager.ControllerName.Wand ||
                    m_CurrentIntersectionController.Value == InputManager.ControllerName.Brush);
                // Because we may be requesting an intersection on another controller's behalf, don't use
                // the passed position, but instead the position respective of the enum.
                Vector3 pos = (m_CurrentIntersectionController.Value == InputManager.ControllerName.Brush) ?
                    InputManager.Brush.Geometry.ToolAttachPoint.position :
                    InputManager.Wand.Geometry.ToolAttachPoint.position;
                m_IntersectionFuture = App.Instance.GpuIntersector.RequestBatchIntersection(
                    pos, m_CollisionRadius, (1 << m_SelectionCanvas.gameObject.layer));
                m_IntersectionFrame = Time.frameCount;
            }

            float result = -1;
            m_LastIntersectionResult.TryGetValue(name, out result);
            return result;
        }

        protected override void OnEndUpdateWithDesiredTransform()
        {
            base.OnEndUpdateWithDesiredTransform();
            if (SelectionTransformed != null)
            {
                TrTransform xf = SelectionTransform;
                if (m_PlaneLockFrameCount == 1)
                {
                    Debug.LogError(
                        "[SelectionWidget.OnEndUpdateWithDesiredTransform] CANVAS selection-box only" +
                        " xf.pos=" + xf.translation +
                        " xf.scale=" + xf.scale +
                        " fired=True");
                }
                SelectionTransformed(xf);
            }
        }

        protected override void OnUpdate()
        {
            if (m_CurrentState == State.Tossed && SelectionTransformed != null)
            {
                SelectionTransformed(SelectionTransform);
            }
        }

        private void OnScenePoseChanged(TrTransform prev, TrTransform current)
        {
            UpdateBoxCollider();
        }

        private void UpdateBoxCollider()
        {
            if (!m_SelectionBounds_CS.HasValue)
            {
                return;
            }

            // Temporarily remember the user-made transformations on the selection
            // in scene-space. For example, when the user freshly selects strokes but has not moved
            // them, this will be Identity.
            TrTransform UserTransformations_SS = SelectionTransform;

            // Inflate bounding box so that we can still respect the collision
            // radius for parts of strokes that are at the outer edges of the
            // bounding box.
            Vector3 inflatedExtents_CS = m_SelectionBounds_CS.Value.extents;
            inflatedExtents_CS += Vector3.one * (m_CollisionRadius / m_SelectionCanvas.Pose.scale);

            // Position our widget within global space as if it's in canvas space
            // so that we can correctly set non-uniform scale. Even though we
            // override the non-uniform scale at the very end, we also do it here
            // because, even though TrTransform only considers uniform scale, it
            // gets its uniform scale from the longest extent on any non-uniform scale.
            transform.localPosition = m_SelectionBounds_CS.Value.center;
            transform.localScale = inflatedExtents_CS;
            transform.localRotation = Quaternion.identity;

            // Capture the scene-space transformation for the selected bounds
            // without considering how the user transformed the selection since
            // creating it.
            m_xfOriginal_SS = App.Scene.AsScene[transform];

            // Recombine the initial stroke transformation (the canvas-space bounds
            // within scene-space) with the transformations caused by the user manipulating
            // the selection (also in scene-space).
            App.Scene.AsScene[transform] = UserTransformations_SS * m_xfOriginal_SS;

            // Since TrTransform doesn't account for non-uniform scale, correct the
            // scale for the non-uniform bounds.
            transform.localScale = inflatedExtents_CS * UserTransformations_SS.scale;
        }

        public void PreventSelectionFromMoving(bool preventMoving)
        {
            // We're overriding pinning to prevent selections.
            m_Pinned = preventMoving;
        }

        override protected void OnUserBeginInteracting()
        {
            base.OnUserBeginInteracting();
            CachePlaneLockGrab();

            // Use pin visuals for preventing movement.
            WidgetManager.m_Instance.DestroyWidgetPin(m_Pin);
            m_Pin = WidgetManager.m_Instance.GetWidgetPin();
            InitPin();

            // Start disabled.
            m_Pin.gameObject.SetActive(false);

            // If we are pinned, jiggle the pin.
            if (m_Pinned)
            {
                m_Pin.gameObject.SetActive(true);
                m_Pin.WobblePin(m_InteractingController);
            }
        }


        void CachePlaneLockGrab()
        {
            m_PlaneLockGrabStartPos_GS = transform.position;
            m_PlaneLockGrabStartRot_GS = transform.rotation;
            m_PlaneLockGrabStartSize = GetSignedWidgetSize();
            m_PlaneLockGrabHandPos_GS = m_PlaneLockGrabStartPos_GS;
            if (InputManager.m_Instance != null)
            {
                int hand = (int)m_InteractingController;
                if (hand >= 0 && hand < InputManager.Controllers.Length
                    && InputManager.Controllers[hand] != null
                    && InputManager.Controllers[hand].Transform != null)
                {
                    m_PlaneLockGrabHandPos_GS =
                        InputManager.Controllers[hand].Transform.position;
                }
            }
            m_PlaneLockGrabCached = false;
            m_PlaneLockNormal_GS = Vector3.right;
            m_PlaneLockForward_GS = Vector3.forward;
            m_PlaneLockUp_GS = Vector3.up;
            m_PlaneLockPivot_GS = m_PlaneLockGrabStartPos_GS;
            if (PointerManager.m_Instance == null
                || PointerManager.m_Instance.SymmetryWidget == null)
            {
                return;
            }
            SymmetryWidget mirror = PointerManager.m_Instance.SymmetryWidget;
            Plane plane = mirror.ReflectionPlane;
            m_PlaneLockNormal_GS = plane.normal.normalized;
            m_PlaneLockForward_GS = Vector3.ProjectOnPlane(
                mirror.transform.forward, m_PlaneLockNormal_GS);
            if (m_PlaneLockForward_GS.sqrMagnitude < 1e-8f)
            {
                m_PlaneLockForward_GS = Vector3.ProjectOnPlane(
                    Vector3.forward, m_PlaneLockNormal_GS);
            }
            if (m_PlaneLockForward_GS.sqrMagnitude < 1e-8f)
            {
                m_PlaneLockForward_GS = Vector3.up;
            }
            m_PlaneLockForward_GS.Normalize();
            m_PlaneLockUp_GS = Vector3.ProjectOnPlane(
                mirror.transform.up, m_PlaneLockNormal_GS);
            if (m_PlaneLockUp_GS.sqrMagnitude < 1e-8f)
            {
                m_PlaneLockUp_GS = Vector3.ProjectOnPlane(
                    Vector3.up, m_PlaneLockNormal_GS);
            }
            if (m_PlaneLockUp_GS.sqrMagnitude < 1e-8f)
            {
                m_PlaneLockUp_GS = Vector3.forward;
            }
            m_PlaneLockUp_GS.Normalize();
            m_PlaneLockPivot_GS = plane.ClosestPointOnPlane(m_PlaneLockGrabStartPos_GS);
            m_PlaneLockGrabCached = true;
            int strokes = 0;
            int widgets = 0;
            if (SelectionManager.m_Instance != null)
            {
                strokes = SelectionManager.m_Instance.SelectedStrokes.Count();
                widgets = SelectionManager.m_Instance.SelectedWidgets.Count();
            }
            bool live = mirror.IsLiveSinglePlaneActive();
            bool bothSides = false;
            if (SelectionManager.m_Instance != null && SelectionManager.m_Instance.HasSelection)
            {
                const float kEps = 0.01f;
                float eps = kEps * App.METERS_TO_UNITS;
                bothSides = SelectionManager.m_Instance.SelectionHasBothSidesOfPlane(
                    mirror.ReflectionPlane, eps);
            }
            m_PlaneLockFrameCount = 0;
            m_PlaneLockLastAlong = 0.0f;
            m_PlaneLockLastSlide = 0.0f;
            m_PlaneLockLastApplied = false;
            Debug.LogError(
                "[SelectionWidget.CachePlaneLockGrab] GRAB selection-box only" +
                " button=" + mirror.PlaneLockActive +
                " tunnel=" + mirror.TunnelLockActive +
                " liveMirror=" + live +
                " bothSides=" + bothSides +
                " scaleActive=" + PlaneLockScaleActive() +
                " moveActive=" + PlaneLockMoveActive() +
                " twoHand=" + UserTwoHandGrabbing +
                " strokes=" + strokes +
                " widgets=" + widgets +
                " n=" + m_PlaneLockNormal_GS +
                " pos=" + m_PlaneLockGrabStartPos_GS);
        }

        bool PlaneLockMoveActive()
        {
            if (PointerManager.m_Instance == null
                || PointerManager.m_Instance.SymmetryWidget == null)
            {
                return false;
            }
            if (SelectionManager.m_Instance == null
                || !SelectionManager.m_Instance.HasSelection)
            {
                return false;
            }
            SymmetryWidget mirror = PointerManager.m_Instance.SymmetryWidget;
            return mirror.TunnelLockActive || mirror.PlaneLockActive;
        }

        bool PlaneLockScaleActive()
        {
            return PlaneLockMoveActive();
        }

        override protected TrTransform GetDesiredTransform(TrTransform xf_GS)
        {
            TrTransform outXf = base.GetDesiredTransform(xf_GS);
            bool scaleLock = PlaneLockScaleActive();
            bool moveLock = PlaneLockMoveActive();
            if (!scaleLock && !moveLock)
            {
                return outXf;
            }
            if (!m_PlaneLockGrabCached)
            {
                CachePlaneLockGrab();
            }
            Vector3 n = m_PlaneLockNormal_GS;
            if (n.sqrMagnitude < 1e-8f)
            {
                n = Vector3.right;
            }
            n.Normalize();
            if (moveLock || scaleLock)
            {
                Vector3 handNow = m_PlaneLockGrabHandPos_GS;
                if (InputManager.m_Instance != null)
                {
                    int hand = (int)m_InteractingController;
                    if (hand >= 0 && hand < InputManager.Controllers.Length
                        && InputManager.Controllers[hand] != null
                        && InputManager.Controllers[hand].Transform != null)
                    {
                        handNow = InputManager.Controllers[hand].Transform.position;
                    }
                }
                Vector3 handDelta = handNow - m_PlaneLockGrabHandPos_GS;
                float along = Vector3.Dot(handDelta, n);
                Vector3 slide = handDelta - n * along;
                SymmetryWidget mirror = PointerManager.m_Instance != null
                    ? PointerManager.m_Instance.SymmetryWidget
                    : null;
                bool tunnel = mirror != null && mirror.TunnelLockActive;
                bool plane = mirror != null && mirror.PlaneLockActive;
                if (tunnel && !plane)
                {
                    Vector3 f = m_PlaneLockForward_GS;
                    if (f.sqrMagnitude < 1e-8f)
                    {
                        f = Vector3.forward;
                    }
                    f.Normalize();
                    slide = f * Vector3.Dot(handDelta, f);
                }
                else if (plane && !tunnel)
                {
                    Vector3 u = m_PlaneLockUp_GS;
                    if (u.sqrMagnitude < 1e-8f)
                    {
                        u = Vector3.up;
                    }
                    u.Normalize();
                    slide = u * Vector3.Dot(handDelta, u);
                }
                outXf.translation = m_PlaneLockGrabStartPos_GS + slide;
                outXf.rotation = m_PlaneLockGrabStartRot_GS;
                m_PlaneLockLastAlong = along;
                m_PlaneLockLastSlide = slide.magnitude;
                m_PlaneLockLastApplied = true;
                m_PlaneLockFrameCount++;
                if (m_PlaneLockFrameCount == 1)
                {
                    Debug.LogError(
                        "[SelectionWidget.GetDesiredTransform] APPLY selection-box only" +
                        " along=" + along.ToString("F3") +
                        " slide=" + slide.magnitude.ToString("F3") +
                        " start=" + m_PlaneLockGrabStartPos_GS +
                        " out=" + outXf.translation);
                }
            }
            return outXf;
        }

        override protected void OnUserBeginTwoHandGrab(
            Vector3 primaryHand, Vector3 secondaryHand, bool secondaryHandInObject)
        {
            base.OnUserBeginTwoHandGrab(primaryHand, secondaryHand, secondaryHandInObject);
            CachePlaneLockGrab();
        }

        override protected void OnUserEndInteracting()
        {
            TrTransform xf = SelectionTransform;
            Debug.LogError(
                "[SelectionWidget.OnUserEndInteracting] END selection-box only" +
                " applied=" + m_PlaneLockLastApplied +
                " frames=" + m_PlaneLockFrameCount +
                " along=" + m_PlaneLockLastAlong.ToString("F3") +
                " slide=" + m_PlaneLockLastSlide.ToString("F3") +
                " start=" + m_PlaneLockGrabStartPos_GS +
                " end=" + transform.position +
                " rotDeg=" + Quaternion.Angle(m_PlaneLockGrabStartRot_GS, transform.rotation).ToString("F1") +
                " canvas.pos=" + xf.translation +
                " canvas.scale=" + xf.scale);
            base.OnUserEndInteracting();
        }

        protected override IEnumerable<StencilWidget> GetStencilsToIgnore()
        {
            return SelectionManager.m_Instance.SelectedWidgets.OfType<StencilWidget>();
        }

        protected override bool MagnetizeToStencils(ref TrTransform xf_GS)
        {
            // In some cases it's weird to align the orientation of a selection
            // with a guide (i.e. when there's only strokes selected)
            var rot = xf_GS.rotation;
            bool usedStencil = base.MagnetizeToStencils(ref xf_GS);
            // No need to do anything if we didn't use a stencil
            if (!usedStencil) return false;

            // Only restore original orientation if we've got no widgets selected
            if (SelectionManager.m_Instance.SelectedWidgets.Count() == 1)
            {
                // A single widget should align as if it was grabbed directly
                // Rather than selected
                var foo = xf_GS.rotation;
                xf_GS.rotation = rot;
                SelectionManager.m_Instance.SelectedWidgets.First().transform.rotation = foo;
            }
            else
            {
                xf_GS.rotation = rot;
            }

            return usedStencil;
        }
    }
} // namespace TiltBrush
