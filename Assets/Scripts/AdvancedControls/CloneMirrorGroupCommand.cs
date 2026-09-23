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

using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TiltBrush
{
    /// Copy the current selection to gold To using the same rigid From->To delta as Send.
    /// Originals stay selected at From. Copies sit unselected on ActiveCanvas.
    /// Does not change temp From or last-send. Does not use live-plane reflection.
    public class CloneMirrorGroupCommand : BaseCommand
    {
        private List<Stroke> m_CopiedStrokes;
        private List<GrabWidget> m_CopiedWidgets;
        private bool m_Valid;
        private CanvasScript m_DestCanvas;

        public bool IsValid
        {
            get
            {
                return m_Valid;
            }
        }

        static TrTransform RigidPose(TrTransform pose)
        {
            pose.scale = 1.0f;
            return pose;
        }

        public CloneMirrorGroupCommand(BaseCommand parent = null) : base(parent)
        {
            m_CopiedStrokes = new List<Stroke>();
            m_CopiedWidgets = new List<GrabWidget>();
            m_Valid = false;

            if (SelectionManager.m_Instance == null
                || PointerManager.m_Instance == null
                || PointerManager.m_Instance.SymmetryWidget == null
                || SketchMemoryScript.m_Instance == null)
            {
                Debug.LogError("[CloneMirrorGroupCommand] CLONE skip missing manager");
                return;
            }

            SymmetryWidget mirror = PointerManager.m_Instance.SymmetryWidget;
            if (!mirror.IsMoveToMirrorArmed())
            {
                Debug.LogError("[CloneMirrorGroupCommand] CLONE skip unarmed");
                return;
            }

            int fromIndex;
            if (!mirror.TryResolveMoveToMirrorFrom(out fromIndex))
            {
                Debug.LogError("[CloneMirrorGroupCommand] CLONE skip no From");
                return;
            }

            int toIndex = mirror.MoveToMirrorToIndex;
            if (fromIndex == toIndex)
            {
                Debug.LogError(
                    "[CloneMirrorGroupCommand] CLONE ignored from==to " + fromIndex);
                return;
            }

            TrTransform from_SS;
            TrTransform to_SS;
            if (!mirror.TryGetMirrorSaveSlotPose(fromIndex, out from_SS)
                || !mirror.TryGetMirrorSaveSlotPose(toIndex, out to_SS))
            {
                Debug.LogError(
                    "[CloneMirrorGroupCommand] CLONE skip pose from=" + fromIndex +
                    " to=" + toIndex);
                return;
            }

            from_SS = RigidPose(from_SS);
            to_SS = RigidPose(to_SS);

            TrTransform delta_SS = RigidPose(to_SS * from_SS.inverse);
            TrTransform scene_GS = App.Scene.Pose;
            TrTransform delta_GS = scene_GS * delta_SS * scene_GS.inverse;

            m_DestCanvas = App.ActiveCanvas;
            if (m_DestCanvas == null)
            {
                Debug.LogError("[CloneMirrorGroupCommand] CLONE skip no ActiveCanvas");
                return;
            }

            TrTransform xfSel = App.Scene.SelectionCanvas != null
                ? App.Scene.SelectionCanvas.Pose
                : m_DestCanvas.Pose;
            TrTransform xfStroke = m_DestCanvas.Pose.inverse * delta_GS * xfSel;
            xfStroke = RigidPose(xfStroke);
            TrTransform xfWidget = m_DestCanvas.Pose.inverse * delta_GS * m_DestCanvas.Pose;
            xfWidget = RigidPose(xfWidget);

            List<Stroke> sources = SelectionManager.m_Instance.SelectedStrokes.ToList();
            for (int i = 0; i < sources.Count; ++i)
            {
                Stroke src = sources[i];
                if (src == null)
                {
                    continue;
                }
                Stroke copy = SketchMemoryScript.m_Instance.DuplicateStroke(
                    src, m_DestCanvas, xfStroke, absoluteScale: true);
                if (copy != null)
                {
                    m_CopiedStrokes.Add(copy);
                }
            }

            if (m_CopiedStrokes.Count > 0)
            {
                GroupManager.MoveStrokesToNewGroups(m_CopiedStrokes, null);
            }

            List<GrabWidget> srcWidgets = SelectionManager.m_Instance.SelectedWidgets.ToList();
            for (int i = 0; i < srcWidgets.Count; ++i)
            {
                GrabWidget src = srcWidgets[i];
                if (src == null)
                {
                    continue;
                }
                GrabWidget copy = src.Clone();
                if (copy == null)
                {
                    continue;
                }
                copy.Group = SketchGroupTag.None;
                if (copy.Canvas != m_DestCanvas)
                {
                    copy.SetCanvas(m_DestCanvas);
                    copy.RestoreGameObjectLayer(m_DestCanvas.gameObject.layer);
                }
                copy.LocalTransform = xfWidget * copy.LocalTransform;
                m_CopiedWidgets.Add(copy);
            }

            m_Valid = m_CopiedStrokes.Count > 0 || m_CopiedWidgets.Count > 0;
            Debug.LogError(
                "[CloneMirrorGroupCommand] CLONE from=" + fromIndex +
                " to=" + toIndex +
                " fromPos=" + from_SS.translation +
                " toPos=" + to_SS.translation +
                " strokes=" + m_CopiedStrokes.Count +
                " widgets=" + m_CopiedWidgets.Count +
                " valid=" + m_Valid);
        }

        public override bool NeedsSave
        {
            get
            {
                return m_Valid;
            }
        }

        protected override void OnRedo()
        {
            if (!m_Valid)
            {
                return;
            }
            Debug.LogError(
                "[CloneMirrorGroupCommand.OnRedo] copies=" + m_CopiedStrokes.Count);
            for (int i = 0; i < m_CopiedStrokes.Count; ++i)
            {
                Stroke stroke = m_CopiedStrokes[i];
                if (stroke == null)
                {
                    continue;
                }
                switch (stroke.m_Type)
                {
                    case Stroke.Type.BrushStroke:
                        if (stroke.m_Object != null)
                        {
                            BaseBrushScript brushScript = stroke.m_Object.GetComponent<BaseBrushScript>();
                            if (brushScript)
                            {
                                brushScript.HideBrush(false);
                            }
                        }
                        break;
                    case Stroke.Type.BatchedBrushStroke:
                        if (stroke.m_BatchSubset != null && stroke.m_BatchSubset.m_ParentBatch != null)
                        {
                            stroke.m_BatchSubset.m_ParentBatch.EnableSubset(stroke.m_BatchSubset);
                        }
                        break;
                }
                if (TiltMeterScript.m_Instance != null)
                {
                    TiltMeterScript.m_Instance.AdjustMeter(stroke, up: true);
                }
            }
            for (int i = 0; i < m_CopiedWidgets.Count; ++i)
            {
                if (m_CopiedWidgets[i] != null)
                {
                    m_CopiedWidgets[i].RestoreFromToss();
                }
            }
        }

        protected override void OnUndo()
        {
            if (!m_Valid)
            {
                return;
            }
            Debug.LogError(
                "[CloneMirrorGroupCommand.OnUndo] hide copies=" + m_CopiedStrokes.Count);
            for (int i = 0; i < m_CopiedStrokes.Count; ++i)
            {
                Stroke stroke = m_CopiedStrokes[i];
                if (stroke == null)
                {
                    continue;
                }
                switch (stroke.m_Type)
                {
                    case Stroke.Type.BrushStroke:
                        if (stroke.m_Object != null)
                        {
                            BaseBrushScript brushScript = stroke.m_Object.GetComponent<BaseBrushScript>();
                            if (brushScript)
                            {
                                brushScript.HideBrush(true);
                            }
                        }
                        break;
                    case Stroke.Type.BatchedBrushStroke:
                        if (stroke.m_BatchSubset != null && stroke.m_BatchSubset.m_ParentBatch != null)
                        {
                            stroke.m_BatchSubset.m_ParentBatch.DisableSubset(stroke.m_BatchSubset);
                        }
                        break;
                }
                if (TiltMeterScript.m_Instance != null)
                {
                    TiltMeterScript.m_Instance.AdjustMeter(stroke, up: false);
                }
            }
            for (int i = 0; i < m_CopiedWidgets.Count; ++i)
            {
                if (m_CopiedWidgets[i] != null)
                {
                    m_CopiedWidgets[i].Hide();
                }
            }
        }
    }
}
