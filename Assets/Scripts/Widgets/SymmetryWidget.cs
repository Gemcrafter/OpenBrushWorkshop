


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
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;


namespace TiltBrush
{

    [System.Serializable]
    public class GuideBeam
    {
        public Transform m_Beam;
        public SymmetryWidget.BeamDirection m_Direction;
        [NonSerialized] public Renderer m_BeamRenderer;
        [NonSerialized] public Vector3 m_Offset;
        [NonSerialized] public Vector3 m_BaseScale;
    }

    public class SymmetryWidget : GrabWidget
    {
        [SerializeField] private Renderer m_LeftRightMesh;
        [SerializeField] private Renderer m_FrontBackMesh;
        [SerializeField] private TextMeshPro m_TitleText;
        [SerializeField] private GameObject m_HintText;
        [SerializeField] private GrabWidgetHome m_Home;
        [SerializeField] private Mesh m_CustomSymmetryMesh;
        [SerializeField] private Material m_CustomSymmetryMaterial;
        public enum BeamDirection
        {
            Up,
            Down,
            Left,
            Right,
            Front,
            Back
        }
        [SerializeField] private GuideBeam[] m_GuideBeams;
        [Tooltip("Live glass beam length in meters before METERS_TO_UNITS.")]
        [SerializeField] private float m_GuideBeamLength;
        private float m_GuideBeamShowRatio;
        [Tooltip("Live glass beam color while orientation snap is holding. Slot guides do not use this.")]
        [SerializeField] private Color m_SnapColor;
        [Header("Live mirror tint")]
        [Tooltip("When on, the live glass mesh uses Live Mirror Tint. When off, stock material color. Does not tint slot guides.")]
        [SerializeField] bool m_LiveMirrorTintEnabled = false;
        [Tooltip("Live glass mesh color while Live Mirror Tint Enabled is on. Hidden when the glass is off. Slot guides use SlotGuide colors instead.")]
        [SerializeField] Color m_LiveMirrorTint = Color.white;
        [SerializeField] private float m_SnapOrientationSpeed = 0.2f;
        [SerializeField] private float m_SnapAngleXZPlane = 45.0f;
        [SerializeField] private float m_SnapXZPlaneStickyAmount;
        private float m_SnapQuantizeAmount = 15.0f;
        private float m_SnapStickyAngle = 1.0f;
        [Header("Mirror diagram")]
        [Tooltip("Show Left/Right beams on the live glass. Does not add a second plane square.")]
        [SerializeField] bool m_ShowThirdAxisLive = false;
        [Tooltip("Show Left/Right beams on slot guides. Does not add a second plane square.")]
        [SerializeField] bool m_ShowThirdAxisSlotGuide = false;

        public enum PreferredOrientation
        {
            HorizontalSideways = 0,
            HorizontalForward = 1,
            VerticalForward = 2,
            VerticalSideways = 3
        }


        // Set up controls for constrained work on the MirrorControlsPanel
        [Header("Mirror locks (glass grab only)")]
        [Tooltip("Glass grab only. Keep the preferred plane while moving or on release.")]
        [SerializeField] private bool m_LockOrientation = false;
        [Tooltip("Glass grab only. Block throw/spin about the plane normal.")]
        [SerializeField] private bool m_LockSpin = false;


        [Tooltip("Default facing for the live glass (Inspector and in-game orientation buttons).")]
        [SerializeField]
        private PreferredOrientation m_PreferredOrientation =
            PreferredOrientation.VerticalForward;

        [SerializeField] private float m_JumpToUserControllerOffsetDistance;
        [SerializeField] private float m_JumpToUserControllerYOffset;
        private static readonly int OutlineWidth = Shader.PropertyToID("_OutlineWidth");
        [SerializeField] private Transform m_SymmetryDomainPrefab;
        [SerializeField] private Transform m_SymmetryDomainParent;


        // User-saved exact mirror pose (scene space). Save/Recall buttons; extendable to multi-anchor.
        private TrTransform m_SavedMirrorPose_SS;
        private bool m_HasSavedMirrorPose;

        // --- Move to Mirror mode (destination realign) ---
        bool m_MoveToMirrorModeActive;
        int m_MoveToMirrorToIndex = -1;
        bool m_MoveToMirrorPickingTo;
        bool m_MoveToMirrorBlockActivate;
        int m_TeleportDestIndex = 0;
        bool m_TeleportJumpPending;
        int m_MoveToMirrorTempFromIndex = -1;
        int m_MoveToMirrorOriginFromIndex = -1;
        HashSet<Stroke> m_LastSendStrokes = new HashSet<Stroke>();
        HashSet<GrabWidget> m_LastSendWidgets = new HashSet<GrabWidget>();
        float m_LastSlotGuideSceneScale = -1.0f;
        float m_LastSlotGuideLiveWorldScale = -1.0f;
        float m_HyperspaceSceneScaleAtEnter = -1.0f;
        public enum SelectionSlideDefault
        {
            [InspectorName("Front-Back Only")]
            FrontBackOnly = 0,
            [InspectorName("Up-Down Only")]
            UpDownOnly = 1,
            [InspectorName("Allow Either")]
            AllowEither = 2,
            [InspectorName("Free Movement (Breaks Symmetry)")]
            FreeMovement = 3
        }

        [Tooltip("Default selection slide after load or new sketch. Front-Back Only = toward/away. Up-Down Only = vertical on the glass. Allow Either = both. Free Movement = no constraint.")]
        [SerializeField]
        SelectionSlideDefault m_SelectionSlideDefault =
            SelectionSlideDefault.FrontBackOnly;
        [Tooltip("Selection grab. On = up/down on the glass. Combines with Tunnel Lock. Both on = either axis. Both off = free.")]
        [SerializeField] bool m_PlaneLockActive;
        [Tooltip("Selection grab. On = toward/away (front-back) on the glass. Combines with Plane Lock. Both on = either axis. Both off = free.")]
        [SerializeField] bool m_TunnelLockActive = true;
        bool m_WasFlyToolLastTick;


        public enum AxisLock
        {
            None = 0,
            X = 1,   // Left-Right
            Y = 2,   // Up-Down
            Z = 3,   // Forward-Back
            All = 4  // No translation
        }

        [Tooltip("Glass grab only. Limit live-mirror slide to one world axis (or All = no slide). Not selection Plane/Tunnel Lock.")]
        [SerializeField] private AxisLock m_AxisLock = AxisLock.None;

        // Grab origin in scene space for this drag (not the Save/Recall pose).
        private Vector3 m_AxisLockGrabOrigin_SS;
        private bool m_HasAxisLockGrabOrigin;

        // If within this distance of saved pose on grab start, line through saved position.
        private const float kAxisLockSavedPoseSnapMeters = 0.05f;

        // One-level undo: pose at start of last grab that actually moved the mirror.
        private TrTransform m_UndoMirrorPose_SS;
        private bool m_HasMirrorMoveUndo;
        private TrTransform m_PoseAtGrabBegin_SS;
        private const float kMirrorUndoMoveEpsilonMeters = 0.001f;

        // --- Mirror save slots (multi-pose; separate from legacy single SaveMirrorPose) ---
        public const int kMirrorSaveSlotCount = 20;

        [SerializeField]
        [Tooltip("How many slot buttons this build shows. Import logs if a sketch uses a higher index.")]
        int m_MirrorSaveSlotVisibleCount = 20;

        private bool[] m_MirrorSaveSlotOccupied = new bool[kMirrorSaveSlotCount];
        private TrTransform[] m_MirrorSaveSlotPose_SS = new TrTransform[kMirrorSaveSlotCount];

        // One-deep clear undo (clear mode).
        private int m_LastClearedMirrorSaveSlotIndex = -1;
        private TrTransform m_LastClearedMirrorSaveSlotPose_SS;
        private bool m_HasLastClearedMirrorSaveSlot;

        // One-deep pre-jump buffer (slot recall or True Center).
        private TrTransform m_QuickReturnPose_SS;
        private bool m_HasQuickReturn;

        // --- SlotGuides (visual markers at saved slot poses) ---
        [Tooltip("Prefab spawned for slot markers. Visual only. No grab.")]
        [SerializeField] GameObject m_SlotGuidePrefab;
        GameObject[] m_SlotGuidePool;
        bool[] m_SlotGuideActive;
        Transform m_SlotGuidePoolRoot;

        [Header("SlotGuide colors")]
        [Tooltip("World mark for an occupied slot that is not the live glass and not Hyperspace To. Show All on.")]
        [SerializeField] Color m_SlotGuideRegular = new Color(1.0f, 0.2f, 0.2f, 1.0f);
        [Tooltip("World mark for the Hyperspace To / jump target. Hidden if that pose is the live glass (avoids z-fight).")]
        [SerializeField] Color m_SlotGuideHyperspace = new Color(0.85f, 0.65f, 0.15f, 1.0f);
        [Tooltip("World mark for a saved pose that matches the live glass only while the glass is off. Never drawn on top of a visible glass.")]
        [SerializeField] Color m_SlotGuideLatent = new Color(0.35f, 0.75f, 1.0f, 1.0f);

        [Header("SlotGuide display")]
        [Tooltip("Slot guide beam length vs live glass beams.")]
        [SerializeField] float m_SlotGuideBeamLengthScale = 0.5f;
        [Tooltip("Slot guide size vs live glass. 1 = same on-screen size.")]
        [SerializeField] float m_SlotGuideDisplayScale = 1.0f;
        [Tooltip("Brightness multiplier on Custom/Pointer _Color.")]
        [SerializeField] float m_SlotGuideEmissionScale = 1.0f;
        bool m_SlotGuidesToggledOn;
        GameObject m_LatentLiveGuide;

        Vector3[][] m_SlotGuideBeamRestLocalPos;
        Vector3[][] m_SlotGuideBeamRestLocalScale;
        static readonly string[] kSlotGuideBeamNames =
        {
            "GuideBeamUp",
            "GuideBeamDown",
            "GuideBeamLeft",
            "GuideBeamRight",
            "GuideBeamFront",
            "GuideBeamBack"
        };


        public bool MoveToMirrorModeActive
        {
            get
            {
                return m_MoveToMirrorModeActive;
            }
        }

        public int MoveToMirrorToIndex
        {
            get
            {
                return m_MoveToMirrorToIndex;
            }
        }

        public bool MoveToMirrorPickingTo
        {
            get
            {
                return m_MoveToMirrorPickingTo;
            }
        }

        public int TeleportDestIndex
        {
            get
            {
                return m_TeleportDestIndex;
            }
        }

        public bool PeekTeleportDestValid()
        {
            return m_TeleportDestIndex >= 0
                && HasMirrorSaveSlot(m_TeleportDestIndex);
        }

        public bool PeekTeleportShouldLight()
        {
            return PeekTeleportDestValid() && m_TeleportJumpPending;
        }

        public void ClearTeleportJumpPending()
        {
            m_TeleportJumpPending = false;
        }

        public void ClearTeleportDest()
        {
            Debug.LogError(
                "[SymmetryWidget.ClearTeleportDest] was dest=" + m_TeleportDestIndex +
                " jumpPending=" + m_TeleportJumpPending);
            m_TeleportDestIndex = -1;
            m_TeleportJumpPending = false;
        }

        public void SetTeleportDest(int index)
        {
            if (index < 0 || index >= kMirrorSaveSlotCount || !HasMirrorSaveSlot(index))
            {
                return;
            }
            m_TeleportDestIndex = index;
            m_TeleportJumpPending = true;
            Debug.LogError(
                "[SymmetryWidget.SetTeleportDest] dest=" + index + " jumpPending=True");
        }


        public Plane ReflectionPlane
        {
            get
            {
                return new Plane(transform.right, transform.position);
            }
        }

        public bool TryGetSlotPlane_GS(int index, out Plane plane_GS)
        {
            plane_GS = new Plane(Vector3.right, Vector3.zero);
            TrTransform pose_SS;
            if (!TryGetMirrorSaveSlotPose(index, out pose_SS))
            {
                return false;
            }
            if (App.Scene == null)
            {
                return false;
            }
            TrTransform pose_GS = App.Scene.Pose * pose_SS;
            Vector3 normal_GS = pose_GS.rotation * Vector3.right;
            if (normal_GS.sqrMagnitude < 1e-8f)
            {
                return false;
            }
            plane_GS = new Plane(normal_GS.normalized, pose_GS.translation);
            return true;
        }

        public bool TryGetSelectionConstraintPlane(out Plane plane_GS, out int slotIndex)
        {
            plane_GS = ReflectionPlane;
            slotIndex = -1;
            if (SelectionManager.m_Instance == null
                || !SelectionManager.m_Instance.HasSelection)
            {
                return false;
            }
            const float kEps = 0.01f;
            float eps = kEps * App.METERS_TO_UNITS;
            Vector3 sel_SS = Vector3.zero;
            bool haveSel = TryGetSelectionPos_SS(out sel_SS);
            float bestDist = float.MaxValue;
            Plane bestPlane = plane_GS;
            int bestIndex = -1;
            for (int i = 0; i < kMirrorSaveSlotCount; ++i)
            {
                Plane slotPlane;
                if (!TryGetSlotPlane_GS(i, out slotPlane))
                {
                    continue;
                }
                if (!SelectionManager.m_Instance.SelectionHasBothSidesOfPlane(slotPlane, eps))
                {
                    continue;
                }
                float dist = 0.0f;
                if (haveSel)
                {
                    TrTransform pose_SS;
                    TryGetMirrorSaveSlotPose(i, out pose_SS);
                    dist = (pose_SS.translation - sel_SS).sqrMagnitude;
                }
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestPlane = slotPlane;
                    bestIndex = i;
                }
            }
            if (bestIndex >= 0)
            {
                plane_GS = bestPlane;
                slotIndex = bestIndex;
                return true;
            }
            if (IsLiveSinglePlaneActive()
                && SelectionManager.m_Instance.SelectionHasBothSidesOfPlane(ReflectionPlane, eps))
            {
                plane_GS = ReflectionPlane;
                slotIndex = -1;
                return true;
            }
            return false;
        }


