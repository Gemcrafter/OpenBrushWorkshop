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
    /// Copies the current selection across the live SinglePlane mirror only.
    /// No identity clone, no ClipboardManager duplicate offset.
    public class MirrorCopySelectionCommand : BaseCommand
    {
        private List<Stroke> m_SourceStrokes;
        private List<Stroke> m_MirroredStrokes;
        private CanvasScript m_CurrentCanvas;

        public MirrorCopySelectionCommand(BaseCommand parent = null) : base(parent)
        {
            m_CurrentCanvas = App.ActiveCanvas;
            m_SourceStrokes = SelectionManager.m_Instance.SelectedStrokes.ToList();
            m_MirroredStrokes = new List<Stroke>();

            if (m_SourceStrokes.Count == 0)
            {
                return;
            }

            if (PointerManager.m_Instance == null ||
                PointerManager.m_Instance.SymmetryWidget == null)
            {
                Debug.LogError("MirrorCopySelectionCommand: SymmetryWidget is null");
                return;
            }

            // Global-space reflection only (same source as SinglePlane's non-identity entry).
            TrTransform xfReflectGS =
                PointerManager.m_Instance.SymmetryWidget.ReflectionPlane.ToTrTransform();

            // Match DuplicateSelectionCommand canvas-space conversion.
            TrTransform xfGSfromCS = App.Scene.SelectionCanvas.Pose;
            TrTransform xfCSfromGS = m_CurrentCanvas.Pose.inverse;
            TrTransform xfReflectCS = xfCSfromGS * xfReflectGS * xfGSfromCS;

            foreach (var stroke in m_SourceStrokes)
            {
                m_MirroredStrokes.Add(
                    SketchMemoryScript.m_Instance.DuplicateStroke(
                        stroke, m_CurrentCanvas, xfReflectCS, absoluteScale: true));
            }

            GroupManager.MoveStrokesToNewGroups(m_MirroredStrokes, null);
        }

        public override bool NeedsSave
        {
            get
            {
                return true;
            }
        }

        protected override void OnRedo()
        {
            foreach (var stroke in m_MirroredStrokes)
            {
                switch (stroke.m_Type)
                {
                    case Stroke.Type.BrushStroke:
                        {
                            BaseBrushScript brushScript = stroke.m_Object.GetComponent<BaseBrushScript>();
                            if (brushScript)
                            {
                                brushScript.HideBrush(false);
                            }
                        }
                        break;
                    case Stroke.Type.BatchedBrushStroke:
                        {
                            stroke.m_BatchSubset.m_ParentBatch.EnableSubset(stroke.m_BatchSubset);
                        }
                        break;
                    default:
                        Debug.LogError("Unexpected: redo NotCreated mirror-copy stroke");
                        break;
                }
                TiltMeterScript.m_Instance.AdjustMeter(stroke, up: true);
            }
        }

        protected override void OnUndo()
        {
            foreach (var stroke in m_MirroredStrokes)
            {
                switch (stroke.m_Type)
                {
                    case Stroke.Type.BrushStroke:
                        {
                            BaseBrushScript brushScript = stroke.m_Object.GetComponent<BaseBrushScript>();
                            if (brushScript)
                            {
                                brushScript.HideBrush(true);
                            }
                        }
                        break;
                    case Stroke.Type.BatchedBrushStroke:
                        {
                            stroke.m_BatchSubset.m_ParentBatch.DisableSubset(stroke.m_BatchSubset);
                        }
                        break;
                    default:
                        Debug.LogError("Unexpected: undo NotCreated mirror-copy stroke");
                        break;
                }
                TiltMeterScript.m_Instance.AdjustMeter(stroke, up: false);
            }
        }
    }
}