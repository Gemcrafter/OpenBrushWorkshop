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

using UnityEngine;

namespace TiltBrush
{
    /// Mirror orientation, axis lock, pose save/recall/undo.
    /// Prefab registration (PanelType, panel map, launch button) is Unity-side work.
    public class MirrorControlsPanel : BasePanel
    {


        [Header("Mirror panel tints")]
        [Tooltip("Occupied slot that is not current, not dest/To, and axis-aligned.")]
        [SerializeField] Color m_ActiveTint = new Color(0f, 102f / 255f, 0f, 1f);
        [Tooltip("Empty slot, or a mode button that is off.")]
        [SerializeField] Color m_InactiveTint = new Color(115f / 255f, 115f / 255f, 115f / 255f, 1f);
        [Tooltip("Occupied slot whose saved rotation is not one of the four home orientations.")]
        [SerializeField] Color m_AngledSlotTint = new Color(247f / 255f, 2f / 255f, 174f / 255f, 1f);
        [Tooltip("Slot whose saved pose matches the live glass. World slot-guide is hidden while the glass is on.")]
        [SerializeField] Color m_CurrentSlotTint = new Color(5f / 255f, 144f / 255f, 250f / 255f, 1f);
        [Tooltip("Hyperspace To while Hyperspace is on. Jump dest while Hyperspace is off and dest is not the live pose.")]
        [SerializeField] Color m_MoveToMirrorToTint = new Color(1f, 200f / 255f, 0f, 1f);

        Color MirrorControlsMoveToMirrorToTint
        {
            get
            {
                return m_MoveToMirrorToTint;
            }
        }

        Color MirrorControlsCurrentSlotTint
        {
            get
            {
                return m_CurrentSlotTint;
            }
        }

        Color MirrorControlsAngledSlotTint
        {
            get
            {
                return m_AngledSlotTint;
            }
        }

        Color MirrorControlsActiveTint
        {
            get
            {
                return m_ActiveTint;
            }
        }

        Color MirrorControlsInactiveTint
        {
            get
            {
                return m_InactiveTint;
            }
        }


        // DECLARATIONS
        bool m_MirrorSaveSlotClearMode;


        override protected void OnEnablePanel()
        {
            base.OnEnablePanel();
            RefreshMirrorControlsPanelTints();
            RefreshAxisLockButtonDescriptions();
            RefreshOrientationButtonDescriptions();
            RefreshMirrorSaveSlotTints();
        }

        override public void OnUpdatePanel(Vector3 vToPanel, Vector3 vHitPoint)
        {
            base.OnUpdatePanel(vToPanel, vHitPoint);
            SymmetryWidget widget = FindSymmetryWidget();
            if (widget != null)
            {
                widget.TickLocomotionDeselect();
                widget.TickMoveToMirrorValidity();
                widget.TickSlotGuideSceneScale();
            }
            RefreshAxisLockButtonDescriptions();
            RefreshOrientationButtonDescriptions();
            RefreshMirrorSaveSlotTints();
        }

        public void NotifyDismissedByUser()
        {
            SymmetryWidget mirror = FindSymmetryWidget();
            if (mirror != null && mirror.MoveToMirrorModeActive)
            {
                Debug.LogError(
                    "[MirrorControlsPanel.NotifyDismissedByUser] close or toss -> exit Hyperspace");
                mirror.ExitMoveToMirrorMode();
            }
        }

        public static void CloseAllOpenInstances()
        {
            MirrorControlsPanel[] panels = FindObjectsOfType<MirrorControlsPanel>();
            for (int i = 0; i < panels.Length; ++i)
            {
                if (panels[i] != null)
                {
                    panels[i].ClosePanel();
                }
            }

            AdvancedLaunchPanel.MirrorControlsLaunchOpen = false;
        }

        public void ClosePanel()
        {
            m_Fixed = false;
            NotifyDismissedByUser();
            AdvancedLaunchPanel.MirrorControlsLaunchOpen = false;

            PanelWidget widget = GetComponent<PanelWidget>();
            if (widget != null)
            {
                widget.Show(false);
                return;
            }

            gameObject.SetActive(false);
        }

        static SymmetryWidget FindSymmetryWidget()
        {
            if (PointerManager.m_Instance == null)
            {
                return null;
            }
            return PointerManager.m_Instance.SymmetryWidget;
        }