        /// Scene-space rotation for preferred home.
        /// Horizontal: local right -> scene up (plane flat, up/down mirror).
        /// Vertical: identity (current built-in default).
        private Quaternion GetPreferredHomeRotation_SS()
        {
            Quaternion horizontalSideways =
                Quaternion.FromToRotation(Vector3.right, Vector3.up);

            switch (m_PreferredOrientation)
            {
                case PreferredOrientation.HorizontalSideways:
                    return horizontalSideways;

                case PreferredOrientation.HorizontalForward:
                    return Quaternion.AngleAxis(90f, Vector3.up) * horizontalSideways;

                case PreferredOrientation.VerticalForward:
                    return Quaternion.identity;

                case PreferredOrientation.VerticalSideways:
                    return Quaternion.AngleAxis(90f, Vector3.up);

                default:
                    return Quaternion.identity;
            }
        }

        /// Scene-space home rotation for a mode (does not change current preference or transform).
        public static Quaternion GetHomeRotationForOrientation(PreferredOrientation orientation)
        {
            Quaternion horizontalSideways =
                Quaternion.FromToRotation(Vector3.right, Vector3.up);

            switch (orientation)
            {
                case PreferredOrientation.HorizontalSideways:
                    return horizontalSideways;
                case PreferredOrientation.HorizontalForward:
                    return Quaternion.AngleAxis(90f, Vector3.up) * horizontalSideways;
                case PreferredOrientation.VerticalForward:
                    return Quaternion.identity;
                case PreferredOrientation.VerticalSideways:
                    return Quaternion.AngleAxis(90f, Vector3.up);
                default:
                    return Quaternion.identity;
            }
        }


        /// How strongly this mode is "Forward" for the user (higher = more Forward).
        /// Vertical: plane normal ~ head-right → classic L/R while looking forward.
        /// Horizontal: local forward in the horizontal plane, same idea.
        public static float GetOrientationForwardScore(PreferredOrientation orientation)
        {
            if (ViewpointScript.Head == null || App.Scene == null)
            {
                if (orientation == PreferredOrientation.VerticalForward
                    || orientation == PreferredOrientation.HorizontalForward)
                {
                    return 1.0f;
                }
                return 0.0f;
            }

            Quaternion rot_GS =
                App.Scene.Pose.rotation * GetHomeRotationForOrientation(orientation);

            bool horizontal =
                orientation == PreferredOrientation.HorizontalSideways
                || orientation == PreferredOrientation.HorizontalForward;

            Vector3 dir_GS = horizontal ? (rot_GS * Vector3.forward) : (rot_GS * Vector3.right);
            dir_GS.y = 0.0f;
            if (dir_GS.sqrMagnitude < 1e-6f)
            {
                return 0.0f;
            }
            dir_GS.Normalize();

            Vector3 headFlat = ViewpointScript.Head.forward;
            headFlat.y = 0.0f;
            if (headFlat.sqrMagnitude < 1e-6f)
            {
                return 0.0f;
            }
            headFlat.Normalize();

            Vector3 headRight = Vector3.Cross(Vector3.up, headFlat);
            if (headRight.sqrMagnitude < 1e-6f)
            {
                return 0.0f;
            }
            headRight.Normalize();

            float alongView = Mathf.Abs(Vector3.Dot(dir_GS, headFlat));
            float alongSide = Mathf.Abs(Vector3.Dot(dir_GS, headRight));
            return alongSide - alongView;
        }

        /// Prefer pairwise use via GetOrientationForwardScore. Kept for any leftover callers.
        public static bool IsOrientationFacingUser(PreferredOrientation orientation)
        {
            return GetOrientationForwardScore(orientation) >= 0.0f;
        }



        /// Applies preferred orientation in scene space; keeps current scene position.
        /// Used by reset, summon, show, and grab-release under orientation lock.
        private void ApplyPreferredOrientation_SS()
        {
            TrTransform xf_SS = App.Scene.AsScene[transform];
            xf_SS.rotation = GetPreferredHomeRotation_SS();
            xf_SS.scale = 1.0f;
            App.Scene.AsScene[transform] = xf_SS;
            transform.localScale = Vector3.one;
            if (m_NonScaleChild != null)
            {
                m_NonScaleChild.OnPosRotChanged();
            }
        }



        public bool HasSavedMirrorPose
        {
            get
            {
                return m_HasSavedMirrorPose;
            }
        }

        public void SaveMirrorPose()
        {
            m_SavedMirrorPose_SS = App.Scene.AsScene[transform];
            m_HasSavedMirrorPose = true;
            Debug.LogError(
                "MIRROR_POSE: Widget SAVE pos=" + m_SavedMirrorPose_SS.translation +
                " has=" + m_HasSavedMirrorPose);
        }

        public void ClearSavedMirrorPose()
        {
            m_HasSavedMirrorPose = false;
        }

        public void RecallMirrorPose()
        {
            if (!m_HasSavedMirrorPose)
            {
                Debug.LogError("MIRROR_POSE: Widget RECALL — hasSave false");
                return;
            }

            m_IsSpinningFreely = false;
            AngularVelocity_GS = Vector3.zero;

            Debug.LogError(
                "MIRROR_POSE: Widget RECALL before=" + App.Scene.AsScene[transform].translation +
                " target=" + m_SavedMirrorPose_SS.translation);

            App.Scene.AsScene[transform] = m_SavedMirrorPose_SS;
            transform.localScale = Vector3.one;
            if (m_NonScaleChild != null)
            {
                m_NonScaleChild.OnPosRotChanged();
            }

            Debug.LogError(
                "MIRROR_POSE: Widget RECALL after=" + App.Scene.AsScene[transform].translation);
        }

        public bool HasMirrorMoveUndo
        {
            get
            {
                return m_HasMirrorMoveUndo;
            }
        }

        /// Restores mirror pose from start of last grab that moved it. Does not affect strokes.
        public void UndoLastMirrorMove()
        {
            if (!m_HasMirrorMoveUndo)
            {
                return;
            }

            m_IsSpinningFreely = false;
            AngularVelocity_GS = Vector3.zero;

            App.Scene.AsScene[transform] = m_UndoMirrorPose_SS;
            transform.localScale = Vector3.one;
            if (m_NonScaleChild != null)
            {
                m_NonScaleChild.OnPosRotChanged();
            }
        }

        public void Spin(float xSpeed, float ySpeed, float zSpeed)
        {
            if (m_LockSpin)
            {
                return;
            }

            Vector3 requested_GS = App.Scene.Pose.rotation * new Vector3(xSpeed, ySpeed, zSpeed);
            // Keep only spin about reflection normal (in-plane spin).
            Vector3 normal_GS = ReflectionPlane.normal;
            Vector3 aboutNormal_GS = Vector3.Project(requested_GS, normal_GS);
            AngularVelocity_GS = aboutNormal_GS;
            m_IsSpinningFreely = aboutNormal_GS.magnitude > m_AngVelDampThreshold;
        }


        public Vector3 GetSpin()
        {
            return App.Scene.Pose.rotation.TrueInverse() * AngularVelocity_GS;
        }

        public override Vector3 CustomDimension
        {
            get
            {
                return m_AngularVelocity_LS;
            }
            set
            {
                m_AngularVelocity_LS = value;
                m_IsSpinningFreely = value.magnitude > m_AngVelDampThreshold;
            }
        }

        override protected void Awake()
        {
            base.Awake();
            if (App.Switchboard != null)
            {
                App.Switchboard.ToolChanged += OnSwitchboardToolChanged;
            }

            m_AngVelDampThreshold = 50f;

            //initialize beams
            for (int i = 0; i < m_GuideBeams.Length; ++i)
            {
                m_GuideBeams[i].m_Offset = m_GuideBeams[i].m_Beam.position - transform.position;
                m_GuideBeams[i].m_BaseScale = m_GuideBeams[i].m_Beam.localScale;
                m_GuideBeams[i].m_BeamRenderer = m_GuideBeams[i].m_Beam.GetComponent<Renderer>();
                m_GuideBeams[i].m_BeamRenderer.enabled = false;
            }

            m_GuideBeamShowRatio = 0.0f;

            if (m_Home != null)
            {
                m_Home.Init();
                m_Home.SetOwner(transform);
                m_Home.SetFixedPosition(App.Scene.AsScene[transform].translation);
            }

            m_CustomShowHide = true;
        }

        void OnSwitchboardToolChanged()
        {
            TickLocomotionDeselect();
        }

        public void SetMode(PointerManager.SymmetryMode rMode)
        {
            switch (rMode)
            {
                case PointerManager.SymmetryMode.SinglePlane:
                    m_LeftRightMesh.enabled = false;
                    for (int i = 0; i < m_GuideBeams.Length; ++i)
                    {
                        BeamDirection dir = m_GuideBeams[i].m_Direction;
                        bool isPerpendicular =
                            dir == BeamDirection.Left || dir == BeamDirection.Right;
                        if (isPerpendicular)
                        {
                            // Optional 3-axis: arm through the plane (normal direction).
                            m_GuideBeams[i].m_BeamRenderer.enabled = m_ShowThirdAxisLive;
                        }
                        else
                        {
                            // Planar arms (in the mirror plane).
                            m_GuideBeams[i].m_BeamRenderer.enabled = true;
                        }
                    }
                    break;
                case PointerManager.SymmetryMode.TwoHanded:
                case PointerManager.SymmetryMode.MultiMirror:
                case PointerManager.SymmetryMode.ScriptedSymmetryMode:
                    m_LeftRightMesh.enabled = false;
                    m_FrontBackMesh.enabled = true;
                    for (int i = 0; i < m_GuideBeams.Length; ++i)
                    {
                        m_GuideBeams[i].m_BeamRenderer.enabled = false;
                    }
                    if (PointerManager.m_Instance.m_CustomSymmetryType == PointerManager.CustomSymmetryType.Point)
                    {
                    }
                    break;
            }
        }


        protected override TrTransform GetDesiredTransform(TrTransform xf_GS)
        {
            TrTransform result = xf_GS;

            // Lock spin: freeze rotation entirely; user must use orientation snap tools to turn.
            if (m_LockSpin)
            {
                result.rotation = transform.rotation;
            }

            if (m_LockOrientation)
            {
                // Keep plane stable during drag; preferred snap applied on release.
                result.rotation = transform.rotation;

                if (SnapEnabled)
                {
                    TrTransform snapped = GetSnappedTransform(result);
                    result.translation = snapped.translation;
                    result.rotation = transform.rotation;
                }
            }
            else if (SnapEnabled)
            {
                result = GetSnappedTransform(xf_GS);
                if (m_LockSpin)
                {
                    result.rotation = transform.rotation;
                }
            }

            if (m_AxisLock != AxisLock.None)
            {
                Debug.LogError(
                    "MIRROR_AXIS: GetDesiredTransform lock=" + m_AxisLock +
                    " hasOrigin=" + m_HasAxisLockGrabOrigin);
            }

            result.translation = ApplyAxisLockTranslation_GS(result.translation);
            return result;
        }



        Vector3 ApplyAxisLockTranslation_GS(Vector3 desiredPos_GS)
        {
            if (m_AxisLock == AxisLock.None || !m_HasAxisLockGrabOrigin)
            {
                return desiredPos_GS;
            }

            Debug.LogError(
                "MIRROR_AXIS: ApplyTranslation lock=" + m_AxisLock +
                " origin_SS=" + m_AxisLockGrabOrigin_SS);

            Vector3 desired_SS = App.Scene.Pose.inverse.MultiplyPoint(desiredPos_GS);
            Vector3 origin_SS = m_AxisLockGrabOrigin_SS;
            Vector3 locked_SS = origin_SS;

            switch (m_AxisLock)
            {
                case AxisLock.X:
                    locked_SS.x = desired_SS.x;
                    locked_SS.y = origin_SS.y;
                    locked_SS.z = origin_SS.z;
                    break;
                case AxisLock.Y:
                    locked_SS.x = origin_SS.x;
                    locked_SS.y = desired_SS.y;
                    locked_SS.z = origin_SS.z;
                    break;
                case AxisLock.Z:
                    locked_SS.x = origin_SS.x;
                    locked_SS.y = origin_SS.y;
                    locked_SS.z = desired_SS.z;
                    break;
                case AxisLock.All:
                    locked_SS = origin_SS;
                    break;
            }

            return App.Scene.Pose.MultiplyPoint(locked_SS);
        }

        void CaptureAxisLockGrabOrigin_SS()
        {
            Vector3 pos_SS = App.Scene.AsScene[transform].translation;

            if (m_HasSavedMirrorPose
                && m_AxisLock != AxisLock.None
                && Vector3.Distance(pos_SS, m_SavedMirrorPose_SS.translation)
                    <= kAxisLockSavedPoseSnapMeters * App.METERS_TO_UNITS)
            {
                pos_SS = m_SavedMirrorPose_SS.translation;
            }

            m_AxisLockGrabOrigin_SS = pos_SS;
            m_HasAxisLockGrabOrigin = true;

            Debug.LogError(
                "MIRROR_AXIS: CaptureOrigin lock=" + m_AxisLock +
                " origin_SS=" + m_AxisLockGrabOrigin_SS +
                " hasOrigin=" + m_HasAxisLockGrabOrigin);
        }

        void ApplyAxisLockOnRelease_SS()
        {
            if (m_AxisLock == AxisLock.None || !m_HasAxisLockGrabOrigin)
            {
                return;
            }

            Debug.LogError(
                "MIRROR_AXIS: ReleaseProject lock=" + m_AxisLock +
                " origin_SS=" + m_AxisLockGrabOrigin_SS);

            TrTransform xf_SS = App.Scene.AsScene[transform];
            Vector3 p = xf_SS.translation;
            Vector3 o = m_AxisLockGrabOrigin_SS;

            switch (m_AxisLock)
            {
                case AxisLock.X:
                    p.y = o.y;
                    p.z = o.z;
                    break;
                case AxisLock.Y:
                    p.x = o.x;
                    p.z = o.z;
                    break;
                case AxisLock.Z:
                    p.x = o.x;
                    p.y = o.y;
                    break;
                case AxisLock.All:
                    p = o;
                    break;
            }

            xf_SS.translation = p;
            App.Scene.AsScene[transform] = xf_SS;
            if (m_NonScaleChild != null)
            {
                m_NonScaleChild.OnPosRotChanged();
            }
        }

