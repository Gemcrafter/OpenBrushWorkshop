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

using System;
using UnityEngine;

namespace TiltBrush
{
    /// Brush curation panel: hide-mode tagging, shelf slot activation, sort policies.
    /// Prefab registration (PanelType, PanelMapKey, PanelButton) is Unity-side work.
    public class BrushCurationPanel : BasePanel
    {
        [Header("HideModeToggle")]
        [SerializeField] string m_HideModeOnText = "Tag mode on";
        [SerializeField] string m_HideModeOffText = "Tag mode off";
        [SerializeField] Color m_HideModeOnTint = Color.white;
        [SerializeField] Color m_HideModeOffTint = new Color(0.45f, 0.45f, 0.45f, 1f);

        [Header("SortToggle")]
        [SerializeField] string m_SortOnText = "Sort on reload: ON";
        [SerializeField] string m_SortOffText = "Sort on reload: OFF";
        [SerializeField] Color m_SortOnTint = Color.white;
        [SerializeField] Color m_SortOffTint = new Color(0.45f, 0.45f, 0.45f, 1f);

        [Header("UntetheredMover (Front text = groups first on reload)")]
        [SerializeField]
        [Tooltip("Shown when slotted groups are first. Matches this field name.")]
        string m_UntetheredFrontText = "Restart puts groups in front";
        [SerializeField]
        [Tooltip("Shown when slotted groups are last (uncategorized earlier).")]
        string m_UntetheredBackText = "Restart puts groups in back";
        [SerializeField] string m_UntetheredSortOffText = "Unsorted (sort off)";
        [SerializeField] Color m_UntetheredActiveTint = Color.white;
        [SerializeField] Color m_UntetheredInactiveTint = new Color(0.45f, 0.45f, 0.45f, 1f);


        static bool AtlasReady()
        {
            return SketchControlsScript.m_Instance != null &&
                   SketchControlsScript.m_Instance.IconTextureAtlas != null;
        }

        static void SafeSetToggleState(ActionToggleButton button, bool on)
        {
            if (button == null)
            {
                return;
            }
            if (!AtlasReady())
            {
                return;
            }
            try
            {
                button.ToggleState = on;
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    "BrushCurationPanel: ToggleState skipped (atlas not ready): " + e.Message);
            }
        }

        public void ToggleHideMode()
        {
            BrushGrid grid = FindBrushGrid();
            if (grid == null)
            {
                Debug.LogWarning("BrushCurationPanel: no BrushGrid found to toggle hide mode.");
                return;
            }
            grid.ToggleHideMode();
            RefreshHideModeLabel();
            RefreshPaletteChrome();
        }

        static void ForceButtonTint(ActionToggleButton button, Color tint)
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

        void RefreshHideModeLabel()
        {
            ActionToggleButton toggle = null;
            ActionToggleButton[] toggles = GetComponentsInChildren<ActionToggleButton>(true);
            for (int i = 0; i < toggles.Length; ++i)
            {
                if (toggles[i] != null && toggles[i].gameObject.name == "HideModeToggle")
                {
                    toggle = toggles[i];
                    break;
                }
            }
            if (toggle == null)
            {
                return;
            }

            string activeId = HiddenBrushSet.ActiveEditSetId;
            if (IsHideModeActive)
            {
                toggle.SetDescriptionText(m_HideModeOnText);
                SafeSetToggleState(toggle, true);
                ForceButtonTint(toggle, m_HideModeOnTint);
                TryCopyActivePaletteIcon(toggle, activeId);
            }
            else
            {
                toggle.SetDescriptionText(m_HideModeOffText);
                SafeSetToggleState(toggle, false);
                ForceButtonTint(toggle, m_HideModeOffTint);
            }
        }

