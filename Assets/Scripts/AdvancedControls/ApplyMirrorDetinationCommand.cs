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

using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TiltBrush
{
    /// SendSelection: move selection From -> gold To.
    /// Undo: restore exact pre-send poses (not intermediate targets).
    /// Delta is rigid (translation + rotation only). Slot and scene scale
    /// are not applied to strokes or widgets.
    public class ApplyMirrorDestinationCommand : BaseCommand
    {
        private List<Stroke> m_Strokes;
        private List<GrabWidget> m_Widgets;
        private List<TrTransform> m_WidgetPosesBefore;
        private TrTransform m_Delta_CS;
        private bool m_Valid;
        private int m_FromIndex;
        private int m_ToIndex;

        static TrTransform RigidPose(TrTransform pose)
        {
            pose.scale = 1.0f;
            return pose;
        }

        public ApplyMirrorDestinationCommand(BaseCommand parent = null) : base(parent)
        {
            m_Strokes = new List<Stroke>();
            m_Widgets = new List<GrabWidget>();
            m_WidgetPosesBefore = new List<TrTransform>();
            m_Valid = false;

            if (SelectionManager.m_Instance == null
                || PointerManager.m_Instance == null
                || PointerManager.m_Instance.SymmetryWidget == null)
            {
                return;
            }

            SymmetryWidget mirror = PointerManager.m_Instance.SymmetryWidget;
            if (!mirror.IsMoveToMirrorArmed())
            {
                return;
            }

            int fromIndex;
            if (!mirror.TryResolveMoveToMirrorFrom(out fromIndex))
            {
                return;
            }

            int toIndex = mirror.MoveToMirrorToIndex;
            TrTransform from_SS;
            TrTransform to_SS;
            if (!mirror.TryGetMirrorSaveSlotPose(fromIndex, out from_SS)
                || !mirror.TryGetMirrorSaveSlotPose(toIndex, out to_SS))
            {
                return;
            }

            from_SS = RigidPose(from_SS);
            to_SS = RigidPose(to_SS);

            // One delta: current From slot -> current gold To only.
            // Scene / canvas poses keep their scale so a zoomed stage still maps SS -> CS.
            TrTransform delta_SS = RigidPose(to_SS * from_SS.inverse);
            TrTransform scene_GS = App.Scene.Pose;
            TrTransform delta_GS = scene_GS * delta_SS * scene_GS.inverse;
            TrTransform xfGSfromCS = App.Scene.SelectionCanvas.Pose;
            m_Delta_CS = xfGSfromCS.inverse * delta_GS * xfGSfromCS;
            m_Delta_CS = RigidPose(m_Delta_CS);

            m_Strokes = SelectionManager.m_Instance.SelectedStrokes.ToList();
            m_Widgets = SelectionManager.m_Instance.SelectedWidgets.ToList();
            for (int i = 0; i < m_Widgets.Count; ++i)
            {
                m_WidgetPosesBefore.Add(m_Widgets[i].LocalTransform);
            }

            if (fromIndex == toIndex)
            {
                Debug.LogError(
                    "[ApplyMirrorDestinationCommand] SEND ignored from==to " + fromIndex);
                return;
            }

            m_FromIndex = fromIndex;
            m_ToIndex = toIndex;
            m_Valid = m_Strokes.Count > 0 || m_Widgets.Count > 0;
            Debug.LogError(
                "[ApplyMirrorDestinationCommand] SEND from=" + fromIndex +
                " to=" + toIndex +
                " fromPos=" + from_SS.translation +
                " toPos=" + to_SS.translation +
                " deltaPos=" + m_Delta_CS.translation +
                " deltaScale=" + m_Delta_CS.scale +
                " strokes=" + m_Strokes.Count +
                " widgets=" + m_Widgets.Count +
                " valid=" + m_Valid);
            // Apply only in OnRedo (PerformAndRecordCommand runs Redo once).
        }

        public override bool NeedsSave
        {
            get
            {
                return m_Valid;
            }
        }

        void ApplyDelta(TrTransform delta_CS)
        {
            delta_CS = RigidPose(delta_CS);
            for (int i = 0; i < m_Strokes.Count; ++i)
            {
                Stroke stroke = m_Strokes[i];
                if (stroke == null)
                {
                    continue;
                }
                stroke.Recreate(delta_CS, absoluteScale: true);
            }

            for (int i = 0; i < m_Widgets.Count; ++i)
            {
                GrabWidget widget = m_Widgets[i];
                if (widget == null)
                {
                    continue;
                }
                widget.LocalTransform = delta_CS * widget.LocalTransform;
            }

            if (SelectionManager.m_Instance != null)
            {
                SelectionManager.m_Instance.UpdateSelectionWidget();
            }
        }

        protected override void OnRedo()
        {
            if (!m_Valid)
            {
                return;
            }
            ApplyDelta(m_Delta_CS);
            if (PointerManager.m_Instance != null
                && PointerManager.m_Instance.SymmetryWidget != null)
            {
                PointerManager.m_Instance.SymmetryWidget.SetMoveToMirrorTempFrom(m_ToIndex);
                PointerManager.m_Instance.SymmetryWidget.RememberLastSendSelection(
                    m_Strokes, m_Widgets);
            }
        }

        protected override void OnUndo()
        {
            if (!m_Valid)
            {
                return;
            }

            // Exact inverse of the single From -> To send.
            ApplyDelta(m_Delta_CS.inverse);

            for (int i = 0; i < m_Widgets.Count; ++i)
            {
                GrabWidget widget = m_Widgets[i];
                if (widget == null || i >= m_WidgetPosesBefore.Count)
                {
                    continue;
                }
                widget.LocalTransform = m_WidgetPosesBefore[i];
            }

            if (SelectionManager.m_Instance != null)
            {
                SelectionManager.m_Instance.UpdateSelectionWidget();
            }
            if (PointerManager.m_Instance != null
                && PointerManager.m_Instance.SymmetryWidget != null)
            {
                PointerManager.m_Instance.SymmetryWidget.SetMoveToMirrorTempFrom(m_FromIndex);
                PointerManager.m_Instance.SymmetryWidget.ClearLastSendSelection();
            }
        }

    } // Functions Complete


} // Namespace TiltBrush