        override protected void OnUpdate()
        {
            TickHyperspaceExitOnSceneScale();

            bool moved = m_UserInteracting;

            // Drive the top of the mirror towards room-space up, to keep the text readable
            // It's a bit obnoxious to do this when the user's grabbing it. Maybe we should
            // also not do this when the canvas is being manipulated?
            if (!m_UserInteracting && !m_IsSpinningFreely && !m_SnapDriftCancel
                && !m_LockOrientation
                && PointerManager.m_Instance.CurrentSymmetryMode != PointerManager.SymmetryMode.MultiMirror)
            {
                // Doing the rotation in object space makes it easier to prove that the
                // plane normal will never be affected.
                // NOTE: This assumes mirror-up is object-space-up
                // and mirror-normal is object-space-right (see ReflectionPlane.get)
                Vector3 up_OS = Vector3.up;
                Vector3 normal_OS = Vector3.right;

                Vector3 desiredUp_OS = transform.InverseTransformDirection(Vector3.up);
                float stability;
                float angle = MathUtils.GetAngleBetween(up_OS, desiredUp_OS, normal_OS, out stability);
                if (stability > .1f && Mathf.Abs(angle) > .05f)
                {
                    float delta = angle * m_SnapOrientationSpeed;
                    Quaternion qDelta_OS = Quaternion.AngleAxis(delta, normal_OS);
                    if (m_NonScaleChild != null)
                    {
                        var t = m_NonScaleChild;
                        t.localRotation = t.localRotation * qDelta_OS;
                    }
                    else
                    {
                        var t = transform;
                        t.localRotation = t.localRotation * qDelta_OS;
                    }
                }
            }

            // Rotation about ReflectionPlane.normal is purely visual and does
            // not affect the widget functionality. So when spinning, rotate about
            // normal until the widget is in a "natural" orientation. Natural is
            // defined as: one of the arms of the widget is aligned as closely as
            // possible to the axis of rotation.
            //
            // The spinning plane divides space into 2 (really 3) regions:
            // - Points that are always in front of or in back of the plane
            //   (two cones joined at the tip)
            // - Points that alternate between front and back of the plane
            //
            // Aligning one of the arms this way makes that arm/axis trace out
            // the boundary between these regions. Interestingly enough, the other
            // axis traces out a plane whose normal is the axis of rotation.
            if (IsSpinningFreely && !m_LockSpin)
            {
                float MAX_ROTATE_SPEED = 100f; // deg/sec
                float DECAY_TIME_SEC = .75f;   // Time to decay 63% towards goal
                Vector3 normal = ReflectionPlane.normal;
                Vector3 projected = AngularVelocity_GS;
                projected = projected - Vector3.Dot(normal, projected) * normal;
                float length = projected.magnitude;
                if (length > 1e-4f)
                {
                    projected /= length;

                    // arm to rotate towards projected; pick the one that's closest
                    // Choices are .up and .forward (and their negatives)
                    Vector3 arm =
                        (Mathf.Abs(Vector3.Dot(transform.up, projected)) >
                        Mathf.Abs(Vector3.Dot(transform.forward, projected)))
                            ? transform.up : transform.forward;
                    arm *= Mathf.Sign(Vector3.Dot(arm, projected));

                    // Rotate arm towards projected. Since both arm and projected
                    // are on the plane, the axis should be +normal or -normal.
                    Vector3 cross = Vector3.Cross(arm, projected);
                    Vector3 axis = normal * Mathf.Sign(Vector3.Dot(cross, normal));
                    float delta = Mathf.Asin(cross.magnitude) * Mathf.Rad2Deg;
                    float angle = (1f - Mathf.Exp(-Time.deltaTime / DECAY_TIME_SEC)) * delta;
                    angle = Mathf.Min(angle, MAX_ROTATE_SPEED * Time.deltaTime);
                    Quaternion q = Quaternion.AngleAxis(angle, axis);
                    transform.rotation = q * transform.rotation;
                    moved = true;
                }
            }

            if (moved && m_NonScaleChild != null)
            {
                m_NonScaleChild.OnPosRotChanged();
            }

            //if our transform changed, update the beams
            float fShowRatio = GetShowRatio();
            bool bInTransition = m_GuideBeamShowRatio != fShowRatio;
            if (bInTransition || transform.hasChanged)
            {
                for (int i = 0; i < m_GuideBeams.Length; ++i)
                {
                    Vector3 vTransformedOffset = transform.rotation * m_GuideBeams[i].m_Offset;
                    Vector3 vGuideBeamPos = transform.position + vTransformedOffset;
                    Vector3 vGuideBeamDir = GetBeamDirection(m_GuideBeams[i].m_Direction);

                    float fBeamLength = m_GuideBeamLength * App.METERS_TO_UNITS;
                    fBeamLength *= fShowRatio;

                    //position guide beam half way to hit point
                    Vector3 vHitPoint = vGuideBeamPos + (vGuideBeamDir * fBeamLength);
                    Vector3 vHalfWay = (vGuideBeamPos + vHitPoint) * 0.5f;
                    m_GuideBeams[i].m_Beam.position = vHalfWay;

                    //set scale to half the distance
                    Vector3 vScale = m_GuideBeams[i].m_BaseScale;
                    vScale.y = fBeamLength * 0.5f;

                    m_GuideBeams[i].m_Beam.localScale = vScale;
                }

                transform.hasChanged = false;
                m_GuideBeamShowRatio = fShowRatio;
            }

            if (PointerManager.m_Instance.CurrentSymmetryMode == PointerManager.SymmetryMode.MultiMirror)
            {
                DrawCustomSymmetryGuides();
            }
        }

        override public void Activate(bool bActive)
        {
            base.Activate(bActive);
            if (bActive && SnapEnabled)
            {
                for (int i = 0; i < m_GuideBeams.Length; ++i)
                {
                    m_GuideBeams[i].m_BeamRenderer.material.color = m_SnapColor;
                }
            }
            m_HintText.SetActive(bActive);
            m_TitleText.color = bActive ? Color.white : Color.grey;
        }

        Vector3 GetBeamDirection(BeamDirection rDir)
        {
            switch (rDir)
            {
                case BeamDirection.Up: return transform.up;
                case BeamDirection.Down: return -transform.up;
                case BeamDirection.Left: return -transform.right;
                case BeamDirection.Right: return transform.right;
                case BeamDirection.Front: return transform.forward;
                case BeamDirection.Back: return -transform.forward;
            }
            return transform.up;
        }

        override protected void OnUserBeginInteracting()
        {
            base.OnUserBeginInteracting();
            if (m_Home != null)
            {
                m_Home.gameObject.SetActive(true);
                m_Home.Reset();
            }

            m_PoseAtGrabBegin_SS = App.Scene.AsScene[transform];
            CaptureAxisLockGrabOrigin_SS();
            Debug.LogError("MIRROR_AXIS: BeginInteracting lock=" + m_AxisLock);
        }

        override protected void OnUserEndInteracting()
        {
            base.OnUserEndInteracting();
            if (m_Home != null)
            {
                m_Home.gameObject.SetActive(false);
            }

            Debug.LogError("MIRROR_AXIS: EndInteracting lock=" + m_AxisLock);

            ApplyAxisLockOnRelease_SS();
            m_HasAxisLockGrabOrigin = false;

            // If this grab actually moved the mirror, offer undo back to grab-start pose.
            {
                Vector3 delta = App.Scene.AsScene[transform].translation
                    - m_PoseAtGrabBegin_SS.translation;
                if (delta.magnitude > kMirrorUndoMoveEpsilonMeters * App.METERS_TO_UNITS)
                {
                    m_UndoMirrorPose_SS = m_PoseAtGrabBegin_SS;
                    m_HasMirrorMoveUndo = true;
                }
            }

            if (m_LockSpin)
            {
                m_IsSpinningFreely = false;
                AngularVelocity_GS = Vector3.zero;
            }
            if (m_LockOrientation)
            {
                m_IsSpinningFreely = false;
                AngularVelocity_GS = Vector3.zero;
                ApplyPreferredOrientation_SS();
                RefreshVisibleSlotGuides();
                return;
            }
            if (m_Home != null && m_Home.ShouldSnapHome())
            {
                ResetToHome();
            }
            RefreshVisibleSlotGuides();
        }





        public Mirror ToMirror()
        {
            return new Mirror
            {
                Transform = TrTransform.FromLocalTransform(transform),
            };
        }

        public void FromMirror(Mirror data)
        {
            transform.localPosition = data.Transform.translation;
            transform.localRotation = data.Transform.rotation;
            if (m_NonScaleChild != null)
            {
                m_NonScaleChild.OnPosRotChanged();
            }
        }

        public void ResetToHome()
        {
            m_IsSpinningFreely = false;

            TrTransform home_SS;
            if (m_Home != null)
            {
                home_SS = m_Home.m_Transform_SS;
            }
            else
            {
                // Home reference missing on prefab: keep current scene position, base rotation.
                home_SS = App.Scene.AsScene[transform];
                home_SS.rotation = Quaternion.identity;
                home_SS.scale = 1.0f;
            }

            if (m_LockOrientation)
            {
                home_SS.rotation = GetPreferredHomeRotation_SS();
            }

            App.Scene.AsScene[transform] = home_SS;
            transform.localScale = Vector3.one;
            if (m_NonScaleChild != null)
            {
                m_NonScaleChild.OnPosRotChanged();
            }
        }

        public void BringToUser()
        {
            // Get brush controller and place a little in front and a little higher.
            Vector3 controllerPos =
                InputManager.m_Instance.GetController(InputManager.ControllerName.Brush).position;
            Vector3 headPos = ViewpointScript.Head.position;
            Vector3 headToController = controllerPos - headPos;
            Vector3 offset = headToController.normalized * m_JumpToUserControllerOffsetDistance +
                Vector3.up * m_JumpToUserControllerYOffset;

            Quaternion rot_GS = transform.rotation;
            if (m_LockOrientation)
            {
                // Preferred orientation in scene space, expressed as global rotation.
                rot_GS = App.Scene.Pose.rotation * GetPreferredHomeRotation_SS();
            }

            TrTransform xf_GS = TrTransform.TR(controllerPos + offset, rot_GS);

            // The transform we built was global space, but we need it in widget local for the command.
            TrTransform newXf = TrTransform.FromTransform(m_NonScaleChild.parent).inverse * xf_GS;
            SketchMemoryScript.m_Instance.PerformAndRecordCommand(
                new MoveWidgetCommand(this, newXf, CustomDimension, final: true),
                discardIfNotMerged: false);
        }


        public void ApplyLiveMirrorTint()
        {
            if (!m_LiveMirrorTintEnabled)
            {
                return;
            }
            Color tint = m_LiveMirrorTint;
            if (m_FrontBackMesh != null)
            {
                ApplyTintToRenderer(m_FrontBackMesh, tint);
            }
            if (m_LeftRightMesh != null)
            {
                ApplyTintToRenderer(m_LeftRightMesh, tint);
            }
            if (m_GuideBeams != null)
            {
                for (int i = 0; i < m_GuideBeams.Length; ++i)
                {
                    if (m_GuideBeams[i].m_BeamRenderer != null)
                    {
                        ApplyTintToRenderer(m_GuideBeams[i].m_BeamRenderer, tint);
                    }
                }
            }
        }

        void ApplyTintToRenderer(Renderer rend, Color tint)
        {
            if (rend == null)
            {
                return;
            }
            Material[] mats = rend.materials;
            for (int i = 0; i < mats.Length; ++i)
            {
                if (mats[i] == null)
                {
                    continue;
                }
                if (mats[i].HasProperty("_Color"))
                {
                    mats[i].SetColor("_Color", tint);
                }
            }
            rend.materials = mats;
        }

        public override void Show(bool bShow, bool bPlayAudio = true)
        {
            base.Show(bShow, false);

            if (bShow)
            {
                // Slot 0 is reserved True Center. Write pose+occupied before
                // dest / tint / guide readers run, or dest=0 is valid-Has / no-pose.
                EnsureTrueCenterSlot();
                // -1 means jump target unset. Do not revive dest=0 on Show.
                if (m_TeleportDestIndex >= kMirrorSaveSlotCount
                    || (m_TeleportDestIndex >= 0
                        && m_TeleportDestIndex != kTrueCenterSlotIndex
                        && !m_MirrorSaveSlotOccupied[m_TeleportDestIndex]))
                {
                    m_TeleportDestIndex = -1;
                    m_TeleportJumpPending = false;
                }

                ApplyLiveMirrorTint();
                AudioManager.m_Instance.PlayMirrorSound(transform.position);

                // Do not apply preferred orientation on show/load — keeps spawn pose stable.
                // Orientation buttons + OnUserEndInteracting apply explicit reorient.

                TrTransform xf_SS = App.Scene.AsScene[transform];
                Debug.LogError(
                    "[SymmetryWidget.Show] MIRROR_SPAWN: Show scene pos=" + xf_SS.translation +
                    " euler=" + xf_SS.rotation.eulerAngles +
                    " scale=" + xf_SS.scale +
                    " world pos=" + transform.position);
            }

            RefreshVisibleSlotGuides();
        }


        public void DrawCustomSymmetryGuides()
        {
            List<LineRenderer> lrs = new List<LineRenderer>();
            var matrices = PointerManager.m_Instance.CustomMirrorMatrices;

            // This can get called before we've had a chance to set up matrices
            if (matrices.Count < 1)
            {
                PointerManager.m_Instance.CalculateMirrors();
                matrices = PointerManager.m_Instance.CustomMirrorMatrices;
            }

            float mirrorScale = PointerManager.m_Instance.GetCustomMirrorScale();

            lrs = m_SymmetryDomainParent.GetComponentsInChildren<LineRenderer>().ToList();
            foreach (var lr in lrs)
            {
                lr.gameObject.SetActive(false);
            }

            for (var i = 0; i < matrices.Count; i++)
            {
                var m0 = matrices[i];
                var m = transform.localToWorldMatrix * m0;
                // Scale the guides away from the origin
                m *= Matrix4x4.TRS(new Vector3(2, .5f, .05f), Quaternion.identity, new Vector3(0.5f, 0.4f, 0));
                matrices[i] = m;

                if (PointerManager.m_Instance.m_CustomSymmetryType == PointerManager.CustomSymmetryType.Wallpaper)
                {
                    LineRenderer lr;
                    if (i < lrs.Count)
                    {
                        lr = lrs[i];
                    }
                    else
                    {
                        var go = Instantiate(m_SymmetryDomainPrefab, m_SymmetryDomainParent);
                        lr = go.GetComponent<LineRenderer>();
                    }
                    lr.gameObject.SetActive(true);
                    // var path = PointerManager.m_Instance.CustomMirrorDomain;
                    float insetAmount = i == 0 ? .1f : .11f;  // Slightly different inset for the first one so it's visible even if overlapping
                    var path = InsetPolygon(PointerManager.m_Instance.CustomMirrorDomain, insetAmount);
                    var path3d = path.Select(v =>
                    {
                        var p = m0.MultiplyPoint3x4(v);
                        p *= mirrorScale;
                        return p;
                    }).ToArray();
                    lr.positionCount = path3d.Length;
                    lr.SetPositions(path3d);
                    if (i == 0)
                    {
                        lr.startColor = Color.white;
                        lr.endColor = Color.white;
                    }
                    else
                    {
                        lr.startColor = Color.blue;
                        lr.endColor = Color.blue;
                    }
                }
            }

            if (PointerManager.m_Instance.m_CustomSymmetryType != PointerManager.CustomSymmetryType.Wallpaper)
            {
                m_CustomSymmetryMaterial.color = Color.gray;
                m_CustomSymmetryMaterial.enableInstancing = true;
                m_CustomSymmetryMaterial.SetFloat(OutlineWidth, -0.01f);
                Graphics.DrawMeshInstanced(
                    m_CustomSymmetryMesh, 0, m_CustomSymmetryMaterial,
                    matrices, null, ShadowCastingMode.Off, false
                );
            }
        }

