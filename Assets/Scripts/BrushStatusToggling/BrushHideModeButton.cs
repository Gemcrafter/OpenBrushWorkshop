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

// Toggle button for the Brush panel's hide-mode. While active, clicking a
// BrushTypeButton marks/unmarks that brush as hidden (see
// BrushTypeButton.OnButtonPressed) instead of selecting it as the active
// brush. Marked brushes stay visible (dimmed) until the next
// BrushCatalog.BeginReload(); see HiddenBrushSet.
namespace TiltBrush
{
    // Extends TiltBrush.Layers.ToggleButton explicitly (fully qualified) --
    // there are two unrelated classes named ToggleButton in this codebase
    // (TiltBrush.ToggleButton and TiltBrush.Layers.ToggleButton). This
    // matches the base class ToggleVisibilityLayerButton itself uses,
    // already proven working on PanelButton_Test.
    public class BrushHideModeButton : TiltBrush.Layers.ToggleButton
    {
        private BrushGrid m_ParentGrid;

        override protected void Awake()
        {
            base.Awake();
            m_ParentGrid = GetComponentInParent<BrushGrid>();
        }

        override protected void OnButtonPressed()
        {
            base.OnButtonPressed(); // flips 'activated', swaps texture via SetButtonActivation
            if (m_ParentGrid != null)
            {
                m_ParentGrid.ToggleHideMode();
            }
        }
    }
} // namespace TiltBrush