        static readonly Color kMirrorControlsPanelActiveTint =
            new Color(0x00 / 255f, 0x66 / 255f, 0x00 / 255f, 1f);
        static readonly Color kMirrorControlsPanelInactiveTint =
            new Color(0.45f, 0.45f, 0.45f, 1f);

        static void ForceMirrorControlsPanelButtonTint(ActionToggleButton button, Color tint)
        {
            if (button == null)
            {
                return;
            }
            Renderer renderer = button.GetComponent<Renderer>();
            if (renderer == null || renderer.material == null)
            {
                return;
            }
            renderer.material.SetColor("_Color", tint);
            if (renderer.material.HasProperty("_SecondaryColor"))
            {
                renderer.material.SetColor("_SecondaryColor", tint);
            }
        }



        void RefreshMirrorControlsPanelTints()
        {
            SymmetryWidget widget = FindSymmetryWidget();
            SymmetryWidget.PreferredOrientation orient =
                widget != null
                    ? widget.CurrentPreferredOrientation
                    : SymmetryWidget.PreferredOrientation.VerticalForward;
            SymmetryWidget.AxisLock axis =
                widget != null ? widget.CurrentAxisLock : SymmetryWidget.AxisLock.None;

            ActionToggleButton[] toggles = GetComponentsInChildren<ActionToggleButton>(true);
            for (int i = 0; i < toggles.Length; ++i)
            {
                if (toggles[i] == null)
                {
                    continue;
                }

                string name = toggles[i].gameObject.name;
                bool active = false;
                bool isMirrorModeControl = true;

                switch (name)
                {
                    case "SetMirrorHorizontalSideways":
                        active = orient == SymmetryWidget.PreferredOrientation.HorizontalSideways;
                        break;
                    case "SetMirrorHorizontalForward":
                        active = orient == SymmetryWidget.PreferredOrientation.HorizontalForward;
                        break;
                    case "SetMirrorVerticalForward":
                        active = orient == SymmetryWidget.PreferredOrientation.VerticalForward;
                        break;
                    case "SetMirrorVerticalSideways":
                        active = orient == SymmetryWidget.PreferredOrientation.VerticalSideways;
                        break;
                    case "Left-Right-X":
                        active = axis == SymmetryWidget.AxisLock.X;
                        break;
                    case "Up-Down-Y":
                        active = axis == SymmetryWidget.AxisLock.Y;
                        break;
                    case "Forward-Back-Z":
                        active = axis == SymmetryWidget.AxisLock.Z;
                        break;
                    case "LockMirrorMovement":
                        active = axis == SymmetryWidget.AxisLock.All;
                        break;
                    default:
                        isMirrorModeControl = false;
                        break;
                }

                if (!isMirrorModeControl)
                {
                    continue;
                }

                ForceMirrorControlsPanelButtonTint(
                    toggles[i],
                    active ? kMirrorControlsPanelActiveTint : kMirrorControlsPanelInactiveTint);
            }
        }


        public void SaveMirrorPose()
        {
            SymmetryWidget widget = FindSymmetryWidget();
            if (widget == null)
            {
                Debug.LogError("MIRROR_POSE: Save — SymmetryWidget is null");
                return;
            }
            widget.SaveMirrorPose();
            Debug.LogError("MIRROR_POSE: Save — OK");
        }

        public void RecallMirrorPose()
        {
            SymmetryWidget widget = FindSymmetryWidget();
            if (widget == null)
            {
                Debug.LogError("MIRROR_POSE: Recall — SymmetryWidget is null");
                return;
            }
            if (!widget.HasSavedMirrorPose)
            {
                Debug.LogError("MIRROR_POSE: Recall — nothing saved");
                return;
            }
            widget.RecallMirrorPose();
            Debug.LogError("MIRROR_POSE: Recall — OK");
        }


        public void UndoMirrorMove()
        {
            SymmetryWidget widget = FindSymmetryWidget();
            if (widget == null)
            {
                Debug.LogError("MIRROR_UNDO: SymmetryWidget null");
                return;
            }
            if (!widget.HasMirrorMoveUndo)
            {
                Debug.LogError("MIRROR_UNDO: nothing to undo");
                return;
            }
            widget.UndoLastMirrorMove();
            Debug.LogError("MIRROR_UNDO: applied");
        }