        public static List<Vector2> InsetPolygon(List<Vector2> originalPoly, float insetAmount)
        {
            insetAmount = -insetAmount;
            int Mod(int x, int m) { return (x % m + m) % m; }

            Vector2 offsetDir = Vector2.zero;

            // Create the Vector3 vertices
            List<Vector2> offsetPoly = new List<Vector2>();
            for (int i = 0; i < originalPoly.Count; i++)
            {
                if (insetAmount != 0)
                {
                    Vector2 tangent1 = (originalPoly[(i + 1) % originalPoly.Count] - originalPoly[i]).normalized;
                    Vector2 tangent2 = (originalPoly[i] - originalPoly[Mod(i - 1, originalPoly.Count)]).normalized;

                    Vector2 normal1 = new Vector2(-tangent1.y, tangent1.x).normalized;
                    Vector2 normal2 = new Vector2(-tangent2.y, tangent2.x).normalized;

                    offsetDir = (normal1 + normal2) / 2;
                    offsetDir *= insetAmount / offsetDir.magnitude;
                }
                offsetPoly.Add(new Vector2(originalPoly[i].x - offsetDir.x, originalPoly[i].y - offsetDir.y));
            }

            return offsetPoly;
        }


        public PreferredOrientation CurrentPreferredOrientation
        {
            get
            {
                return m_PreferredOrientation;
            }
        }

        public void SetPreferredOrientation(PreferredOrientation orientation, bool applyNow = true)
        {
            m_PreferredOrientation = orientation;
            if (applyNow)
            {
                m_IsSpinningFreely = false;
                AngularVelocity_GS = Vector3.zero;
                ApplyPreferredOrientation_SS();
            }
        }

        public void SetOrientationHorizontalSideways()
        {
            SetPreferredOrientation(PreferredOrientation.HorizontalSideways, applyNow: true);
        }

        public void SetOrientationHorizontalForward()
        {
            SetPreferredOrientation(PreferredOrientation.HorizontalForward, applyNow: true);
        }

        public void SetOrientationVerticalForward()
        {
            SetPreferredOrientation(PreferredOrientation.VerticalForward, applyNow: true);
        }

        public void SetOrientationVerticalSideways()
        {
            SetPreferredOrientation(PreferredOrientation.VerticalSideways, applyNow: true);
        }


        public AxisLock CurrentAxisLock
        {
            get
            {
                return m_AxisLock;
            }
        }

        public void SetAxisLock(AxisLock lockMode)
        {
            m_AxisLock = lockMode;
        }


        /// Sets lock, or clears to None if the same lock is requested again.
        public void ToggleAxisLock(AxisLock lockMode)
        {
            if (m_AxisLock == lockMode)
            {
                m_AxisLock = AxisLock.None;
            }
            else
            {
                m_AxisLock = lockMode;
            }
            Debug.LogError("MIRROR_AXIS: ToggleAxisLock -> " + m_AxisLock);
        }

        public void ToggleAxisLockX()
        {
            ToggleAxisLock(AxisLock.X);
        }

        public void ToggleAxisLockY()
        {
            ToggleAxisLock(AxisLock.Y);
        }

        public void ToggleAxisLockZ()
        {
            ToggleAxisLock(AxisLock.Z);
        }

        public void ToggleAxisLockAll()
        {
            ToggleAxisLock(AxisLock.All);
        }

        public void ClearAxisLock()
        {
            m_AxisLock = AxisLock.None;
            Debug.LogError("MIRROR_AXIS: ClearAxisLock -> None");
        }


        // ConstrainSpin and Constrain Mirror to 90 degree increments
        public bool LockOrientation
        {
            get
            {
                return m_LockOrientation;
            }
            set
            {
                m_LockOrientation = value;
            }
        }

        public bool LockSpin
        {
            get
            {
                return m_LockSpin;
            }
            set
            {
                m_LockSpin = value;
            }
        }

        public void ToggleLockOrientation()
        {
            m_LockOrientation = !m_LockOrientation;
            Debug.LogError(
                "[SymmetryWidget.ToggleLockOrientation] MIRROR_LOCK: Orientation -> " +
                m_LockOrientation);
        }

        public void ToggleLockSpin()
        {
            m_LockSpin = !m_LockSpin;
            Debug.LogError(
                "[SymmetryWidget.ToggleLockSpin] MIRROR_LOCK: Spin -> " + m_LockSpin);
        }

        // ================================
        // SECTION:  Mirror Save slots 
        // ================================

        public bool HasMirrorQuickReturn
        {
            get
            {
                return m_HasQuickReturn;
            }
        }

        public float GetLiveAsSceneScale()
        {
            float s = App.Scene.AsScene[transform].scale;
            if (s < 1e-4f)
            {
                return 1.0f;
            }
            return s;
        }

        public void SetLiveAsSceneScale(float scale)
        {
            if (scale < 1e-4f)
            {
                scale = 1.0f;
            }
            TrTransform xf = App.Scene.AsScene[transform];
            xf.scale = scale;
            App.Scene.AsScene[transform] = xf;
            if (m_NonScaleChild != null)
            {
                m_NonScaleChild.OnPosRotChanged();
            }
        }

        void ApplyMirrorPose_SS(TrTransform pose_SS)
        {
            m_IsSpinningFreely = false;
            AngularVelocity_GS = Vector3.zero;
            float keepScale = GetLiveAsSceneScale();
            pose_SS.scale = keepScale;
            App.Scene.AsScene[transform] = pose_SS;
            if (m_NonScaleChild != null)
            {
                m_NonScaleChild.OnPosRotChanged();
            }
        }


        void StashQuickReturnFromCurrent_SS()
        {
            m_QuickReturnPose_SS = App.Scene.AsScene[transform];
            m_HasQuickReturn = true;
            Debug.LogError(
                "[SymmetryWidget.StashQuickReturnFromCurrent_SS] MIRROR_SLOT: QR stash pos=" +
                m_QuickReturnPose_SS.translation +
                " euler=" + m_QuickReturnPose_SS.rotation.eulerAngles);
        }


        /// Sketch-origin / default mirror pose in scene space (placeholder until spawn is identified).
        /// Not user-editable.
        /// Default mirror spawn / sketch-origin reference in scene space.
        /// Measured from first Show: (0, 15, 0), identity rotation. Not user-editable.
        public TrTransform GetMirrorTrueCenterPose_SS()
        {
            TrTransform center = TrTransform.identity;
            center.translation = new Vector3(0.0f, 15.0f, 0.0f);
            center.rotation = Quaternion.identity;
            center.scale = 1.0f;

            return center;
        }


        public const int kTrueCenterSlotIndex = 0;

        public bool IsTrueCenterSlot(int index)
        {
            return index == kTrueCenterSlotIndex;
        }

        public void EnsureTrueCenterSlot()
        {
            m_MirrorSaveSlotPose_SS[kTrueCenterSlotIndex] = GetMirrorTrueCenterPose_SS();
            m_MirrorSaveSlotOccupied[kTrueCenterSlotIndex] = true;
        }

        public bool HasMirrorSaveSlot(int index)
        {
            if (index < 0 || index >= kMirrorSaveSlotCount)
            {
                return false;
            }
            if (IsTrueCenterSlot(index))
            {
                EnsureTrueCenterSlot();
                return true;
            }
            return m_MirrorSaveSlotOccupied[index];
        }


        static TrTransform SlotPoseWithoutScale(TrTransform pose_SS)
        {
            pose_SS.scale = 1.0f;
            return pose_SS;
        }

        public void SaveMirrorSaveSlot(int index)
        {
            if (index < 0 || index >= kMirrorSaveSlotCount)
            {
                return;
            }
            if (IsTrueCenterSlot(index))
            {
                EnsureTrueCenterSlot();
                Debug.LogError(
                    "MIRROR_SLOT: SAVE index=0 blocked (True Center reserved)");
                return;
            }
            m_MirrorSaveSlotPose_SS[index] = SlotPoseWithoutScale(App.Scene.AsScene[transform]);
            m_MirrorSaveSlotOccupied[index] = true;
            Debug.LogError(
                "MIRROR_SLOT: SAVE index=" + index +
                " pos=" + m_MirrorSaveSlotPose_SS[index].translation);
            SetTeleportDest(index);
        }

        public void RecallMirrorSaveSlot(int index)
        {
            if (index < 0 || index >= kMirrorSaveSlotCount)
            {
                return;
            }
            if (IsTrueCenterSlot(index))
            {
                ApplyMirrorTrueCenter();
                Debug.LogError("MIRROR_SLOT: RECALL index=0 True Center");
                return;
            }
            if (!m_MirrorSaveSlotOccupied[index])
            {
                Debug.LogError("MIRROR_SLOT: RECALL index=" + index + " empty");
                return;
            }
            StashQuickReturnFromCurrent_SS();
            ApplyMirrorPose_SS(m_MirrorSaveSlotPose_SS[index]);
            Debug.LogError(
                "MIRROR_SLOT: RECALL index=" + index +
                " pos=" + m_MirrorSaveSlotPose_SS[index].translation);
            SetTeleportDest(index);
            RefreshVisibleSlotGuides();
        }

        public void ClearMirrorSaveSlot(int index)
        {
            if (index < 0 || index >= kMirrorSaveSlotCount)
            {
                return;
            }
            if (IsTrueCenterSlot(index))
            {
                EnsureTrueCenterSlot();
                Debug.LogError("MIRROR_SLOT: CLEAR index=0 blocked (True Center reserved)");
                return;
            }
            if (!m_MirrorSaveSlotOccupied[index])
            {
                return;
            }
            m_LastClearedMirrorSaveSlotPose_SS = m_MirrorSaveSlotPose_SS[index];
            m_LastClearedMirrorSaveSlotIndex = index;
            m_HasLastClearedMirrorSaveSlot = true;
            m_MirrorSaveSlotOccupied[index] = false;
            Debug.LogError("MIRROR_SLOT: CLEAR index=" + index);
            NotifyMirrorSaveSlotCleared(index);
            HideSlotGuide(index);
            if (m_TeleportDestIndex == index)
            {
                SetTeleportDest(kTrueCenterSlotIndex);
            }
        }

        /// Restores last-cleared pose into this index if it matches. Returns true if restored.
        public bool TryRestoreLastClearedMirrorSaveSlot(int index)
        {
            if (index < 0 || index >= kMirrorSaveSlotCount)
            {
                return false;
            }
            if (!m_HasLastClearedMirrorSaveSlot || m_LastClearedMirrorSaveSlotIndex != index)
            {
                return false;
            }
            m_MirrorSaveSlotPose_SS[index] = m_LastClearedMirrorSaveSlotPose_SS;
            m_MirrorSaveSlotOccupied[index] = true;
            m_HasLastClearedMirrorSaveSlot = false;
            m_LastClearedMirrorSaveSlotIndex = -1;
            Debug.LogError("MIRROR_SLOT: RESTORE last-cleared index=" + index);
            return true;
        }

        public void ForceSlot1TrueCenter()
        {
            if (m_MirrorSaveSlotOccupied[kTrueCenterSlotIndex]
                && !SlotPoseIsTrueCenter(m_MirrorSaveSlotPose_SS[kTrueCenterSlotIndex]))
            {
                MigrateLegacySlot0ToFirstEmpty();
            }
            EnsureTrueCenterSlot();
        }

        public void ApplyMirrorTrueCenter()
        {
            ForceSlot1TrueCenter();
            StashQuickReturnFromCurrent_SS();
            ApplyMirrorPose_SS(GetMirrorTrueCenterPose_SS());
            SetTeleportDest(kTrueCenterSlotIndex);
            RefreshVisibleSlotGuides();
            Debug.LogError("MIRROR_SLOT: TRUE CENTER applied slot1");
        }



        public void ApplyMirrorQuickReturn()
        {
            if (!m_HasQuickReturn)
            {
                Debug.LogError(
                    "[SymmetryWidget.ApplyMirrorQuickReturn] MIRROR_SLOT: QUICK RETURN — empty");
                return;
            }
            Debug.LogError(
                "[SymmetryWidget.ApplyMirrorQuickReturn] MIRROR_SLOT: QUICK RETURN apply pos=" +
                m_QuickReturnPose_SS.translation +
                " euler=" + m_QuickReturnPose_SS.rotation.eulerAngles +
                " current euler=" + App.Scene.AsScene[transform].rotation.eulerAngles);
            ApplyMirrorPose_SS(m_QuickReturnPose_SS);
            Debug.LogError(
                "[SymmetryWidget.ApplyMirrorQuickReturn] MIRROR_SLOT: QUICK RETURN after pos=" +
                App.Scene.AsScene[transform].translation +
                " euler=" + App.Scene.AsScene[transform].rotation.eulerAngles);
        }


        // DO NOT CALL THIS UNLESS DISCUSSED BEFORE WE USE IT
        // This has the potential to wipe all saved positions which we may want on reload
        // in most cases we want to have the user do this consciously and manually
        // we may apply to a tool button.  Still being determined
        //We will want to move to more permanent storage for slots. It should be stored with the sketch itself.  
        // A new sketch should start without any saved entries(except the initial mirror position, whenever that is known).  
        // It's likely a specific setting determined in the code  
        // DO NOT CALL ClearAllMirrorSaveSlots UNLESS DISCUSSED BEFORE WE USE IT
        public void ClearAllMirrorSaveSlots()
        {
            for (int i = 0; i < kMirrorSaveSlotCount; ++i)
            {
                m_MirrorSaveSlotOccupied[i] = false;
            }
            EnsureTrueCenterSlot();
            m_HasLastClearedMirrorSaveSlot = false;
            m_LastClearedMirrorSaveSlotIndex = -1;
            m_HasQuickReturn = false;
            Debug.LogError("MIRROR_SLOT: ClearAll");
        }

