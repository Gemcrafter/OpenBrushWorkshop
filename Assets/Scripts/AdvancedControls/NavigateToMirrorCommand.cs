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
    /// Snap live glass to the Teleport dest slot. Does not move the user.
    /// Independent of Hyperspace To. Undo restores glass pose and scale.
    public class NavigateToMirrorCommand : BaseCommand
    {
        private bool m_Valid;
        private int m_DestIndex;
        private TrTransform m_GlassBefore_SS;
        private float m_KeepScale;

        public bool IsValid
        {
            get
            {
                return m_Valid;
            }
        }

        public NavigateToMirrorCommand(BaseCommand parent = null) : base(parent)
        {
            m_Valid = false;
            if (App.Scene == null
                || PointerManager.m_Instance == null
                || PointerManager.m_Instance.SymmetryWidget == null)
            {
                Debug.LogError("[NavigateToMirrorCommand] skip missing widget");
                return;
            }

            SymmetryWidget mirror = PointerManager.m_Instance.SymmetryWidget;
            if (!mirror.PeekTeleportDestValid())
            {
                Debug.LogError("[NavigateToMirrorCommand] skip no dest slot");
                return;
            }

            m_DestIndex = mirror.TeleportDestIndex;
            if (mirror.IsMirrorSaveSlotMatchingCurrent(m_DestIndex))
            {
                Debug.LogError(
                    "[NavigateToMirrorCommand] skip already there dest=" + m_DestIndex);
                return;
            }

            m_GlassBefore_SS = App.Scene.AsScene[mirror.transform];
            m_KeepScale = mirror.GetLiveAsSceneScale();
            m_Valid = true;
            Debug.LogError(
                "[NavigateToMirrorCommand] dest=" + m_DestIndex +
                " keepScale=" + m_KeepScale);
        }

        public override bool NeedsSave
        {
            get
            {
                return false;
            }
        }

        void ApplyDest()
        {
            SymmetryWidget mirror = PointerManager.m_Instance.SymmetryWidget;
            if (mirror == null)
            {
                return;
            }
            mirror.RecallMirrorSaveSlot(m_DestIndex);
            mirror.SetLiveAsSceneScale(m_KeepScale);
            mirror.Show(true);
            mirror.RefreshVisibleSlotGuides();
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
                "[NavigateToMirrorCommand.OnRedo] dest=" + m_DestIndex);
            ApplyDest();
        }

        protected override void OnUndo()
        {
            if (!m_Valid)
            {
                return;
            }
            Debug.LogError(
                "[NavigateToMirrorCommand.OnUndo] dest=" + m_DestIndex);
            RestoreGlass();
        }
    }

}