        public void SetMirrorHorizontalSideways()
        {
            SymmetryWidget widget = FindSymmetryWidget();
            if (widget == null)
            {
                Debug.LogError("MIRROR_ORIENT: HorizontalSideways — SymmetryWidget null");
                return;
            }
            widget.SetOrientationHorizontalSideways();
            RefreshMirrorControlsPanelTints();
        }

        public void SetMirrorHorizontalForward()
        {
            SymmetryWidget widget = FindSymmetryWidget();
            if (widget == null)
            {
                Debug.LogError("MIRROR_ORIENT: HorizontalForward — SymmetryWidget null");
                return;
            }
            widget.SetOrientationHorizontalForward();
            RefreshMirrorControlsPanelTints();
        }

        public void SetMirrorVerticalForward()
        {
            SymmetryWidget widget = FindSymmetryWidget();
            if (widget == null)
            {
                Debug.LogError("MIRROR_ORIENT: VerticalForward — SymmetryWidget null");
                return;
            }
            widget.SetOrientationVerticalForward();
            RefreshMirrorControlsPanelTints();
        }

        public void SetMirrorVerticalSideways()
        {
            SymmetryWidget widget = FindSymmetryWidget();
            if (widget == null)
            {
                Debug.LogError("MIRROR_ORIENT: VerticalSideways — SymmetryWidget null");
                return;
            }
            widget.SetOrientationVerticalSideways();
            RefreshMirrorControlsPanelTints();
        }

        public void ToggleMirrorAxisLockX()
        {
            Debug.LogError("MIRROR_AXIS: panel ToggleX");
            SymmetryWidget widget = FindSymmetryWidget();
            if (widget == null)
            {
                Debug.LogError("MIRROR_AXIS: panel ToggleX — SymmetryWidget null");
                return;
            }
            widget.ToggleAxisLockX();
            Debug.LogError("MIRROR_AXIS: panel ToggleX done, widget=" + widget.CurrentAxisLock);
            RefreshMirrorControlsPanelTints();
        }

        public void ToggleMirrorAxisLockY()
        {
            Debug.LogError("MIRROR_AXIS: panel ToggleY");
            SymmetryWidget widget = FindSymmetryWidget();
            if (widget == null)
            {
                Debug.LogError("MIRROR_AXIS: panel ToggleY — SymmetryWidget null");
                return;
            }
            widget.ToggleAxisLockY();
            Debug.LogError("MIRROR_AXIS: panel ToggleY done, widget=" + widget.CurrentAxisLock);
            RefreshMirrorControlsPanelTints();
        }

        public void ToggleMirrorAxisLockZ()
        {
            Debug.LogError("MIRROR_AXIS: panel ToggleZ");
            SymmetryWidget widget = FindSymmetryWidget();
            if (widget == null)
            {
                Debug.LogError("MIRROR_AXIS: panel ToggleZ — SymmetryWidget null");
                return;
            }
            widget.ToggleAxisLockZ();
            Debug.LogError("MIRROR_AXIS: panel ToggleZ done, widget=" + widget.CurrentAxisLock);
            RefreshMirrorControlsPanelTints();
        }

        public void ToggleMirrorAxisLockAll()
        {
            Debug.LogError("MIRROR_AXIS: panel ToggleAll");
            SymmetryWidget widget = FindSymmetryWidget();
            if (widget == null)
            {
                Debug.LogError("MIRROR_AXIS: panel ToggleAll — SymmetryWidget null");
                return;
            }
            widget.ToggleAxisLockAll();
            Debug.LogError("MIRROR_AXIS: panel ToggleAll done, widget=" + widget.CurrentAxisLock);
            RefreshMirrorControlsPanelTints();
        }

        /// Hover text for scene X/Z only: Left-Right vs Forward-Back from the user's view.
        /// Buttons still lock scene X and Z; Y stays Up-Down.
        void RefreshAxisLockButtonDescriptions()
        {
            bool xIsSideways = GetSceneXIsSidewaysForUser();
            string descX = xIsSideways ? "Lateral" : "Forward-Back";
            string descZ = xIsSideways ? "Forward-Back" : "Lateral";

            ActionToggleButton[] toggles = GetComponentsInChildren<ActionToggleButton>(true);
            for (int i = 0; i < toggles.Length; ++i)
            {
                if (toggles[i] == null)
                {
                    continue;
                }
                string name = toggles[i].gameObject.name;
                if (name == "Left-Right-X")
                {
                    toggles[i].SetDescriptionText(descX);
                }
                else if (name == "Forward-Back-Z")
                {
                    toggles[i].SetDescriptionText(descZ);
                }
            }
        }