        /// Occupied slots only. Scale is always 1 so sketch save cannot carry widget scale.
        public MirrorSaveSlotEntry[] ExportMirrorSaveSlots()
        {
            List<MirrorSaveSlotEntry> list = new List<MirrorSaveSlotEntry>();
            for (int i = 0; i < kMirrorSaveSlotCount; ++i)
            {
                if (!m_MirrorSaveSlotOccupied[i])
                {
                    continue;
                }
                MirrorSaveSlotEntry entry = new MirrorSaveSlotEntry();
                entry.Index = i;
                entry.Transform = SlotPoseWithoutScale(m_MirrorSaveSlotPose_SS[i]);
                list.Add(entry);
            }
            if (list.Count == 0)
            {
                return null;
            }
            return list.ToArray();
        }

        /// Replaces in-memory slots from metadata. Null/empty clears all user slots.
        /// Slot 1 is forced to True Center. Live glass and user stand snap to slot 1.
        /// Does not touch Quick Return / last-cleared.
        /// Exits Hyperspace so mode from the previous sketch cannot stick.
        public void ImportMirrorSaveSlots(MirrorSaveSlotEntry[] entries)
        {
            HardInvalidateMoveToMirrorAndExitMode();
            ApplySelectionSlideDefault();

            for (int i = 0; i < kMirrorSaveSlotCount; ++i)
            {
                m_MirrorSaveSlotOccupied[i] = false;
            }
            m_HasLastClearedMirrorSaveSlot = false;
            m_LastClearedMirrorSaveSlotIndex = -1;

            if (entries == null || entries.Length == 0)
            {
                EnsureTrueCenterSlot();
                Debug.LogError(
                    "[SymmetryWidget.ImportMirrorSaveSlots] MIRROR_SLOT: import empty + True Center");
                NotifySlotGuidesSceneReset();
                SnapLiveAndUserToTrueCenterAfterLoad();
                return;
            }

            for (int e = 0; e < entries.Length; ++e)
            {
                MirrorSaveSlotEntry entry = entries[e];
                if (entry == null)
                {
                    continue;
                }
                int index = entry.Index;
                if (index < 0 || index >= kMirrorSaveSlotCount)
                {
                    Debug.LogError(
                        "[SymmetryWidget.ImportMirrorSaveSlots] MIRROR_SLOT: bad index " +
                        index);
                    continue;
                }
                m_MirrorSaveSlotPose_SS[index] = SlotPoseWithoutScale(entry.Transform);
                m_MirrorSaveSlotOccupied[index] = true;
                Debug.LogError(
                    "[SymmetryWidget.ImportMirrorSaveSlots] MIRROR_SLOT: load index=" +
                    index + " pos=" + m_MirrorSaveSlotPose_SS[index].translation +
                    " scale=" + m_MirrorSaveSlotPose_SS[index].scale);
            }
            if (MigrateLegacySlot0ToFirstEmpty())
            {
                EnsureTrueCenterSlot();
            }
            WarnIfImportedSlotsExceedVisibleBank();
            EnsureTrueCenterSlot();
            Debug.LogError(
                "[SymmetryWidget.ImportMirrorSaveSlots] MIRROR_SLOT: import done + True Center");
            NotifySlotGuidesSceneReset();
            SnapLiveAndUserToTrueCenterAfterLoad();
        }

        void SnapLiveAndUserToTrueCenterAfterLoad()
        {
            EnsureTrueCenterSlot();
            float keepScale = GetLiveAsSceneScale();
            ApplyMirrorTrueCenter();
            SetLiveAsSceneScale(keepScale);
            SetTeleportDest(kTrueCenterSlotIndex);
            ClearTeleportJumpPending();

            if (App.Scene == null)
            {
                Debug.LogError(
                    "[SymmetryWidget.SnapLiveAndUserToTrueCenterAfterLoad] glass only");
                return;
            }

            TrTransform to_SS = GetMirrorTrueCenterPose_SS();
            Vector3 forward_SS = to_SS.rotation * Vector3.forward;
            Vector3 stand_SS = to_SS.translation
                - forward_SS * (0.7f * App.METERS_TO_UNITS);
            TrTransform standPose = TrTransform.TR(stand_SS, Quaternion.identity);
            Vector3 stand_GS = (App.Scene.Pose * standPose).translation;
            Vector3 head_GS = Vector3.zero;
            if (ViewpointScript.Head != null)
            {
                head_GS = ViewpointScript.Head.position;
            }
            else if (Camera.main != null)
            {
                head_GS = Camera.main.transform.position;
            }
            TrTransform sceneAfter = App.Scene.Pose;
            sceneAfter.translation += head_GS - stand_GS;
            float bounds = 100.0f;
            if (SceneSettings.m_Instance != null)
            {
                bounds = SceneSettings.m_Instance.HardBoundsRadiusMeters_SS;
            }
            App.Scene.Pose = SketchControlsScript.MakeValidScenePose(sceneAfter, bounds);
            Debug.LogError(
                "[SymmetryWidget.SnapLiveAndUserToTrueCenterAfterLoad] dest=0 keepScale=" +
                keepScale);
        }