        /// Copies the active Palette_NN button icon onto HideModeToggle.
        /// Safe during panel spawn: atlas / renderer may not be ready yet.
        void TryCopyActivePaletteIcon(ActionToggleButton hideToggle, string activePaletteId)
        {
            if (hideToggle == null || string.IsNullOrEmpty(activePaletteId))
            {
                return;
            }
            if (!HiddenBrushSet.IsPaletteId(activePaletteId))
            {
                return;
            }
            if (SketchControlsScript.m_Instance == null ||
                SketchControlsScript.m_Instance.IconTextureAtlas == null)
            {
                return;
            }
            if (hideToggle.GetComponent<Renderer>() == null)
            {
                return;
            }

            ActionToggleButton[] toggles = GetComponentsInChildren<ActionToggleButton>(true);
            ActionToggleButton paletteButton = null;
            for (int i = 0; i < toggles.Length; ++i)
            {
                if (toggles[i] == null)
                {
                    continue;
                }
                if (string.Equals(
                        toggles[i].gameObject.name,
                        activePaletteId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    paletteButton = toggles[i];
                    break;
                }
            }
            if (paletteButton == null)
            {
                return;
            }

            Texture2D icon = BrushPaletteConfig.GetSlotIcon(activePaletteId);
            if (icon == null)
            {
                icon = paletteButton.GetCurrentButtonTexture();
            }
            if (icon == null)
            {
                return;
            }

            try
            {
                hideToggle.SetButtonTexture(icon);
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    "BrushCurationPanel: could not copy hide-mode icon: " + e.Message);
            }
        }

        override protected void OnEnablePanel()
        {
            base.OnEnablePanel();
            RefreshHideModeLabel();
            RefreshDisplayModeLabel();
            RefreshPanelSetLabels();
            RefreshPaletteChrome();
            RefreshSortLabel();
            RefreshUntetheredMoverLabel();
            RefreshConcealCustomLabel();
            ApplyConfigSlotIcons();
            StartCoroutine(DeferredPanelChromeRefresh());
        }

        System.Collections.IEnumerator DeferredPanelChromeRefresh()
        {
            yield return null;
            SyncDisplayModeToggleVisual();
            RefreshDisplayModeLabel();
            RefreshHideModeLabel();
            RefreshPaletteChrome();
            RefreshSortLabel();
            RefreshUntetheredMoverLabel();
            ApplyConfigSlotIcons();
        }

        public bool IsHideModeActive
        {
            get
            {
                BrushGrid grid = FindBrushGrid();
                return grid != null && grid.HideModeActive;
            }
        }

        private BrushGrid FindBrushGrid()
        {
            if (PanelManager.m_Instance == null)
            {
                return null;
            }
            BasePanel panel = PanelManager.m_Instance.GetActivePanelByType(BasePanel.PanelType.Brush);
            if (panel == null)
            {
                panel = PanelManager.m_Instance.GetActivePanelByType(BasePanel.PanelType.BrushMobile);
            }
            if (panel == null)
            {
                panel = PanelManager.m_Instance.GetPanelByType(BasePanel.PanelType.Brush);
            }
            if (panel == null)
            {
                panel = PanelManager.m_Instance.GetPanelByType(BasePanel.PanelType.BrushMobile);
            }
            if (panel == null)
            {
                return null;
            }
            return panel.GetComponentInChildren<BrushGrid>(includeInactive: true);
        }


        void ApplyConfigSlotIcons()
        {
            ActionToggleButton[] toggles = GetComponentsInChildren<ActionToggleButton>(true);
            for (int i = 0; i < toggles.Length; ++i)
            {
                if (toggles[i] == null)
                {
                    continue;
                }
                string objectName = toggles[i].gameObject.name;
                if (!HiddenBrushSet.IsPaletteId(objectName))
                {
                    continue;
                }
                Texture2D icon = BrushPaletteConfig.GetSlotIcon(objectName);
                if (icon == null)
                {
                    continue;
                }
                ApplyIconToAllThreeSlots(toggles[i], icon);
            }
        }