        /// True when scene X is the user's lateral axis (scene Z more aligned with head forward).
        /// False when scene X is more forward/back for the user (labels should swap).
        static bool GetSceneXIsSidewaysForUser()
        {
            if (ViewpointScript.Head == null || App.Scene == null)
            {
                return true;
            }

            Vector3 headFlat = ViewpointScript.Head.forward;
            headFlat.y = 0.0f;
            if (headFlat.sqrMagnitude < 1e-6f)
            {
                return true;
            }
            headFlat.Normalize();

            // Scene axes expressed in global space, then flattened.
            Quaternion sceneToGlobal = App.Scene.Pose.rotation;
            Vector3 sceneX_GS = sceneToGlobal * Vector3.right;
            Vector3 sceneZ_GS = sceneToGlobal * Vector3.forward;
            sceneX_GS.y = 0.0f;
            sceneZ_GS.y = 0.0f;

            if (sceneX_GS.sqrMagnitude < 1e-6f || sceneZ_GS.sqrMagnitude < 1e-6f)
            {
                return true;
            }
            sceneX_GS.Normalize();
            sceneZ_GS.Normalize();

            float alignX = Mathf.Abs(Vector3.Dot(headFlat, sceneX_GS));
            float alignZ = Mathf.Abs(Vector3.Dot(headFlat, sceneZ_GS));
            return alignZ >= alignX;
        }



        void RefreshOrientationButtonDescriptions()
        {
            float vf = SymmetryWidget.GetOrientationForwardScore(
                SymmetryWidget.PreferredOrientation.VerticalForward);
            float vs = SymmetryWidget.GetOrientationForwardScore(
                SymmetryWidget.PreferredOrientation.VerticalSideways);
            float hf = SymmetryWidget.GetOrientationForwardScore(
                SymmetryWidget.PreferredOrientation.HorizontalForward);
            float hs = SymmetryWidget.GetOrientationForwardScore(
                SymmetryWidget.PreferredOrientation.HorizontalSideways);

            // Pairwise: higher score = Forward label on that button; the other gets Sideways.
            bool vfIsForward = vf >= vs;
            bool hfIsForward = hf >= hs;

            ActionToggleButton[] toggles = GetComponentsInChildren<ActionToggleButton>(true);
            for (int i = 0; i < toggles.Length; ++i)
            {
                if (toggles[i] == null)
                {
                    continue;
                }
                string name = toggles[i].gameObject.name;
                if (name == "SetMirrorVerticalForward")
                {
                    toggles[i].SetDescriptionText(
                        vfIsForward ? "Mirror Vertical Forward" : "Mirror Vertical Sideways");
                }
                else if (name == "SetMirrorVerticalSideways")
                {
                    toggles[i].SetDescriptionText(
                        vfIsForward ? "Mirror Vertical Sideways" : "Mirror Vertical Forward");
                }
                else if (name == "SetMirrorHorizontalForward")
                {
                    toggles[i].SetDescriptionText(
                        hfIsForward ? "Mirror Horizontal Forward" : "Mirror Horizontal Sideways");
                }
                else if (name == "SetMirrorHorizontalSideways")
                {
                    toggles[i].SetDescriptionText(
                        hfIsForward ? "Mirror Horizontal Sideways" : "Mirror Horizontal Forward");
                }
            }
        }


        // Constrain Spin and enforce 90 degree snap
        public void ToggleConstrainOrientation()
        {
            SymmetryWidget widget = FindSymmetryWidget();
            if (widget == null)
            {
                Debug.LogError(
                    "[MirrorControlsPanel.ToggleConstrainOrientation] MIRROR_LOCK: widget null");
                return;
            }
            widget.ToggleLockOrientation();
            RefreshMirrorControlsPanelTints();
            RefreshMirrorSaveSlotTints();
        }