        void ReportMirrorSlotBankProblem(string playerLine)
        {
            Debug.LogError(
                "[SymmetryWidget.ReportMirrorSlotBankProblem] MIRROR_SLOT: " + playerLine);
            try
            {
                if (OutputWindowScript.m_Instance != null)
                {
                    OutputWindowScript.m_Instance.CreateInfoCardAtController(
                        InputManager.ControllerName.Brush, playerLine);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError(
                    "[SymmetryWidget.ReportMirrorSlotBankProblem] MIRROR_SLOT: card failed " +
                    ex.GetType().Name + " " + ex.Message);
            }
        }

        void WarnIfImportedSlotsExceedVisibleBank()
        {
            int visible = m_MirrorSaveSlotVisibleCount;
            if (visible < 1)
            {
                visible = kMirrorSaveSlotCount;
            }
            if (visible > kMirrorSaveSlotCount)
            {
                visible = kMirrorSaveSlotCount;
            }
            int highest = -1;
            int occupied = 0;
            for (int i = 0; i < kMirrorSaveSlotCount; ++i)
            {
                if (!m_MirrorSaveSlotOccupied[i])
                {
                    continue;
                }
                occupied += 1;
                if (i > highest)
                {
                    highest = i;
                }
            }
            Debug.LogError(
                "[SymmetryWidget.WarnIfImportedSlotsExceedVisibleBank] MIRROR_SLOT: occupied=" +
                occupied + " highestIndex=" + highest + " visibleCount=" + visible);
            if (highest >= visible)
            {
                ReportMirrorSlotBankProblem(
                    "This sketch uses mirror slots this panel does not show. Use the advanced Mirror Controls panel.");
            }
        }

        bool SlotPoseIsTrueCenter(TrTransform pose_SS)
        {
            pose_SS = SlotPoseWithoutScale(pose_SS);
            TrTransform home = GetMirrorTrueCenterPose_SS();
            float eps = 0.01f * App.METERS_TO_UNITS;
            if ((pose_SS.translation - home.translation).sqrMagnitude > eps * eps)
            {
                return false;
            }
            return Quaternion.Angle(pose_SS.rotation, home.rotation) <= 1.0f;
        }

        bool MigrateLegacySlot0ToFirstEmpty()
        {
            if (!m_MirrorSaveSlotOccupied[kTrueCenterSlotIndex])
            {
                Debug.LogError(
                    "[SymmetryWidget.MigrateLegacySlot0ToFirstEmpty] slot1 empty, write TC");
                return true;
            }
            if (SlotPoseIsTrueCenter(m_MirrorSaveSlotPose_SS[kTrueCenterSlotIndex]))
            {
                Debug.LogError(
                    "[SymmetryWidget.MigrateLegacySlot0ToFirstEmpty] slot1 already TC");
                return true;
            }
            TrTransform legacy = SlotPoseWithoutScale(m_MirrorSaveSlotPose_SS[kTrueCenterSlotIndex]);
            int dest = -1;
            for (int i = kTrueCenterSlotIndex + 1; i < kMirrorSaveSlotCount; ++i)
            {
                if (!m_MirrorSaveSlotOccupied[i])
                {
                    dest = i;
                    break;
                }
            }
            if (dest < 0)
            {
                ReportMirrorSlotBankProblem(
                    "No empty mirror slot. Old slot 1 was kept. Use the full Mirror Controls panel.");
                return false;
            }
            m_MirrorSaveSlotPose_SS[dest] = legacy;
            m_MirrorSaveSlotOccupied[dest] = true;
            Debug.LogError(
                "[SymmetryWidget.MigrateLegacySlot0ToFirstEmpty] MIRROR_SLOT: moved old slot1 -> index=" +
                dest + " pos=" + legacy.translation);
            return true;
        }

        /// Clears user slots when starting a new sketch. Session buffers cleared too.
        /// True Center remains code-defined; live mirror pose is handled by ResetSymmetryToHome.
        public void ClearMirrorSaveSlotsForNewSketch()
        {
            for (int i = 0; i < kMirrorSaveSlotCount; ++i)
            {
                m_MirrorSaveSlotOccupied[i] = false;
            }
            m_HasLastClearedMirrorSaveSlot = false;
            m_LastClearedMirrorSaveSlotIndex = -1;
            m_HasQuickReturn = false;
            HardInvalidateMoveToMirrorAndExitMode();
            ApplySelectionSlideDefault();
            EnsureTrueCenterSlot();
            NotifySlotGuidesSceneReset();
            Debug.LogError(
                "[SymmetryWidget.ClearMirrorSaveSlotsForNewSketch] MIRROR_SLOT: cleared + True Center");
        }

        /// True if occupied slot rotation matches one of the four preferred homes within 1 degree.
        public bool IsMirrorSaveSlotAxisAligned(int index)
        {
            if (index < 0 || index >= kMirrorSaveSlotCount)
            {
                return false;
            }
            if (!m_MirrorSaveSlotOccupied[index])
            {
                return false;
            }

            const float kAlignedMaxDegrees = 1.0f;
            Quaternion saved = m_MirrorSaveSlotPose_SS[index].rotation;

            PreferredOrientation[] modes =
            {
                PreferredOrientation.HorizontalSideways,
                PreferredOrientation.HorizontalForward,
                PreferredOrientation.VerticalForward,
                PreferredOrientation.VerticalSideways
            };

            for (int i = 0; i < modes.Length; ++i)
            {
                Quaternion home = GetHomeRotationForOrientation(modes[i]);
                if (Quaternion.Angle(saved, home) <= kAlignedMaxDegrees)
                {
                    return true;
                }
            }
            return false;
        }


        /// True if occupied slot pose matches the live mirror (position + rotation).
        public bool IsMirrorSaveSlotMatchingCurrent(int index)
        {
            TrTransform saved;
            if (!TryGetMirrorSaveSlotPose(index, out saved))
            {
                return false;
            }

            TrTransform cur = App.Scene.AsScene[transform];

            float posEps = 0.01f * App.METERS_TO_UNITS;
            if ((cur.translation - saved.translation).sqrMagnitude > posEps * posEps)
            {
                return false;
            }

            const float kRotMaxDegrees = 1.0f;
            if (Quaternion.Angle(cur.rotation, saved.rotation) > kRotMaxDegrees)
            {
                return false;
            }

            return true;
        }


        // ================================
        // SECTION END:  Mirror Save slots 
        // ================================

        // SECTION:  MIROR TRANSLATION WITH OBJECTS AND BRUSH STROKES
        public bool PlaneLockActive
        {
            get
            {
                return m_PlaneLockActive;
            }
        }

        public void TogglePlaneLock()
        {
            m_PlaneLockActive = !m_PlaneLockActive;
            Debug.LogError(
                "[SymmetryWidget.TogglePlaneLock] PlaneLock=" + m_PlaneLockActive);
        }

        public void SetPlaneLock(bool on)
        {
            m_PlaneLockActive = on;
        }

        public bool TunnelLockActive
        {
            get
            {
                return m_TunnelLockActive;
            }
        }

        public void ToggleTunnelLock()
        {
            m_TunnelLockActive = !m_TunnelLockActive;
            Debug.LogError(
                "[SymmetryWidget.ToggleTunnelLock] TunnelLock=" + m_TunnelLockActive);
        }

        public void SetTunnelLock(bool on)
        {
            m_TunnelLockActive = on;
        }

        public void ApplySelectionSlideDefault()
        {
            switch (m_SelectionSlideDefault)
            {
                case SelectionSlideDefault.FrontBackOnly:
                    m_TunnelLockActive = true;
                    m_PlaneLockActive = false;
                    break;
                case SelectionSlideDefault.UpDownOnly:
                    m_TunnelLockActive = false;
                    m_PlaneLockActive = true;
                    break;
                case SelectionSlideDefault.AllowEither:
                    m_TunnelLockActive = true;
                    m_PlaneLockActive = true;
                    break;
                default:
                    m_TunnelLockActive = false;
                    m_PlaneLockActive = false;
                    break;
            }
            Debug.LogError(
                "[SymmetryWidget.ApplySelectionSlideDefault] default=" +
                m_SelectionSlideDefault +
                " tunnel=" + m_TunnelLockActive +
                " plane=" + m_PlaneLockActive);
        }

        public bool IsLiveSinglePlaneActive()
        {
            if (!LiveMirrorIsVisible())
            {
                return false;
            }
            if (PointerManager.m_Instance == null)
            {
                return false;
            }
            return PointerManager.m_Instance.CurrentSymmetryMode
                == PointerManager.SymmetryMode.SinglePlane;
        }

        public int MoveToMirrorTempFromIndex
        {
            get
            {
                return m_MoveToMirrorTempFromIndex;
            }
        }

        public void ClearMoveToMirrorTempFrom()
        {
            m_MoveToMirrorTempFromIndex = -1;
        }

        public void SetMoveToMirrorTempFrom(int index)
        {
            if (index < 0 || index >= kMirrorSaveSlotCount || !HasMirrorSaveSlot(index))
            {
                m_MoveToMirrorTempFromIndex = -1;
                return;
            }
            m_MoveToMirrorTempFromIndex = index;
        }

        public void RememberLastSendSelection(
            IEnumerable<Stroke> strokes, IEnumerable<GrabWidget> widgets)
        {
            m_LastSendStrokes.Clear();
            m_LastSendWidgets.Clear();
            if (strokes != null)
            {
                foreach (Stroke stroke in strokes)
                {
                    if (stroke != null)
                    {
                        m_LastSendStrokes.Add(stroke);
                    }
                }
            }
            if (widgets != null)
            {
                foreach (GrabWidget widget in widgets)
                {
                    if (widget != null)
                    {
                        m_LastSendWidgets.Add(widget);
                    }
                }
            }
        }

        public void ClearLastSendSelection()
        {
            m_LastSendStrokes.Clear();
            m_LastSendWidgets.Clear();
        }

        void DropLastSendIfSelectionGrew()
        {
            if (!m_MoveToMirrorModeActive)
            {
                return;
            }
            if (m_LastSendStrokes.Count == 0 && m_LastSendWidgets.Count == 0)
            {
                return;
            }
            if (SelectionManager.m_Instance == null
                || !SelectionManager.m_Instance.HasSelection)
            {
                return;
            }

            bool grew = false;
            foreach (Stroke stroke in SelectionManager.m_Instance.SelectedStrokes)
            {
                if (stroke != null && !m_LastSendStrokes.Contains(stroke))
                {
                    grew = true;
                    break;
                }
            }
            if (!grew)
            {
                foreach (GrabWidget widget in SelectionManager.m_Instance.SelectedWidgets)
                {
                    if (widget != null && !m_LastSendWidgets.Contains(widget))
                    {
                        grew = true;
                        break;
                    }
                }
            }
            if (!grew)
            {
                return;
            }

            List<Stroke> dropStrokes = new List<Stroke>();
            foreach (Stroke stroke in m_LastSendStrokes)
            {
                if (stroke != null && SelectionManager.m_Instance.IsStrokeSelected(stroke))
                {
                    dropStrokes.Add(stroke);
                }
            }
            List<GrabWidget> dropWidgets = new List<GrabWidget>();
            foreach (GrabWidget widget in m_LastSendWidgets)
            {
                if (widget != null && SelectionManager.m_Instance.IsWidgetSelected(widget))
                {
                    dropWidgets.Add(widget);
                }
            }

            if (dropStrokes.Count > 0)
            {
                SelectionManager.m_Instance.DeselectStrokes(dropStrokes);
            }
            if (dropWidgets.Count > 0)
            {
                SelectionManager.m_Instance.DeselectWidgets(dropWidgets);
            }

            Debug.LogError(
                "[SymmetryWidget.DropLastSendIfSelectionGrew] drop sent pile" +
                " strokes=" + dropStrokes.Count +
                " widgets=" + dropWidgets.Count);
            ClearLastSendSelection();

            int remainFrom;
            if (SelectionManager.m_Instance.HasSelection
                && TryGetNearestOccupiedSlotToSelection(out remainFrom))
            {
                m_MoveToMirrorTempFromIndex = remainFrom;
                Debug.LogError(
                    "[SymmetryWidget.DropLastSendIfSelectionGrew] tempFrom remain=" +
                    remainFrom);
            }
            else
            {
                m_MoveToMirrorTempFromIndex = -1;
            }
        }

        public void EnterMoveToMirrorMode()
        {
            m_MoveToMirrorModeActive = true;
            if (App.Scene != null)
            {
                m_HyperspaceSceneScaleAtEnter = App.Scene.Pose.scale;
            }
            m_MoveToMirrorPickingTo = false;
            m_MoveToMirrorToIndex = -1;
            m_MoveToMirrorTempFromIndex = -1;
            m_MoveToMirrorOriginFromIndex = -1;
            m_MoveToMirrorBlockActivate = false;
            int origin;
            if (TryResolveMoveToMirrorFrom(out origin))
            {
                m_MoveToMirrorOriginFromIndex = origin;
            }
        }

        void ClearMoveToMirrorModeFlags()
        {
            m_MoveToMirrorModeActive = false;
            m_MoveToMirrorPickingTo = false;
            m_MoveToMirrorToIndex = -1;
            m_MoveToMirrorTempFromIndex = -1;
            m_MoveToMirrorOriginFromIndex = -1;
            m_MoveToMirrorBlockActivate = false;
            ClearLastSendSelection();
        }

        void DeselectIfMovedFromHyperspaceOrigin()
        {
            if (m_MoveToMirrorTempFromIndex < 0)
            {
                return;
            }
            if (m_MoveToMirrorOriginFromIndex >= 0
                && m_MoveToMirrorTempFromIndex == m_MoveToMirrorOriginFromIndex)
            {
                return;
            }
            if (SelectionManager.m_Instance != null
                && SelectionManager.m_Instance.HasSelection)
            {
                SelectionManager.m_Instance.ClearActiveSelection();
                Debug.LogError(
                    "[SymmetryWidget.DeselectIfMovedFromHyperspaceOrigin] cleared tempFrom=" +
                    m_MoveToMirrorTempFromIndex + " origin=" + m_MoveToMirrorOriginFromIndex);
            }
        }

        public void ExitMoveToMirrorMode(bool keepJumpDest = false)
        {
            if (m_MoveToMirrorToIndex >= 0)
            {
                HideSlotGuide(m_MoveToMirrorToIndex);
            }
            DeselectIfMovedFromHyperspaceOrigin();
            ClearMoveToMirrorModeFlags();
            if (!keepJumpDest)
            {
                ClearTeleportDest();
            }
            RefreshVisibleSlotGuides();
        }

        public void BeginMoveToMirrorSetTo()
        {
            if (!m_MoveToMirrorModeActive)
            {
                return;
            }
            // Toggle off while still picking: drop To and dest link.
            if (m_MoveToMirrorPickingTo)
            {
                Debug.LogError(
                    "[SymmetryWidget.BeginMoveToMirrorSetTo] cancel pick To=" +
                    m_MoveToMirrorToIndex);
                ClearMoveToMirrorTo();
                ClearTeleportDest();
                return;
            }
            m_MoveToMirrorPickingTo = true;
            Debug.LogError(
                "[SymmetryWidget.BeginMoveToMirrorSetTo] picking To keep=" +
                m_MoveToMirrorToIndex);
            RefreshVisibleSlotGuides();
        }

        public void ClearMoveToMirrorTo()
        {
            if (m_MoveToMirrorToIndex >= 0)
            {
                HideSlotGuide(m_MoveToMirrorToIndex);
            }
            m_MoveToMirrorToIndex = -1;
            m_MoveToMirrorPickingTo = false;
            ClearTeleportDest();
            Debug.LogError(
                "[SymmetryWidget.ClearMoveToMirrorTo] Send/Clone unarmed");
            RefreshVisibleSlotGuides();
        }

        public void TryAssignMoveToMirrorTo(int index)
        {
            if (!m_MoveToMirrorModeActive || !m_MoveToMirrorPickingTo)
            {
                return;
            }
            if (!HasMirrorSaveSlot(index))
            {
                return;
            }

            int fromIndex;
            if (TryResolveMoveToMirrorFrom(out fromIndex) && fromIndex == index)
            {
                Debug.LogError(
                    "[SymmetryWidget.TryAssignMoveToMirrorTo] reject From==To index=" +
                    index);
                return;
            }

            int oldTo = m_MoveToMirrorToIndex;
            m_MoveToMirrorToIndex = index;
            m_MoveToMirrorPickingTo = false;
            SetTeleportDest(index);
            if (oldTo >= 0 && oldTo != index)
            {
                HideSlotGuide(oldTo);
            }
            if (m_SlotGuidesToggledOn)
            {
                RefreshVisibleSlotGuides();
            }
            else
            {
                RefreshHyperspaceToSlotGuide();
            }
        }

        public bool TryResolveMoveToMirrorFrom(out int fromIndex)
        {
            fromIndex = -1;
            if (m_MoveToMirrorModeActive
                && SelectionManager.m_Instance != null
                && SelectionManager.m_Instance.HasSelection
                && m_MoveToMirrorTempFromIndex >= 0
                && HasMirrorSaveSlot(m_MoveToMirrorTempFromIndex))
            {
                fromIndex = m_MoveToMirrorTempFromIndex;
                return true;
            }
            for (int i = 0; i < kMirrorSaveSlotCount; ++i)
            {
                if (HasMirrorSaveSlot(i) && IsMirrorSaveSlotMatchingCurrent(i))
                {
                    fromIndex = i;
                    return true;
                }
            }
            if (TryGetNearestOccupiedSlotToSelection(out fromIndex))
            {
                return true;
            }
            return false;
        }

        bool TryGetSelectionPos_SS(out Vector3 pos_SS)
        {
            pos_SS = Vector3.zero;
            if (SelectionManager.m_Instance == null
                || !SelectionManager.m_Instance.HasSelection)
            {
                return false;
            }
            Vector3 acc = Vector3.zero;
            int n = 0;
            foreach (Stroke stroke in SelectionManager.m_Instance.SelectedStrokes)
            {
                if (stroke == null || stroke.m_ControlPoints == null
                    || stroke.m_ControlPoints.Length == 0)
                {
                    continue;
                }
                CanvasScript canvas = stroke.Canvas;
                if (canvas == null)
                {
                    canvas = App.ActiveCanvas;
                }
                if (canvas == null)
                {
                    continue;
                }
                Vector3 world = canvas.Pose * stroke.m_ControlPoints[0].m_Pos;
                acc += App.Scene.Pose.inverse * world;
                n += 1;
                if (n >= 24)
                {
                    break;
                }
            }
            if (n == 0)
            {
                foreach (GrabWidget widget in SelectionManager.m_Instance.SelectedWidgets)
                {
                    if (widget == null)
                    {
                        continue;
                    }
                    acc += App.Scene.AsScene[widget.transform].translation;
                    n += 1;
                    if (n >= 8)
                    {
                        break;
                    }
                }
            }
            if (n == 0)
            {
                return false;
            }
            pos_SS = acc / n;
            return true;
        }

        bool TryGetNearestOccupiedSlotToSelection(out int index)
        {
            index = -1;
            Vector3 pos_SS;
            if (!TryGetSelectionPos_SS(out pos_SS))
            {
                return false;
            }
            float best = 1e12f;
            for (int i = 0; i < kMirrorSaveSlotCount; ++i)
            {
                TrTransform slotPose;
                if (!HasMirrorSaveSlot(i) || !TryGetMirrorSaveSlotPose(i, out slotPose))
                {
                    continue;
                }
                float d = (slotPose.translation - pos_SS).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    index = i;
                }
            }
            return index >= 0;
        }

        public bool PeekMoveToMirrorToValid()
        {
            return m_MoveToMirrorToIndex >= 0 && HasMirrorSaveSlot(m_MoveToMirrorToIndex);
        }

        public bool IsMoveToMirrorArmed()
        {
            if (!m_MoveToMirrorModeActive)
            {
                return false;
            }
            if (!PeekMoveToMirrorToValid())
            {
                return false;
            }

            int fromIndex;
            if (!TryResolveMoveToMirrorFrom(out fromIndex))
            {
                return false;
            }
            if (fromIndex == m_MoveToMirrorToIndex)
            {
                return false;
            }
            if (SelectionManager.m_Instance == null || !SelectionManager.m_Instance.HasSelection)
            {
                return false;
            }
            return true;
        }

        public void JumpToMoveToMirrorDestination()
        {
            if (!PeekMoveToMirrorToValid())
            {
                return;
            }
            RecallMirrorSaveSlot(m_MoveToMirrorToIndex);
        }

        public void HardInvalidateMoveToMirrorAndExitMode()
        {
            if (m_MoveToMirrorToIndex >= 0)
            {
                HideSlotGuide(m_MoveToMirrorToIndex);
            }
            DeselectIfMovedFromHyperspaceOrigin();
            ClearMoveToMirrorModeFlags();
            ClearTeleportDest();
        }

        public bool CanFormalizeMoveToMirrorActive()
        {
            if (m_MoveToMirrorBlockActivate)
            {
                return false;
            }
            if (!PeekMoveToMirrorToValid())
            {
                return false;
            }
            if (IsMirrorSaveSlotMatchingCurrent(m_MoveToMirrorToIndex))
            {
                return false;
            }
            return true;
        }

        public void NoteMoveToMirrorJumped()
        {
            m_MoveToMirrorBlockActivate = true;
            Debug.LogError(
                "[SymmetryWidget.NoteMoveToMirrorJumped] Activate locked until Hyperspace toggled");
        }

        public void SnapLiveMirrorToDestination()
        {
            if (!CanFormalizeMoveToMirrorActive())
            {
                return;
            }
            RecallMirrorSaveSlot(m_MoveToMirrorToIndex);
            Show(true);
            RefreshVisibleSlotGuides();
            Debug.LogError(
                "[SymmetryWidget.SnapLiveMirrorToDestination] live shown at To=" +
                m_MoveToMirrorToIndex);
        }

        // Translation validitation and Notification Clear
        public void NotifyMirrorSaveSlotCleared(int index)
        {
            if (!m_MoveToMirrorModeActive)
            {
                return;
            }
            if (index != m_MoveToMirrorToIndex)
            {
                return;
            }
            // Soft invalid while grace may restore; Tick decides hard exit.
            // Keep m_MoveToMirrorToIndex until grace ends or mode exits.
        }


        public void TickLocomotionDeselect()
        {
            bool loco = false;
            if (SketchSurfacePanel.m_Instance != null)
            {
                BaseTool.ToolType tool = SketchSurfacePanel.m_Instance.GetCurrentToolType();
                loco = tool == BaseTool.ToolType.FlyTool
                    || tool == BaseTool.ToolType.TeleportTool;
            }
            if (loco && !m_WasFlyToolLastTick)
            {
                if (SelectionManager.m_Instance != null
                    && SelectionManager.m_Instance.HasSelection)
                {
                    Debug.LogError(
                        "[SymmetryWidget.TickLocomotionDeselect] fly/teleport start -> deselect");
                    SelectionManager.m_Instance.ClearActiveSelection();
                }
                ClearLastSendSelection();
                m_MoveToMirrorTempFromIndex = -1;
            }
            m_WasFlyToolLastTick = loco;
        }

        public void NotifyLocomotionLikeFlyTeleport()
        {
            if (SelectionManager.m_Instance != null
                && SelectionManager.m_Instance.HasSelection)
            {
                SelectionManager.m_Instance.ClearActiveSelection();
            }
            ClearLastSendSelection();
            m_MoveToMirrorTempFromIndex = -1;
            Debug.LogError(
                "[SymmetryWidget.NotifyLocomotionLikeFlyTeleport] deselect and clear temp From");
        }