        static void ApplyIconToAllThreeSlots(ActionToggleButton button, Texture2D icon)
        {
            if (button == null || icon == null)
            {
                return;
            }
            try
            {
                button.SetButtonTexture(icon);
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    "BrushCurationPanel: SetButtonTexture skipped: " + e.Message);
            }
            SetPrivateTexture(button, "m_ButtonTexture", icon);
            SetPrivateTexture(button, "m_TextureOn", icon);
            SetPrivateTexture(button, "m_TextureOff", icon);
        }

        static void SetPrivateTexture(object target, string fieldName, Texture2D icon)
        {
            System.Reflection.FieldInfo field = target.GetType().GetField(
                fieldName,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);
            if (field == null || field.FieldType != typeof(Texture2D))
            {
                Type walk = target.GetType().BaseType;
                while (field == null && walk != null)
                {
                    field = walk.GetField(
                        fieldName,
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Public);
                    walk = walk.BaseType;
                }
            }
            if (field != null && field.FieldType == typeof(Texture2D))
            {
                field.SetValue(target, icon);
            }
        }

        public static void CloseAllOpenInstances()
        {
            BrushCurationPanel[] panels = FindObjectsOfType<BrushCurationPanel>();
            for (int i = 0; i < panels.Length; ++i)
            {
                if (panels[i] != null)
                {
                    panels[i].ClosePanel();
                }
            }

            AdvancedLaunchPanel.BrushCurationLaunchOpen = false;
        }

        public void ClosePanel()
        {
            m_Fixed = false;
            AdvancedLaunchPanel.BrushCurationLaunchOpen = false;
            PanelWidget widget = GetComponent<PanelWidget>();
            if (widget != null)
            {
                widget.Show(false);
                return;
            }
            gameObject.SetActive(false);
        }

        public void ToggleDisplayMode()
        {
            ActionToggleButton toggle = null;
            ActionToggleButton[] toggles = GetComponentsInChildren<ActionToggleButton>(true);
            for (int i = 0; i < toggles.Length; ++i)
            {
                if (toggles[i] != null && toggles[i].gameObject.name == "DisplayModeToggle")
                {
                    toggle = toggles[i];
                    break;
                }
            }
            if (toggle != null)
            {
                if (toggle.ToggleState)
                {
                    HiddenBrushSet.ActiveDisplayMode = HiddenBrushSet.DisplayMode.All;
                }
                else
                {
                    HiddenBrushSet.ActiveDisplayMode = HiddenBrushSet.DisplayMode.Working;
                }
            }
            else
            {
                if (HiddenBrushSet.ActiveDisplayMode == HiddenBrushSet.DisplayMode.Working)
                {
                    HiddenBrushSet.ActiveDisplayMode = HiddenBrushSet.DisplayMode.All;
                }
                else
                {
                    HiddenBrushSet.ActiveDisplayMode = HiddenBrushSet.DisplayMode.Working;
                }
            }
            SyncDisplayModeToggleVisual();
            RefreshDisplayModeLabel();
            Debug.Log(
                "BrushCurationPanel: display mode = " +
                HiddenBrushSet.ActiveDisplayMode +
                " (applies on next brush catalog reload)");
        }

        void SyncDisplayModeToggleVisual()
        {
            ActionToggleButton[] toggles = GetComponentsInChildren<ActionToggleButton>(true);
            for (int i = 0; i < toggles.Length; ++i)
            {
                if (toggles[i] == null || toggles[i].gameObject.name != "DisplayModeToggle")
                {
                    continue;
                }
                if (!toggles[i].gameObject.activeInHierarchy)
                {
                    return;
                }
                try
                {
                    toggles[i].ToggleState =
                        HiddenBrushSet.ActiveDisplayMode == HiddenBrushSet.DisplayMode.All;
                }
                catch (Exception e)
                {
                    Debug.LogWarning(
                        "BrushCurationPanel: could not sync display toggle visual: " + e.Message);
                }
                return;
            }
        }

        public void SetDisplayModeWorking()
        {
            HiddenBrushSet.ActiveDisplayMode = HiddenBrushSet.DisplayMode.Working;
            RefreshDisplayModeLabel();
        }

        public void SetDisplayModeAll()
        {
            HiddenBrushSet.ActiveDisplayMode = HiddenBrushSet.DisplayMode.All;
            RefreshDisplayModeLabel();
        }

        void RefreshDisplayModeLabel()
        {
            ActionToggleButton[] toggles = GetComponentsInChildren<ActionToggleButton>(true);
            for (int i = 0; i < toggles.Length; ++i)
            {
                if (toggles[i] == null || toggles[i].gameObject.name != "DisplayModeToggle")
                {
                    continue;
                }
                if (HiddenBrushSet.ActiveDisplayMode == HiddenBrushSet.DisplayMode.All)
                {
                    toggles[i].SetDescriptionText("Display: All Mixed (applies on restart)");
                }
                else
                {
                    toggles[i].SetDescriptionText(
                        "Display: Working Set First (applies on restart)");
                }
                return;
            }
        }

        public void SetDisplaySetAll()
        {
            HiddenBrushSet.SetDisplaySetToAll();
            RefreshPanelSetLabels();
            Debug.Log(
                "BrushCurationPanel: panel set = All (applies on next brush catalog reload)");
        }

        public void SetDisplaySetHidden()
        {
            HiddenBrushSet.SetDisplaySetToHidden();
            RefreshPanelSetLabels();
            Debug.Log(
                "BrushCurationPanel: panel set = hidden (applies on next brush catalog reload)");
        }

        void RefreshPanelSetLabels()
        {
            ActionToggleButton[] toggles = GetComponentsInChildren<ActionToggleButton>(true);
            for (int i = 0; i < toggles.Length; ++i)
            {
                if (toggles[i] == null)
                {
                    continue;
                }
                string objectName = toggles[i].gameObject.name;
                if (objectName == "PanelSetAllButton")
                {
                    if (HiddenBrushSet.IsDisplayingAllSets())
                    {
                        toggles[i].SetDescriptionText(
                            "Panel: Load Full Brush Catalog (applies on restart)");
                    }
                    else
                    {
                        toggles[i].SetDescriptionText(
                            "Panel: All brushes (applies on restart)");
                    }
                }
                else if (objectName == "PanelSetHiddenButton")
                {
                    bool hiddenActive =
                        !HiddenBrushSet.IsDisplayingAllSets() &&
                        string.Equals(
                            HiddenBrushSet.ActiveDisplaySetId,
                            HiddenBrushSet.HiddenSetId,
                            StringComparison.OrdinalIgnoreCase);
                    if (hiddenActive)
                    {
                        toggles[i].SetDescriptionText(
                            "Panel: Hidden only - ACTIVE (applies on restart)");
                    }
                    else
                    {
                        toggles[i].SetDescriptionText(
                            "Panel: Hidden only (applies on restart)");
                    }
                }
            }
        }

        public void SetActivePalette01()
        {
            ActivatePalette(1);
        }
        public void SetActivePalette02()
        {
            ActivatePalette(2);
        }
        public void SetActivePalette03()
        {
            ActivatePalette(3);
        }
        public void SetActivePalette04()
        {
            ActivatePalette(4);
        }
        public void SetActivePalette05()
        {
            ActivatePalette(5);
        }
        public void SetActivePalette06()
        {
            ActivatePalette(6);
        }
        public void SetActivePalette07()
        {
            ActivatePalette(7);
        }
        public void SetActivePalette08()
        {
            ActivatePalette(8);
        }
        public void SetActivePalette09()
        {
            ActivatePalette(9);
        }
        public void SetActivePalette10()
        {
            ActivatePalette(10);
        }
        public void SetActivePalette11()
        {
            ActivatePalette(11);
        }
        public void SetActivePalette12()
        {
            ActivatePalette(12);
        }
        public void SetActivePalette13()
        {
            ActivatePalette(13);
        }
        public void SetActivePalette14()
        {
            ActivatePalette(14);
        }
        public void SetActivePalette15()
        {
            ActivatePalette(15);
        }
        public void SetActivePalette16()
        {
            ActivatePalette(16);
        }
        public void SetActivePalette17()
        {
            ActivatePalette(17);
        }
        public void SetActivePalette18()
        {
            ActivatePalette(18);
        }
        public void SetActivePalette19()
        {
            ActivatePalette(19);
        }
        public void SetActivePalette20()
        {
            ActivatePalette(20);
        }
        public void SetActivePalette21()
        {
            ActivatePalette(21);
        }
        public void SetActivePalette22()
        {
            ActivatePalette(22);
        }
        public void SetActivePalette23()
        {
            ActivatePalette(23);
        }
        public void SetActivePalette24()
        {
            ActivatePalette(24);
        }
        public void SetActivePalette25()
        {
            ActivatePalette(25);
        }
        public void SetActivePalette26()
        {
            ActivatePalette(26);
        }
        public void SetActivePalette27()
        {
            ActivatePalette(27);
        }
        public void SetActivePalette28()
        {
            ActivatePalette(28);
        }
        public void SetActivePalette29()
        {
            ActivatePalette(29);
        }
        public void SetActivePalette30()
        {
            ActivatePalette(30);
        }
        public void SetActivePalette31()
        {
            ActivatePalette(31);
        }
        public void SetActivePalette32()
        {
            ActivatePalette(32);
        }
        public void SetActivePalette33()
        {
            ActivatePalette(33);
        }
        public void SetActivePalette34()
        {
            ActivatePalette(34);
        }
        public void SetActivePalette35()
        {
            ActivatePalette(35);
        }
        public void SetActivePalette36()
        {
            ActivatePalette(36);
        }
        public void SetActivePalette37()
        {
            ActivatePalette(37);
        }

        public void ActivatePalette(int index)
        {
            if (index < 1)
            {
                return;
            }
            string id = HiddenBrushSet.FormatPaletteId(index);
            if (!HiddenBrushSet.IsPaletteId(id))
            {
                return;
            }
            HiddenBrushSet.ActiveEditSetId = id;
            RefreshPaletteChrome();
            RefreshHideModeLabel();
            Debug.Log(string.Format(
                "BrushCurationPanel: active palette = {0} (writable={1})",
                HiddenBrushSet.ActiveEditSetId,
                HiddenBrushSet.IsWritablePalette(HiddenBrushSet.ActiveEditSetId)));
        }

        public void ToggleSortEnabled()
        {
            HiddenBrushSet.SortEnabled = !HiddenBrushSet.SortEnabled;
            RefreshSortLabel();
            RefreshUntetheredMoverLabel();
            Debug.Log(string.Format(
                "BrushCurationPanel: sortEnabled = {0} (applies on reload)",
                HiddenBrushSet.SortEnabled));
        }

        void RefreshSortLabel()
        {
            ActionToggleButton[] toggles = GetComponentsInChildren<ActionToggleButton>(true);
            for (int i = 0; i < toggles.Length; ++i)
            {
                if (toggles[i] == null || toggles[i].gameObject.name != "SortToggle")
                {
                    continue;
                }
                bool on = HiddenBrushSet.SortEnabled;
                SafeSetToggleState(toggles[i], on);
                if (on)
                {
                    toggles[i].SetDescriptionText(m_SortOnText);
                    ForceButtonTint(toggles[i], m_SortOnTint);
                }
                else
                {
                    toggles[i].SetDescriptionText(m_SortOffText);
                    ForceButtonTint(toggles[i], m_SortOffTint);
                }
                return;
            }
        }

        void RefreshPaletteChrome()
        {
            ActionToggleButton[] toggles = GetComponentsInChildren<ActionToggleButton>(true);
            string activeId = HiddenBrushSet.ActiveEditSetId;
            bool modeOn = IsHideModeActive;
            for (int i = 0; i < toggles.Length; ++i)
            {
                if (toggles[i] == null)
                {
                    continue;
                }
                string objectName = toggles[i].gameObject.name;
                if (!HiddenBrushSet.IsPaletteId(objectName))
                {
                    continue;
                }

                bool isActive = string.Equals(
                    objectName, activeId, StringComparison.OrdinalIgnoreCase);
                bool populated = false;
                if (HiddenBrushSet.IsPermanentSlot(objectName))
                {
                    populated = true;
                }
                else
                {
                    foreach (Guid g in HiddenBrushSet.GetSetGuids(objectName))
                    {
                        populated = true;
                        break;
                    }
                }

                string label = HiddenBrushSet.GetDisplayName(objectName);
                if (string.IsNullOrEmpty(label))
                {
                    label = objectName;
                }
                if (populated)
                {
                    label = label + " *";
                }
                if (!HiddenBrushSet.IsWritablePalette(objectName))
                {
                    label = label + " [stock]";
                }
                toggles[i].SetDescriptionText(label);

                Color tint;
                if (modeOn && isActive)
                {
                    tint = Color.white;
                }
                else if (modeOn)
                {
                    string hex = HiddenBrushSet.GetPaletteColor(objectName);
                    if (!HiddenBrushSet.TryParseHexColor(hex, out tint))
                    {
                        tint = Color.white;
                    }
                }
                else
                {
                    tint = new Color(0.45f, 0.45f, 0.45f, 1f);
                }
                ForceButtonTint(toggles[i], tint);
            }
        }

        public void ToggleUntetheredMover()
        {
            if (HiddenBrushSet.UntetheredPlacement == HiddenBrushSet.UntetheredPlacementMode.Front)
            {
                HiddenBrushSet.UntetheredPlacement = HiddenBrushSet.UntetheredPlacementMode.Back;
            }
            else
            {
                HiddenBrushSet.UntetheredPlacement = HiddenBrushSet.UntetheredPlacementMode.Front;
            }
            RefreshUntetheredMoverLabel();
            Debug.Log(string.Format(
                "BrushCurationPanel: UntetheredPlacement = {0} (applies on reload)",
                HiddenBrushSet.UntetheredPlacement));
        }

        void RefreshUntetheredMoverLabel()
        {
            ActionToggleButton[] toggles = GetComponentsInChildren<ActionToggleButton>(true);
            for (int i = 0; i < toggles.Length; ++i)
            {
                if (toggles[i] == null || toggles[i].gameObject.name != "UntetheredMover")
                {
                    continue;
                }

                bool sortOn = HiddenBrushSet.SortEnabled;
                // Enum Front originally meant "untethered block first" = groups last.
                // Inspector Front = groups first = enum Back.
                bool groupsInFront = HiddenBrushSet.UntetheredPlacement ==
                    HiddenBrushSet.UntetheredPlacementMode.Back;
                SafeSetToggleState(toggles[i], sortOn && groupsInFront);
                if (!sortOn)
                {
                    toggles[i].SetDescriptionText(m_UntetheredSortOffText);
                    ForceButtonTint(toggles[i], m_UntetheredInactiveTint);
                }
                else if (groupsInFront)
                {
                    toggles[i].SetDescriptionText(m_UntetheredFrontText);
                    ForceButtonTint(toggles[i], m_UntetheredActiveTint);
                }
                else
                {
                    toggles[i].SetDescriptionText(m_UntetheredBackText);
                    ForceButtonTint(toggles[i], m_UntetheredActiveTint);
                }
                return;
            }
        }

        public void ToggleConcealCustom()
        {
            HiddenBrushSet.ConcealCustom = !HiddenBrushSet.ConcealCustom;
            RefreshConcealCustomLabel();
            Debug.Log(string.Format(
                "BrushCurationPanel: concealCustom = {0} (applies on reload)",
                HiddenBrushSet.ConcealCustom));
        }

        public void SetConcealCustomOn()
        {
            HiddenBrushSet.ConcealCustom = true;
            RefreshConcealCustomLabel();
        }

        public void SetConcealCustomOff()
        {
            HiddenBrushSet.ConcealCustom = false;
            RefreshConcealCustomLabel();
        }

        void RefreshConcealCustomLabel()
        {
            ActionToggleButton[] toggles = GetComponentsInChildren<ActionToggleButton>(true);
            for (int i = 0; i < toggles.Length; ++i)
            {
                if (toggles[i] == null || toggles[i].gameObject.name != "ConcealCustomToggle")
                {
                    continue;
                }
                if (HiddenBrushSet.ConcealCustom)
                {
                    toggles[i].SetDescriptionText("Conceal Custom: On (reload)");
                }
                else
                {
                    toggles[i].SetDescriptionText("Conceal Custom: Off (reload)");
                }
                return;
            }
        }
    }
}