        public void ToggleLockSpin()
        {
            SymmetryWidget widget = FindSymmetryWidget();
            if (widget == null)
            {
                Debug.LogError(
                    "[MirrorControlsPanel.ToggleLockSpin] MIRROR_LOCK: widget null");
                return;
            }
            widget.ToggleLockSpin();
            RefreshMirrorControlsPanelTints();
            RefreshMirrorSaveSlotTints();
        }


        // ------- MIRROR SAVE SLOT SECTION BEGINS ----------
        public void MirrorSaveSlot01()
        {
            HandleMirrorSaveSlot(1);
        }
        public void MirrorSaveSlot02()
        {
            HandleMirrorSaveSlot(2);
        }
        public void MirrorSaveSlot03()
        {
            HandleMirrorSaveSlot(3);
        }
        public void MirrorSaveSlot04()
        {
            HandleMirrorSaveSlot(4);
        }
        public void MirrorSaveSlot05()
        {
            HandleMirrorSaveSlot(5);
        }
        public void MirrorSaveSlot06()
        {
            HandleMirrorSaveSlot(6);
        }
        public void MirrorSaveSlot07()
        {
            HandleMirrorSaveSlot(7);
        }
        public void MirrorSaveSlot08()
        {
            HandleMirrorSaveSlot(8);
        }
        public void MirrorSaveSlot09()
        {
            HandleMirrorSaveSlot(9);
        }
        public void MirrorSaveSlot10()
        {
            HandleMirrorSaveSlot(10);
        }
        public void MirrorSaveSlot11()
        {
            HandleMirrorSaveSlot(11);
        }
        public void MirrorSaveSlot12()
        {
            HandleMirrorSaveSlot(12);
        }
        public void MirrorSaveSlot13()
        {
            HandleMirrorSaveSlot(13);
        }
        public void MirrorSaveSlot14()
        {
            HandleMirrorSaveSlot(14);
        }
        public void MirrorSaveSlot15()
        {
            HandleMirrorSaveSlot(15);
        }


        // SAVE SLOT RELATED FUNCTIONS

        public void ToggleMirrorSaveSlotClearMode()
        {
            SymmetryWidget widget = FindSymmetryWidget();
            if (widget != null && widget.MoveToMirrorModeActive)
            {
                return;
            }

            m_MirrorSaveSlotClearMode = !m_MirrorSaveSlotClearMode;
            Debug.LogError(
                "MIRROR_SLOT: ClearMode -> " + m_MirrorSaveSlotClearMode);
            RefreshMirrorSaveSlotTints();
        }

        void HandleMirrorSaveSlot(int slotNumber1Based)
        {
            int index = slotNumber1Based - 1;
            SymmetryWidget widget = FindSymmetryWidget();
            if (widget == null)
            {
                Debug.LogError("MIRROR_SLOT: Handle — SymmetryWidget null");
                return;
            }
            if (index < 0 || index >= SymmetryWidget.kMirrorSaveSlotCount)
            {
                Debug.LogError("MIRROR_SLOT: Handle - bad index " + index);
                return;
            }

            if (widget.IsTrueCenterSlot(index) && !widget.MoveToMirrorModeActive)
            {
                if (m_MirrorSaveSlotClearMode)
                {
                    Debug.LogError("MIRROR_SLOT: Handle slot1 True Center - clear ignored");
                    RefreshMirrorSaveSlotTints();
                    return;
                }
                widget.EnsureTrueCenterSlot();
                widget.SetTeleportDest(index);
                RefreshMirrorSaveSlotTints();
                return;
            }

            // Hyperspace on: slots only assign To while picking; no save/recall/clear.
            if (widget.MoveToMirrorModeActive)
            {
                if (widget.MoveToMirrorPickingTo)
                {
                    widget.TryAssignMoveToMirrorTo(index);
                    RefreshMirrorSaveSlotTints();
                }
                return;
            }

            if (m_MirrorSaveSlotClearMode)
            {
                if (widget.HasMirrorSaveSlot(index))
                {
                    widget.ClearMirrorSaveSlot(index);
                }
                else
                {
                    widget.TryRestoreLastClearedMirrorSaveSlot(index);
                }
                RefreshMirrorSaveSlotTints();
                return;
            }

            if (widget.HasMirrorSaveSlot(index))
            {
                widget.SetTeleportDest(index);
            }
            else
            {
                widget.SaveMirrorSaveSlot(index);
            }
            RefreshMirrorSaveSlotTints();
        }