        // Live-glass teleport retired. Use TeleportToMirrorCommand (gold To).
        public void TeleportUserToLiveMirror()
        {
            Debug.LogError(
                "[SymmetryWidget.TeleportUserToLiveMirror] retired; use TeleportToMirrorCommand");
        }

        void TickHyperspaceExitOnSceneScale()
        {
            if (!m_MoveToMirrorModeActive)
            {
                return;
            }
            if (App.Scene == null)
            {
                return;
            }
            float sceneScale = App.Scene.Pose.scale;
            if (m_HyperspaceSceneScaleAtEnter < 0.0f)
            {
                m_HyperspaceSceneScaleAtEnter = sceneScale;
                return;
            }
            float baseline = m_HyperspaceSceneScaleAtEnter;
            if (baseline < 1e-4f)
            {
                baseline = 1.0f;
            }
            float ratio = sceneScale / baseline;
            if (ratio < 1.0f)
            {
                ratio = baseline / sceneScale;
            }
            // Ignore reset-view / pose jitter. Real pinch is a large ratio change.
            if (ratio < 1.20f)
            {
                return;
            }
            Debug.LogError(
                "[SymmetryWidget.TickHyperspaceExitOnSceneScale] scene scale " +
                m_HyperspaceSceneScaleAtEnter + " -> " + sceneScale +
                " ratio=" + ratio +
                " exit Hyperspace");
            ExitMoveToMirrorMode();
        }

        public void NoteHyperspaceSceneScale()
        {
            if (App.Scene == null)
            {
                return;
            }
            m_HyperspaceSceneScaleAtEnter = App.Scene.Pose.scale;
            Debug.LogError(
                "[SymmetryWidget.NoteHyperspaceSceneScale] baseline=" +
                m_HyperspaceSceneScaleAtEnter);
        }

        public void TickMoveToMirrorValidity()
        {
            if (!m_MoveToMirrorModeActive)
            {
                return;
            }

            TickHyperspaceExitOnSceneScale();
            if (!m_MoveToMirrorModeActive)
            {
                return;
            }

            DropLastSendIfSelectionGrew();

            if (SelectionManager.m_Instance == null
                || !SelectionManager.m_Instance.HasSelection)
            {
                if (m_MoveToMirrorTempFromIndex >= 0)
                {
                    Debug.LogError(
                        "[SymmetryWidget.TickMoveToMirrorValidity] clear temp From=" +
                        m_MoveToMirrorTempFromIndex + " (no selection)");
                }
                m_MoveToMirrorTempFromIndex = -1;
            }

            int fromIndex;
            if (!TryResolveMoveToMirrorFrom(out fromIndex))
            {
                // No selection / no From: keep mode and show-all. Send stays unarmed.
                return;
            }

            // To was set but slot no longer occupied and cannot restore -> hard exit
            if (m_MoveToMirrorToIndex >= 0 && !HasMirrorSaveSlot(m_MoveToMirrorToIndex))
            {
                bool canRestore =
                    m_HasLastClearedMirrorSaveSlot
                    && m_LastClearedMirrorSaveSlotIndex == m_MoveToMirrorToIndex;
                if (!canRestore)
                {
                    HardInvalidateMoveToMirrorAndExitMode();
                    return;
                }
            }

            RefreshHyperspaceToSlotGuide();
        }

        public bool TryGetMirrorSaveSlotPose(int index, out TrTransform pose_SS)
        {
            pose_SS = TrTransform.identity;
            if (index < 0 || index >= kMirrorSaveSlotCount)
            {
                return false;
            }
            if (IsTrueCenterSlot(index))
            {
                EnsureTrueCenterSlot();
                pose_SS = GetMirrorTrueCenterPose_SS();
                return true;
            }
            if (!m_MirrorSaveSlotOccupied[index])
            {
                return false;
            }
            pose_SS = SlotPoseWithoutScale(m_MirrorSaveSlotPose_SS[index]);
            return true;
        }


        // SLOT GUIDE

        bool SlotGuidePoolIsAlive()
        {
            if (m_SlotGuidePool == null || m_SlotGuidePool.Length != kMirrorSaveSlotCount)
            {
                return false;
            }
            if (m_SlotGuidePoolRoot == null)
            {
                return false;
            }
            if (m_SlotGuidePool[0] == null)
            {
                return false;
            }
            return true;
        }

        void DestroySlotGuidePool()
        {
            if (m_SlotGuidePool != null)
            {
                for (int i = 0; i < m_SlotGuidePool.Length; ++i)
                {
                    if (m_SlotGuidePool[i] != null)
                    {
                        Destroy(m_SlotGuidePool[i]);
                    }
                }
            }
            if (m_SlotGuidePoolRoot != null)
            {
                Destroy(m_SlotGuidePoolRoot.gameObject);
            }
            if (m_LatentLiveGuide != null)
            {
                Destroy(m_LatentLiveGuide);
                m_LatentLiveGuide = null;
            }
            m_SlotGuidePool = null;
            m_SlotGuidePoolRoot = null;
            m_SlotGuideActive = null;
            m_SlotGuideBeamRestLocalPos = null;
            m_SlotGuideBeamRestLocalScale = null;
            Debug.LogError(
                "[SymmetryWidget.DestroySlotGuidePool] SLOTGUIDE: pool destroyed");
        }

        void EnsureSlotGuidePool()
        {
            if (SlotGuidePoolIsAlive())
            {
                return;
            }

            Debug.LogError(
                "[SymmetryWidget.EnsureSlotGuidePool] SLOTGUIDE: rebuild prefab=" +
                (m_SlotGuidePrefab != null) +
                " scene=" + (App.Scene != null));

            if (m_SlotGuidePool != null || m_SlotGuidePoolRoot != null)
            {
                DestroySlotGuidePool();
            }

            if (m_SlotGuidePrefab == null)
            {
                Debug.LogError(
                    "[SymmetryWidget.EnsureSlotGuidePool] SLOTGUIDE: prefab not assigned");
                return;
            }
            if (App.Scene == null)
            {
                Debug.LogError(
                    "[SymmetryWidget.EnsureSlotGuidePool] SLOTGUIDE: App.Scene null");
                return;
            }

            GameObject root = new GameObject("SlotGuidePool");
            m_SlotGuidePoolRoot = root.transform;
            m_SlotGuidePoolRoot.SetParent(App.Scene.transform, false);

            m_SlotGuidePool = new GameObject[kMirrorSaveSlotCount];
            m_SlotGuideActive = new bool[kMirrorSaveSlotCount];
            m_SlotGuideBeamRestLocalPos = new Vector3[kMirrorSaveSlotCount][];
            m_SlotGuideBeamRestLocalScale = new Vector3[kMirrorSaveSlotCount][];
            for (int i = 0; i < kMirrorSaveSlotCount; ++i)
            {
                GameObject guide = Instantiate(m_SlotGuidePrefab, m_SlotGuidePoolRoot);
                guide.name = "SlotGuide_" + i;
                guide.SetActive(false);
                m_SlotGuidePool[i] = guide;
                m_SlotGuideActive[i] = false;
                CaptureSlotGuideBeamRest(i, guide.transform);
            }
            Debug.LogError(
                "[SymmetryWidget.EnsureSlotGuidePool] SLOTGUIDE: pool created count=" +
                kMirrorSaveSlotCount);
        }

        void CaptureSlotGuideBeamRest(int index, Transform guideRoot)
        {
            int count = kSlotGuideBeamNames.Length;
            m_SlotGuideBeamRestLocalPos[index] = new Vector3[count];
            m_SlotGuideBeamRestLocalScale[index] = new Vector3[count];
            for (int b = 0; b < count; ++b)
            {
                Transform beam = guideRoot.Find(kSlotGuideBeamNames[b]);
                if (beam == null)
                {
                    m_SlotGuideBeamRestLocalPos[index][b] = Vector3.zero;
                    m_SlotGuideBeamRestLocalScale[index][b] = Vector3.one;
                    continue;
                }
                m_SlotGuideBeamRestLocalPos[index][b] = beam.localPosition;
                m_SlotGuideBeamRestLocalScale[index][b] = beam.localScale;
            }
        }

        int FindSlotGuideBeamNameIndex(string childName)
        {
            for (int i = 0; i < kSlotGuideBeamNames.Length; ++i)
            {
                if (kSlotGuideBeamNames[i] == childName)
                {
                    return i;
                }
            }
            return -1;
        }

        int FindSlotGuidePoolIndex(Transform guideRoot)
        {
            if (m_SlotGuidePool == null || guideRoot == null)
            {
                return -1;
            }
            for (int i = 0; i < m_SlotGuidePool.Length; ++i)
            {
                if (m_SlotGuidePool[i] != null && m_SlotGuidePool[i].transform == guideRoot)
                {
                    return i;
                }
            }
            return -1;
        }

        float GetSlotGuideBeamLength()
        {
            float scale = m_SlotGuideBeamLengthScale;
            if (scale < 0.0f)
            {
                scale = 0.0f;
            }
            return m_GuideBeamLength * App.METERS_TO_UNITS * scale;
        }

