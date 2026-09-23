// Copyright 2026 The Open Brush Authors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using UnityEngine;

namespace TiltBrush
{
    /// Move the user in front of the last chosen occupied slot and make that
    /// slot the live glass. Independent of Hyperspace To. Strokes do not move.
    public class TeleportToMirrorCommand : BaseCommand
    {
        private const float kStandBackMeters = 0.7f;
        private const float kTinyMoveMeters = 0.05f;

        private bool m_Valid;
        private TrTransform m_SceneBefore;
        private TrTransform m_SceneAfter;
        private TrTransform m_GlassBefore_SS;
        private float m_KeepScale;
        private int m_ToIndex;

        public bool IsValid
        {
            get
            {
                return m_Valid;
            }
        }

        public TeleportToMirrorCommand(BaseCommand parent = null) : base(parent)
        {
            m_Valid = false;
            if (App.Scene == null
                || PointerManager.m_Instance == null
                || PointerManager.m_Instance.SymmetryWidget == null)
            {
                Debug.LogError("[TeleportToMirrorCommand] skip missing scene or widget");
                return;
            }

            SymmetryWidget mirror = PointerManager.m_Instance.SymmetryWidget;
            if (!mirror.PeekTeleportDestValid())
            {
                Debug.LogError(
                    "[TeleportToMirrorCommand] skip no dest slot");
                return;
            }

            m_ToIndex = mirror.TeleportDestIndex;
            TrTransform to_SS;
            if (!mirror.TryGetMirrorSaveSlotPose(m_ToIndex, out to_SS))
            {
                Debug.LogError(
                    "[TeleportToMirrorCommand] skip no pose to=" + m_ToIndex);
                return;
            }
            to_SS.scale = 1.0f;

            m_SceneBefore = App.Scene.Pose;
            m_GlassBefore_SS = App.Scene.AsScene[mirror.transform];
            m_KeepScale = mirror.GetLiveAsSceneScale();

            Vector3 stand_GS = SlotStandWorld(to_SS);
            Vector3 head_GS = HeadPosition();
            Vector3 movement = head_GS - stand_GS;
            float moveMeters = movement.magnitude * App.UNITS_TO_METERS;
            bool glassAlreadyThere = mirror.IsMirrorSaveSlotMatchingCurrent(m_ToIndex);
            if (glassAlreadyThere && moveMeters < kTinyMoveMeters)
            {
                Debug.LogError(
                    "[TeleportToMirrorCommand] skip already there to=" + m_ToIndex);
                return;
            }

            TrTransform sceneAfter = m_SceneBefore;
            sceneAfter.translation += movement;
            float bounds = 100.0f;
            if (SceneSettings.m_Instance != null)
            {
                bounds = SceneSettings.m_Instance.HardBoundsRadiusMeters_SS;
            }
            m_SceneAfter = SketchControlsScript.MakeValidScenePose(sceneAfter, bounds);
            m_Valid = true;
            Debug.LogError(
                "[TeleportToMirrorCommand] to=" + m_ToIndex +
                " toPos=" + to_SS.translation +
                " keepScale=" + m_KeepScale +
                " sceneScale=" + m_SceneBefore.scale +
                " moveMeters=" + moveMeters +
                " glassAlready=" + glassAlreadyThere);
        }

        public override bool NeedsSave
        {
            get
            {
                return false;
            }
        }

        static Vector3 HeadPosition()
        {
            if (ViewpointScript.Head != null)
            {
                return ViewpointScript.Head.position;
            }
            if (Camera.main != null)
            {
                return Camera.main.transform.position;
            }
            return Vector3.zero;
        }

        static Vector3 SlotStandWorld(TrTransform to_SS)
        {
            Vector3 forward_SS = to_SS.rotation * Vector3.forward;
            Vector3 stand_SS = to_SS.translation - forward_SS * (kStandBackMeters * App.METERS_TO_UNITS);
            TrTransform standPose = TrTransform.TR(stand_SS, Quaternion.identity);
            return (App.Scene.Pose * standPose).translation;
        }

        void ApplyGlassToTo()
        {
            SymmetryWidget mirror = PointerManager.m_Instance.SymmetryWidget;
            if (mirror == null)
            {
                return;
            }
            float keep = m_KeepScale;
            mirror.RecallMirrorSaveSlot(m_ToIndex);
            mirror.SetLiveAsSceneScale(keep);
            mirror.Show(true);
        }

        void RestoreGlass()
        {
            SymmetryWidget mirror = PointerManager.m_Instance.SymmetryWidget;
            if (mirror == null)
            {
                return;
            }
            TrTransform xf = m_GlassBefore_SS;
            xf.scale = m_KeepScale;
            App.Scene.AsScene[mirror.transform] = xf;
            mirror.SetLiveAsSceneScale(m_KeepScale);
            mirror.Show(true);
            mirror.RefreshVisibleSlotGuides();
        }

        protected override void OnRedo()
        {
            if (!m_Valid)
            {
                return;
            }
            Debug.LogError(
                "[TeleportToMirrorCommand.OnRedo] to=" + m_ToIndex +
                " keepScale=" + m_KeepScale);
            ApplyGlassToTo();
            App.Scene.Pose = m_SceneAfter;
            if (SelectionManager.m_Instance != null
                && SelectionManager.m_Instance.HasSelection)
            {
                SelectionManager.m_Instance.ClearActiveSelection();
            }
            SymmetryWidget mirror = PointerManager.m_Instance.SymmetryWidget;
            if (mirror != null)
            {
                mirror.ClearLastSendSelection();
                mirror.ClearTeleportJumpPending();
                mirror.RefreshVisibleSlotGuides();
            }
        }

        protected override void OnUndo()
        {
            if (!m_Valid)
            {
                return;
            }
            Debug.LogError(
                "[TeleportToMirrorCommand.OnUndo] restore to=" + m_ToIndex +
                " keepScale=" + m_KeepScale);
            App.Scene.Pose = m_SceneBefore;
            RestoreGlass();
            SymmetryWidget mirror = PointerManager.m_Instance.SymmetryWidget;
            if (mirror != null)
            {
                mirror.SetTeleportDest(m_ToIndex);
            }
        }
    }
}