        public void MirrorTrueCenter()
        {
            HandleMirrorSaveSlot(1);
        }



        public void QuickRestore()
        {
            SymmetryWidget widget = FindSymmetryWidget();
            if (widget != null && widget.MoveToMirrorModeActive)
            {
                return;
            }

            if (m_MirrorSaveSlotClearMode)
            {
                Debug.LogError("MIRROR_SLOT: QuickRestore — ignored (clear mode)");
                return;
            }
            if (widget == null)
            {
                Debug.LogError("MIRROR_SLOT: QuickRestore — SymmetryWidget null");
                return;
            }
            widget.ApplyMirrorQuickReturn();
            RefreshMirrorSaveSlotTints();
        }


        void RefreshMirrorSaveSlotTints()
        {
            SymmetryWidget widget = FindSymmetryWidget();
            ActionToggleButton[] toggles = GetComponentsInChildren<ActionToggleButton>(true);
            for (int i = 0; i < toggles.Length; ++i)
            {
                if (toggles[i] == null)
                {
                    continue;
                }
                string name = toggles[i].gameObject.name;
                bool active = false;
                bool isSlotControl = true;
                bool angledOccupied = false;
                bool matchesCurrent = false;
                bool isMoveToTo = false;
                bool isTeleportDest = false;

                // --- Hyperspace phase buttons (gold / purple) ---
                if (name == "HyperspaceMode")
                {
                    active = widget != null && widget.MoveToMirrorModeActive;
                    ForceMirrorControlsPanelButtonTint(
                        toggles[i],
                        active ? MirrorControlsMoveToMirrorToTint : MirrorControlsInactiveTint);
                    continue;
                }
                if (name == "SetJumpTarget")
                {
                    active = widget != null && widget.MoveToMirrorPickingTo;
                    ForceMirrorControlsPanelButtonTint(
                        toggles[i],
                        active ? MirrorControlsMoveToMirrorToTint : MirrorControlsInactiveTint);
                    continue;
                }
                if (name == "SendSelection")
                {
                    active = widget != null && widget.IsMoveToMirrorArmed();
                    ForceMirrorControlsPanelButtonTint(
                        toggles[i],
                        active ? MirrorControlsMoveToMirrorToTint : MirrorControlsInactiveTint);
                    continue;
                }
                if (name == "CloneMirrorGroup")
                {
                    active = widget != null && widget.IsMoveToMirrorArmed();
                    ForceMirrorControlsPanelButtonTint(
                        toggles[i],
                        active ? MirrorControlsMoveToMirrorToTint : MirrorControlsInactiveTint);
                    continue;
                }
                if (name == "TeleportToActive")
                {
                    active = widget != null && widget.PeekTeleportShouldLight();
                    ForceMirrorControlsPanelButtonTint(
                        toggles[i],
                        active ? MirrorControlsMoveToMirrorToTint : MirrorControlsInactiveTint);
                    continue;
                }
                if (name == "MakeTargetSlotActive")
                {
                    active = widget != null
                        && widget.PeekTeleportDestValid()
                        && !widget.IsMirrorSaveSlotMatchingCurrent(widget.TeleportDestIndex);
                    ForceMirrorControlsPanelButtonTint(
                        toggles[i],
                        active ? MirrorControlsAngledSlotTint : MirrorControlsInactiveTint);
                    continue;
                }
                if (name == "SlotGuides")
                {
                    active = widget != null && widget.SlotGuidesToggledOn;
                    ForceMirrorControlsPanelButtonTint(
                        toggles[i],
                        active ? MirrorControlsActiveTint : MirrorControlsInactiveTint);
                    continue;
                }
                if (name == "PlaneLock")
                {
                    active = widget != null && widget.PlaneLockActive;
                    ForceMirrorControlsPanelButtonTint(
                        toggles[i],
                        active ? MirrorControlsMoveToMirrorToTint : MirrorControlsInactiveTint);
                    continue;
                }
                if (name == "TunnelLock")
                {
                    active = widget != null && widget.TunnelLockActive;
                    ForceMirrorControlsPanelButtonTint(
                        toggles[i],
                        active ? MirrorControlsMoveToMirrorToTint : MirrorControlsInactiveTint);
                    continue;
                }

                // --- Existing mirror controls ---
                if (name == "ClearSavedMirror")
                {
                    active = m_MirrorSaveSlotClearMode;
                }
                else if (name == "QuickRestore")
                {
                    active = widget != null && widget.HasMirrorQuickReturn;
                }
                else if (name == "ConstrainOrientation")
                {
                    active = widget != null && widget.LockOrientation;
                }
                else if (name == "LockSpin")
                {
                    active = widget != null && widget.LockSpin;
                }
                else if (name == "TrueCenter")
                {
                    bool atCenter = false;
                    if (widget != null)
                    {
                        TrTransform cur = App.Scene.AsScene[widget.transform];
                        TrTransform home = widget.GetMirrorTrueCenterPose_SS();
                        float eps = 0.01f * App.METERS_TO_UNITS;
                        atCenter =
                            (cur.translation - home.translation).sqrMagnitude <= eps * eps;
                    }
                    ForceMirrorControlsPanelButtonTint(
                        toggles[i],
                        atCenter
                            ? MirrorControlsInactiveTint
                            : Color.white);
                    continue;
                }
                else if (name.StartsWith("MirrorSaveSlot") && name.Length >= 16)
                {
                    int slotNumber;
                    if (int.TryParse(name.Substring(14), out slotNumber))
                    {
                        int index = slotNumber - 1;
                        active = widget != null && widget.HasMirrorSaveSlot(index);
                        if (active && widget != null)
                        {
                            matchesCurrent = widget.IsMirrorSaveSlotMatchingCurrent(index);
                            angledOccupied = !widget.IsMirrorSaveSlotAxisAligned(index);
                            isMoveToTo =
                                widget.MoveToMirrorModeActive
                                && widget.PeekMoveToMirrorToValid()
                                && widget.MoveToMirrorToIndex == index
                                && !matchesCurrent;
                            // Hyperspace To is the jump/send target. Dest gold
                            // on a different slot was a second "active" target.
                            isTeleportDest =
                                !widget.MoveToMirrorModeActive
                                && widget.PeekTeleportDestValid()
                                && widget.TeleportDestIndex == index
                                && !matchesCurrent;
                        }
                    }
                    else
                    {
                        isSlotControl = false;
                    }
                }
                else
                {
                    isSlotControl = false;
                }
                if (!isSlotControl)
                {
                    continue;
                }
                Color tint;
                if (!active)
                {
                    tint = MirrorControlsInactiveTint;
                }
                else if (isMoveToTo)
                {
                    tint = MirrorControlsMoveToMirrorToTint;
                }
                else if (isTeleportDest)
                {
                    tint = MirrorControlsMoveToMirrorToTint;
                }
                else if (matchesCurrent)
                {
                    tint = MirrorControlsCurrentSlotTint;
                }
                else if (angledOccupied)
                {
                    tint = MirrorControlsAngledSlotTint;
                }
                else
                {
                    tint = MirrorControlsActiveTint;
                }
                ForceMirrorControlsPanelButtonTint(toggles[i], tint);
            }
        }