        void ApplySlotGuideTint(GameObject guide, Color tint)
        {
            if (guide == null)
            {
                return;
            }
            float emit = m_SlotGuideEmissionScale;
            if (emit < 0.0f)
            {
                emit = 0.0f;
            }
            Color applied = tint;
            applied.r = tint.r * emit;
            applied.g = tint.g * emit;
            applied.b = tint.b * emit;
            MeshRenderer[] renderers = guide.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; ++i)
            {
                if (renderers[i] == null)
                {
                    continue;
                }
                renderers[i].material.SetColor("_Color", applied);
            }
        }

        public void TickSlotGuideSceneScale()
        {
            if (!AreAnySlotGuidesVisible())
            {
                return;
            }
            if (App.Scene == null)
            {
                return;
            }
            float sceneScale = App.Scene.Pose.scale;
            float liveWorld = GetLiveMirrorWorldScale();
            if (Mathf.Abs(sceneScale - m_LastSlotGuideSceneScale) < 1e-4f
                && Mathf.Abs(liveWorld - m_LastSlotGuideLiveWorldScale) < 1e-4f)
            {
                return;
            }
            m_LastSlotGuideSceneScale = sceneScale;
            m_LastSlotGuideLiveWorldScale = liveWorld;
            for (int i = 0; i < kMirrorSaveSlotCount; ++i)
            {
                if (m_SlotGuideActive == null || !m_SlotGuideActive[i])
                {
                    continue;
                }
                if (m_SlotGuidePool == null || m_SlotGuidePool[i] == null)
                {
                    continue;
                }
                TrTransform pose_SS;
                if (!TryGetMirrorSaveSlotPose(i, out pose_SS))
                {
                    continue;
                }
                SetSlotGuidePose(m_SlotGuidePool[i], pose_SS);
            }
        }

        float GetLiveShowScale()
        {
            // Local-to-scene size of the live widget. Scene pinch already
            // scales children; do not multiply by App.Scene.Pose.scale.
            float liveScale = App.Scene.AsScene[transform].scale;
            if (liveScale < 1e-4f)
            {
                liveScale = 1.0f;
            }
            return liveScale;
        }

        float GetSlotGuideDisplayScale()
        {
            float mul = m_SlotGuideDisplayScale;
            if (mul < 1e-4f)
            {
                mul = 1.0f;
            }
            return mul;
        }

        float GetLiveMirrorWorldScale()
        {
            Transform src = transform;
            if (m_NonScaleChild != null)
            {
                src = m_NonScaleChild.transform;
            }
            float s = src.lossyScale.x;
            if (s < 1e-4f)
            {
                s = 1.0f;
            }
            return s;
        }

        void ApplySlotGuideWorldScale(GameObject guide)
        {
            if (guide == null)
            {
                return;
            }
            float want = GetLiveMirrorWorldScale() * GetSlotGuideDisplayScale();
            float parent = 1.0f;
            if (guide.transform.parent != null)
            {
                parent = guide.transform.parent.lossyScale.x;
            }
            if (parent < 1e-4f)
            {
                parent = 1.0f;
            }
            guide.transform.localScale = Vector3.one * (want / parent);
        }

        void SetSlotGuidePose(GameObject guide, TrTransform pose_SS)
        {
            if (guide == null)
            {
                return;
            }
            TrTransform xf = pose_SS;
            xf.scale = 1.0f;
            App.Scene.AsScene[guide.transform] = xf;
            ApplySlotGuideWorldScale(guide);
            if (App.Scene != null)
            {
                m_LastSlotGuideSceneScale = App.Scene.Pose.scale;
            }
        }

        public bool IsSlotGuideCoincidentWithLive(int index)
        {
            TrTransform slotPose;
            if (!TryGetMirrorSaveSlotPose(index, out slotPose))
            {
                return false;
            }

            TrTransform live = App.Scene.AsScene[transform];
            float posEps = 0.01f * App.METERS_TO_UNITS;
            if ((live.translation - slotPose.translation).sqrMagnitude > posEps * posEps)
            {
                return false;
            }
            const float kRotMaxDegrees = 1.0f;
            if (Quaternion.Angle(live.rotation, slotPose.rotation) > kRotMaxDegrees)
            {
                return false;
            }
            return true;
        }

        public void ShowSlotGuide(int index, Color tint)
        {
            if (index < 0 || index >= kMirrorSaveSlotCount)
            {
                Debug.LogError(
                    "[SymmetryWidget.ShowSlotGuide] SLOTGUIDE: skip bad index=" + index);
                return;
            }
            if (!HasMirrorSaveSlot(index))
            {
                Debug.LogError(
                    "[SymmetryWidget.ShowSlotGuide] SLOTGUIDE: skip empty index=" + index);
                return;
            }
            // Show-all (and Hyperspace To) may place a mark on the live slot.
            // Tint chooses Regular / Latent / Hyperspace. Do not hide.

            EnsureSlotGuidePool();
            if (m_SlotGuidePool == null || m_SlotGuidePool[index] == null)
            {
                Debug.LogError(
                    "[SymmetryWidget.ShowSlotGuide] SLOTGUIDE: pool missing index=" + index);
                return;
            }

            TrTransform pose_SS;
            if (!TryGetMirrorSaveSlotPose(index, out pose_SS))
            {
                Debug.LogError(
                    "[SymmetryWidget.ShowSlotGuide] SLOTGUIDE: no pose index=" + index);
                return;
            }

            GameObject guide = m_SlotGuidePool[index];
            SetSlotGuidePose(guide, pose_SS);
            ConfigureSlotGuideVisuals(guide);
            ApplySlotGuideTint(guide, tint);
            guide.SetActive(true);
            m_SlotGuideActive[index] = true;

            TrTransform applied = App.Scene.AsScene[guide.transform];
            Debug.LogError(
                "[SymmetryWidget.ShowSlotGuide] SLOTGUIDE: shown index=" + index +
                " pos=" + pose_SS.translation +
                " liveScale=" + GetLiveShowScale() +
                " displayMul=" + m_SlotGuideDisplayScale +
                " appliedScale=" + applied.scale +
                " worldPos=" + guide.transform.position);
        }


        public void HideSlotGuide(int index)
        {
            if (index < 0 || index >= kMirrorSaveSlotCount)
            {
                return;
            }
            if (m_SlotGuidePool == null || m_SlotGuidePool[index] == null)
            {
                if (m_SlotGuideActive != null && index < m_SlotGuideActive.Length)
                {
                    m_SlotGuideActive[index] = false;
                }
                return;
            }
            m_SlotGuidePool[index].SetActive(false);
            m_SlotGuideActive[index] = false;
        }

        public void HideAllSlotGuides()
        {
            if (m_SlotGuidePool != null)
            {
                for (int i = 0; i < m_SlotGuidePool.Length; ++i)
                {
                    HideSlotGuide(i);
                }
            }
            HideLatentLiveGuide();
        }

        public bool SlotGuidesToggledOn
        {
            get
            {
                return m_SlotGuidesToggledOn;
            }
        }

        public bool AreAnySlotGuidesVisible()
        {
            if (m_SlotGuideActive == null)
            {
                return false;
            }
            for (int i = 0; i < m_SlotGuideActive.Length; ++i)
            {
                if (m_SlotGuideActive[i])
                {
                    return true;
                }
            }
            return false;
        }

        public void NotifySlotGuidesSceneReset()
        {
            Debug.LogError(
                "[SymmetryWidget.NotifySlotGuidesSceneReset] SLOTGUIDE: reset hide+destroy pool");
            m_SlotGuidesToggledOn = false;
            HideAllSlotGuides();
            DestroySlotGuidePool();
        }

        public void RefreshHyperspaceToSlotGuide()
        {
            int toIndex = m_MoveToMirrorToIndex;
            bool toValid = PeekMoveToMirrorToValid();

            // Drop leftover To marks. Changing To used to Show() the new
            // index and leave the previous index painted Hyperspace-gold.
            for (int i = 0; i < kMirrorSaveSlotCount; ++i)
            {
                if (i == toIndex && toValid && m_MoveToMirrorModeActive)
                {
                    continue;
                }
                if (!m_SlotGuidesToggledOn)
                {
                    HideSlotGuide(i);
                }
            }

            if (toValid && m_MoveToMirrorModeActive)
            {
                if (LiveMirrorIsActive() && IsSlotGuideCoincidentWithLive(toIndex))
                {
                    HideSlotGuide(toIndex);
                }
                else
                {
                    ShowSlotGuide(toIndex, m_SlotGuideHyperspace);
                }
            }
        }

        public void ToggleSlotGuides()
        {
            int occupied = 0;
            int coincident = 0;
            for (int i = 0; i < kMirrorSaveSlotCount; ++i)
            {
                if (HasMirrorSaveSlot(i))
                {
                    occupied += 1;
                    if (IsSlotGuideCoincidentWithLive(i))
                    {
                        coincident += 1;
                    }
                }
            }

            Debug.LogError(
                "[SymmetryWidget.ToggleSlotGuides] SLOTGUIDE: press occupied=" + occupied +
                " coincident=" + coincident +
                " anyVisible=" + AreAnySlotGuidesVisible() +
                " poolAlive=" + SlotGuidePoolIsAlive() +
                " liveScale=" + GetLiveShowScale() +
                " displayMul=" + m_SlotGuideDisplayScale +
                " livePos=" + App.Scene.AsScene[transform].translation);

            EnsureSlotGuidePool();
            if (m_SlotGuidesToggledOn)
            {
                Debug.LogError(
                    "[SymmetryWidget.ToggleSlotGuides] SLOTGUIDE: hide all");
                m_SlotGuidesToggledOn = false;
                HideAllSlotGuides();
                RefreshHyperspaceToSlotGuide();
                return;
            }
            m_SlotGuidesToggledOn = true;
            RefreshVisibleSlotGuides();
            Debug.LogError(
                "[SymmetryWidget.ToggleSlotGuides] SLOTGUIDE: show done" +
                " anyVisible=" + AreAnySlotGuidesVisible() +
                " liveOn=" + LiveMirrorIsVisible());
        }

        bool LiveMirrorIsActive()
        {
            if (!gameObject.activeInHierarchy)
            {
                return false;
            }
            if (PointerManager.m_Instance != null
                && PointerManager.m_Instance.CurrentSymmetryMode
                    == PointerManager.SymmetryMode.None)
            {
                return false;
            }
            return true;
        }

        bool LiveMirrorIsVisible()
        {
            if (!LiveMirrorIsActive())
            {
                return false;
            }
            if (!isActiveAndEnabled)
            {
                return false;
            }
            bool meshOn = false;
            if (m_FrontBackMesh != null && m_FrontBackMesh.enabled)
            {
                meshOn = true;
            }
            if (m_LeftRightMesh != null && m_LeftRightMesh.enabled)
            {
                meshOn = true;
            }
            if (m_FrontBackMesh == null && m_LeftRightMesh == null)
            {
                meshOn = true;
            }
            return meshOn;
        }

        bool IsHyperspaceToIndex(int index)
        {
            if (!m_MoveToMirrorModeActive)
            {
                return false;
            }
            if (!PeekMoveToMirrorToValid())
            {
                return false;
            }
            return index == m_MoveToMirrorToIndex;
        }

        bool TryGetSlotGuideTint(int index, out Color tint)
        {
            tint = m_SlotGuideRegular;
            if (!HasMirrorSaveSlot(index))
            {
                return false;
            }
            bool coincident = IsSlotGuideCoincidentWithLive(index);
            // Live glass owns that pose. Do not draw a second plane or beams
            // (True Center included). Panel current-tint is the live indicator.
            if (coincident && LiveMirrorIsActive())
            {
                return false;
            }
            if (IsHyperspaceToIndex(index))
            {
                tint = m_SlotGuideHyperspace;
                return true;
            }
            if (coincident)
            {
                tint = m_SlotGuideLatent;
                return true;
            }
            tint = m_SlotGuideRegular;
            return true;
        }

        public void RefreshVisibleSlotGuides()
        {
            if (!m_SlotGuidesToggledOn)
            {
                HideLatentLiveGuide();
                return;
            }
            for (int i = 0; i < kMirrorSaveSlotCount; ++i)
            {
                Color tint;
                if (!TryGetSlotGuideTint(i, out tint))
                {
                    HideSlotGuide(i);
                    continue;
                }
                ShowSlotGuide(i, tint);
            }
            UpdateLatentLiveGuide();
        }

        bool AnySlotCoincidentWithLive()
        {
            for (int i = 0; i < kMirrorSaveSlotCount; ++i)
            {
                if (HasMirrorSaveSlot(i) && IsSlotGuideCoincidentWithLive(i))
                {
                    return true;
                }
            }
            return false;
        }

        void HideLatentLiveGuide()
        {
            if (m_LatentLiveGuide != null)
            {
                m_LatentLiveGuide.SetActive(false);
            }
        }

        void UpdateLatentLiveGuide()
        {
            // Latent marks the hidden live widget only. If that pose is already a
            // saved slot, TryGetSlotGuideTint paints that slot Latent and we skip this.
            if (!m_SlotGuidesToggledOn || LiveMirrorIsActive() || AnySlotCoincidentWithLive())
            {
                HideLatentLiveGuide();
                return;
            }
            EnsureSlotGuidePool();
            if (m_SlotGuidePrefab == null || m_SlotGuidePoolRoot == null)
            {
                return;
            }
            if (m_LatentLiveGuide == null)
            {
                m_LatentLiveGuide = Instantiate(m_SlotGuidePrefab, m_SlotGuidePoolRoot);
                m_LatentLiveGuide.name = "SlotGuide_LatentLive";
            }
            TrTransform live = App.Scene.AsScene[transform];
            SetSlotGuidePose(m_LatentLiveGuide, live);
            ConfigureSlotGuideVisuals(m_LatentLiveGuide);
            ApplySlotGuideTint(m_LatentLiveGuide, m_SlotGuideLatent);
            m_LatentLiveGuide.SetActive(true);
        }



        void LayoutSlotGuideBeams(GameObject guide)
        {
            if (guide == null)
            {
                return;
            }

            float beamLength = m_GuideBeamLength * App.METERS_TO_UNITS;
            if (beamLength < 1e-4f)
            {
                beamLength = 1.0f * App.METERS_TO_UNITS;
            }

            LayoutOneSlotGuideBeam(guide.transform, "GuideBeamUp", guide.transform.up, beamLength);
            LayoutOneSlotGuideBeam(guide.transform, "GuideBeamDown", -guide.transform.up, beamLength);
            LayoutOneSlotGuideBeam(guide.transform, "GuideBeamLeft", -guide.transform.right, beamLength);
            LayoutOneSlotGuideBeam(guide.transform, "GuideBeamRight", guide.transform.right, beamLength);
            LayoutOneSlotGuideBeam(guide.transform, "GuideBeamFront", guide.transform.forward, beamLength);
            LayoutOneSlotGuideBeam(guide.transform, "GuideBeamBack", -guide.transform.forward, beamLength);
        }

        void LayoutOneSlotGuideBeam(
            Transform guideRoot, string childName, Vector3 worldDir, float beamLength)
        {
            Transform beam = guideRoot.Find(childName);
            if (beam == null)
            {
                return;
            }
            if (beamLength < 1e-4f)
            {
                return;
            }

            int poolIndex = FindSlotGuidePoolIndex(guideRoot);
            int beamIndex = FindSlotGuideBeamNameIndex(childName);
            Vector3 restLocalPos = beam.localPosition;
            Vector3 restLocalScale = beam.localScale;
            if (poolIndex >= 0 && beamIndex >= 0
                && m_SlotGuideBeamRestLocalPos != null
                && m_SlotGuideBeamRestLocalPos[poolIndex] != null)
            {
                restLocalPos = m_SlotGuideBeamRestLocalPos[poolIndex][beamIndex];
                restLocalScale = m_SlotGuideBeamRestLocalScale[poolIndex][beamIndex];
            }

            worldDir.Normalize();
            Vector3 start = guideRoot.TransformPoint(restLocalPos);
            Vector3 hit = start + worldDir * beamLength;
            beam.position = (start + hit) * 0.5f;

            Vector3 scale = restLocalScale;
            scale.y = beamLength * 0.5f;
            beam.localScale = scale;
        }


        void ConfigureSlotGuideVisuals(GameObject guide)
        {
            if (guide == null)
            {
                return;
            }

            // One plane square; never the second plane mesh.
            SetSlotGuideChildRendererEnabled(guide.transform, "MeshFrontBack", true);
            SetSlotGuideChildRendererEnabled(guide.transform, "MeshLeftRight", false);

            // Planar arms (in the plane of the square).
            SetSlotGuideChildRendererEnabled(guide.transform, "GuideBeamUp", true);
            SetSlotGuideChildRendererEnabled(guide.transform, "GuideBeamDown", true);
            SetSlotGuideChildRendererEnabled(guide.transform, "GuideBeamFront", true);
            SetSlotGuideChildRendererEnabled(guide.transform, "GuideBeamBack", true);

            LayoutSlotGuidePlanarBeams(guide);

            // Optional 3-axis: perpendicular arms only (not a second plane square).
            SetSlotGuideChildRendererEnabled(
                guide.transform, "GuideBeamLeft", m_ShowThirdAxisSlotGuide);
            SetSlotGuideChildRendererEnabled(
                guide.transform, "GuideBeamRight", m_ShowThirdAxisSlotGuide);

            if (m_ShowThirdAxisSlotGuide)
            {
                LayoutSlotGuidePerpendicularBeams(guide);
            }
        }

        void LayoutSlotGuideThirdAxisBeams(GameObject guide)
        {
            if (guide == null)
            {
                return;
            }
            float beamLength = m_GuideBeamLength * App.METERS_TO_UNITS;
            if (beamLength < 1e-4f)
            {
                beamLength = 1.0f * App.METERS_TO_UNITS;
            }
            LayoutOneSlotGuideBeam(
                guide.transform, "GuideBeamLeft", -guide.transform.right, beamLength);
            LayoutOneSlotGuideBeam(
                guide.transform, "GuideBeamRight", guide.transform.right, beamLength);
        }

        void SetSlotGuideChildRendererEnabled(Transform guideRoot, string childName, bool enabled)
        {
            Transform child = guideRoot.Find(childName);
            if (child == null)
            {
                return;
            }
            MeshRenderer renderer = child.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.enabled = enabled;
            }
            child.gameObject.SetActive(enabled);
        }

        void LayoutSlotGuidePlanarBeams(GameObject guide)
        {
            if (guide == null)
            {
                return;
            }

            float beamLength = GetSlotGuideBeamLength();
            if (beamLength < 1e-4f)
            {
                return;
            }

            LayoutOneSlotGuideBeam(
                guide.transform, "GuideBeamUp", guide.transform.up, beamLength);
            LayoutOneSlotGuideBeam(
                guide.transform, "GuideBeamDown", -guide.transform.up, beamLength);
            LayoutOneSlotGuideBeam(
                guide.transform, "GuideBeamFront", guide.transform.forward, beamLength);
            LayoutOneSlotGuideBeam(
                guide.transform, "GuideBeamBack", -guide.transform.forward, beamLength);
        }

        void LayoutSlotGuidePerpendicularBeams(GameObject guide)
        {
            if (guide == null)
            {
                return;
            }

            float beamLength = GetSlotGuideBeamLength();
            if (beamLength < 1e-4f)
            {
                return;
            }

            LayoutOneSlotGuideBeam(
                guide.transform, "GuideBeamLeft", -guide.transform.right, beamLength);
            LayoutOneSlotGuideBeam(
                guide.transform, "GuideBeamRight", guide.transform.right, beamLength);
        }



    } // Functions Completed


} // namespace TiltBrush