        // ------- MIRROR SAVE SLOT SECTION COMPLETE ----------

        // METHODS FOR MIRROR TRANSLATION WITH OBJECTS AND BRUSH STROKES
        void OnDisable()
        {
            // Reset All Panels / hide only puts the pad away.
            // Hyperspace state stays on SymmetryWidget so From/To survive.
        }

        public void ToggleMoveToMirrorMode()
        {
            SymmetryWidget widget = FindSymmetryWidget();
            if (widget == null)
            {
                return;
            }
            if (widget.MoveToMirrorModeActive)
            {
                widget.ExitMoveToMirrorMode();
            }
            else
            {
                m_MirrorSaveSlotClearMode = false;
                widget.EnterMoveToMirrorMode();
            }
            RefreshMirrorSaveSlotTints();
        }

        public void BeginMoveToMirrorSetTo()
        {
            SymmetryWidget widget = FindSymmetryWidget();
            if (widget == null)
            {
                return;
            }
            if (!widget.MoveToMirrorModeActive)
            {
                return;
            }
            if (m_MirrorSaveSlotClearMode)
            {
                m_MirrorSaveSlotClearMode = false;
            }
            widget.BeginMoveToMirrorSetTo();
            RefreshMirrorSaveSlotTints();
        }

        public void ApplyMoveToMirror()
        {
            SymmetryWidget widget = FindSymmetryWidget();
            if (widget == null || !widget.IsMoveToMirrorArmed())
            {
                Debug.LogError(
                    "[MirrorControlsPanel.ApplyMoveToMirror] SEND skip unarmed");
                return;
            }
            SketchMemoryScript.m_Instance.PerformAndRecordCommand(
                new ApplyMirrorDestinationCommand());
            RefreshMirrorSaveSlotTints();
        }


        public void JumpToMoveToMirrorDestination()
        {
            SymmetryWidget widget = FindSymmetryWidget();
            if (widget == null || !widget.PeekTeleportDestValid())
            {
                Debug.LogError(
                    "[MirrorControlsPanel.JumpToMoveToMirrorDestination] skip no dest slot");
                return;
            }

            widget.ExitMoveToMirrorMode(true);
            NavigateToMirrorCommand cmd = new NavigateToMirrorCommand();
            if (!cmd.IsValid)
            {
                Debug.LogError(
                    "[MirrorControlsPanel.JumpToMoveToMirrorDestination] skip already there or invalid");
                RefreshMirrorSaveSlotTints();
                return;
            }
            SketchMemoryScript.m_Instance.PerformAndRecordCommand(cmd);
            RefreshMirrorSaveSlotTints();
        }

        //  SLOT GUIDES
        public void ToggleSlotGuides()
        {
            SymmetryWidget widget = FindSymmetryWidget();
            if (widget == null)
            {
                return;
            }
            widget.ToggleSlotGuides();
            RefreshMirrorSaveSlotTints();
        }

        public void TogglePlaneLock()
        {
            SymmetryWidget widget = FindSymmetryWidget();
            if (widget == null)
            {
                return;
            }
            widget.TogglePlaneLock();
            RefreshMirrorSaveSlotTints();
        }

        public void ToggleTunnelLock()
        {
            SymmetryWidget widget = FindSymmetryWidget();
            if (widget == null)
            {
                return;
            }
            widget.ToggleTunnelLock();
            RefreshMirrorSaveSlotTints();
        }

        public void SummonLiveMirror()
        {
            if (SketchControlsScript.m_Instance == null)
            {
                return;
            }

            SketchControlsScript.m_Instance.IssueGlobalCommand(
                SketchControlsScript.GlobalCommands.SummonMirror);
        }

        public void TeleportUserToActiveMirror()
        {
            SymmetryWidget widget = FindSymmetryWidget();
            if (widget == null || !widget.PeekTeleportDestValid())
            {
                Debug.LogError(
                    "[MirrorControlsPanel.TeleportUserToActiveMirror] skip no dest slot");
                return;
            }
            widget.ExitMoveToMirrorMode(true);
            TeleportToMirrorCommand cmd = new TeleportToMirrorCommand();
            if (!cmd.IsValid)
            {
                Debug.LogError(
                    "[MirrorControlsPanel.TeleportUserToActiveMirror] skip command invalid");
                RefreshMirrorSaveSlotTints();
                return;
            }
            SketchMemoryScript.m_Instance.PerformAndRecordCommand(cmd);
            RefreshMirrorSaveSlotTints();
        }

        public void CloneMoveToMirror()
        {
            SymmetryWidget widget = FindSymmetryWidget();
            if (widget == null || !widget.IsMoveToMirrorArmed())
            {
                Debug.LogError(
                    "[MirrorControlsPanel.CloneMoveToMirror] CLONE skip unarmed");
                return;
            }
            CloneMirrorGroupCommand cloneCmd = new CloneMirrorGroupCommand();
            if (!cloneCmd.IsValid)
            {
                Debug.LogError(
                    "[MirrorControlsPanel.CloneMoveToMirror] CLONE skip invalid");
                return;
            }
            SketchMemoryScript.m_Instance.PerformAndRecordCommand(cloneCmd);
            widget.ClearMoveToMirrorTo();
            RefreshMirrorSaveSlotTints();
        }


    } // Functions Complete

} //Namespace TiltBrush
