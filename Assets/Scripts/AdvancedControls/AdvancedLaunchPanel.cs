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

using System.Reflection;
using UnityEngine;

namespace TiltBrush
{
    /// Wand-fixed tear-away launcher for high-use panels and tools.
    /// Prefab registration: PanelType AdvancedLaunch = 20304.
    public class AdvancedLaunchPanel : BasePanel
    {
        public enum MultiMirrorSettingsPlacement
        {
            AttachedToLaunch = 0,
            InFrontOfUser = 1,
        }

        [SerializeField] GameObject m_MultiMirrorSettings;

        [Tooltip("AttachedToLaunch keeps the window on the pad. InFrontOfUser unparents it and places it in front of the headset. Throwable grab is not in this popup; that needs a panel prefab.")]
        [SerializeField]
        MultiMirrorSettingsPlacement m_MultiMirrorSettingsPlacement =
            MultiMirrorSettingsPlacement.InFrontOfUser;

        [Tooltip("Used when InFrontOfUser. X right, Y up, Z forward from the headset, in meters.")]
        [SerializeField] Vector3 m_MultiMirrorWorldOffset = new Vector3(0f, 0f, 1.1f);

        [Header("Launch spawn from headset (meters)")]
        [Tooltip("X right, Y up, Z forward. Used when Button_MirrorControls opens the pad.")]
        [SerializeField] Vector3 m_MirrorControlsSpawnOffset = new Vector3(0.08f, -0.08f, 0.48f);
        [Tooltip("X right, Y up, Z forward. Used when Button_BrushCuration opens the pad.")]
        [SerializeField] Vector3 m_BrushCurationSpawnOffset = new Vector3(-0.08f, -0.12f, 0.50f);
        [Tooltip("X right, Y up, Z forward. Used when Button_MultiMirror opens the pad.")]
        [SerializeField] Vector3 m_MultiMirrorSettingsSpawnOffset = new Vector3(0.32f, -0.06f, 0.48f);

        [Header("Launch mode tints")]
        [Tooltip("Button_Mirror when single-plane Mirror is live.")]
        [SerializeField] Color m_MirrorActiveTint = new Color(5f / 255f, 144f / 255f, 250f / 255f, 1f);
        [Tooltip("Button_MultiMirror when MultiMirror is live.")]
        [SerializeField] Color m_MultiMirrorActiveTint = new Color(0f, 102f / 255f, 0f, 1f);
        [Tooltip("Mirror / MultiMirror launch icons when that mode is off.")]
        [SerializeField] Color m_LaunchIdleTint = new Color(1f, 1f, 1f, 1f);
        [Tooltip("Button_MirrorControls when Mirror Controls panel is open.")]
        [SerializeField] Color m_MirrorControlsActiveTint = new Color(0f, 102f / 255f, 0f, 1f);
        [Tooltip("Button_BrushCuration when Brush Curation panel is open.")]
        [SerializeField] Color m_BrushCurationActiveTint = new Color(0f, 102f / 255f, 0f, 1f);
        [Tooltip("PanelButton / tool icon when that panel or tool is live.")]
        [SerializeField] Color m_LaunchPanelOpenTint = new Color(0f, 102f / 255f, 0f, 1f);

        bool m_MultiMirrorSettingsInited;
        public static bool MirrorControlsLaunchOpen;
        public static bool BrushCurationLaunchOpen;

        override protected void OnEnablePanel()
        {
            base.OnEnablePanel();
            m_MultiMirrorSettingsInited = false;
            MirrorControlsLaunchOpen = false;
            BrushCurationLaunchOpen = false;
        }

        override public void OnUpdatePanel(Vector3 vToPanel, Vector3 vHitPoint)
        {
            base.OnUpdatePanel(vToPanel, vHitPoint);
            RefreshLaunchSymmetryTints();
        }

        override protected bool GazeDismissesActivePopUp()
        {
            return false;
        }

        public void PlaceActiveMultiMirrorSettings()
        {
            if (m_ActivePopUp == null)
            {
                return;
            }

            if (m_MultiMirrorSettingsPlacement != MultiMirrorSettingsPlacement.InFrontOfUser)
            {
                return;
            }

            Transform popupXf = m_ActivePopUp.transform;
            popupXf.SetParent(null, true);

            Transform head = null;
            if (Camera.main != null)
            {
                head = Camera.main.transform;
            }

            if (head == null)
            {
                Debug.LogError(
                    "[AdvancedLaunchPanel.PlaceActiveMultiMirrorSettings] LAUNCH: no head camera");
                return;
            }

            float units = App.METERS_TO_UNITS;
            Vector3 worldPos =
                head.position
                + head.right * (m_MultiMirrorWorldOffset.x * units)
                + head.up * (m_MultiMirrorWorldOffset.y * units)
                + head.forward * (m_MultiMirrorWorldOffset.z * units);
            popupXf.position = worldPos;

            Vector3 toHead = head.position - popupXf.position;
            if (toHead.sqrMagnitude > 1e-6f)
            {
                popupXf.rotation = Quaternion.LookRotation(toHead.normalized, Vector3.up);
            }

            Debug.LogError(
                "[AdvancedLaunchPanel.PlaceActiveMultiMirrorSettings] LAUNCH: world place pos="
                + popupXf.position);
        }

        void LateUpdate()
        {
            TickMultiMirrorSettings();
            RefreshLaunchSymmetryTints();
        }

        void TickMultiMirrorSettings()
        {
            if (m_MultiMirrorSettings == null)
            {
                return;
            }

            if (!m_MultiMirrorSettings.activeInHierarchy)
            {
                m_MultiMirrorSettingsInited = false;
                return;
            }

            if (m_MultiMirrorSettingsInited)
            {
                return;
            }

            PopUpWindow popup = m_MultiMirrorSettings.GetComponent<PopUpWindow>();
            if (popup == null)
            {
                Debug.LogError(
                    "[AdvancedLaunchPanel.TickMultiMirrorSettings] LAUNCH: settings has no PopUpWindow");
                m_MultiMirrorSettingsInited = true;
                return;
            }

            popup.Init(gameObject, "");
            HidePopUpDismissChrome(m_MultiMirrorSettings);
            m_MultiMirrorSettingsInited = true;
            Debug.LogError(
                "[AdvancedLaunchPanel.TickMultiMirrorSettings] LAUNCH: MultiMirror settings Init");
        }

        static void HidePopUpDismissChrome(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            Transform[] xforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < xforms.Length; ++i)
            {
                if (xforms[i] == null)
                {
                    continue;
                }

                string name = xforms[i].name;
                if (name == "Button_Cancel"
                    || name == "Button_Confirm"
                    || name == "Cancel"
                    || name == "Confirm"
                    || name == "Button_Back"
                    || name == "Button_Done")
                {
                    xforms[i].gameObject.SetActive(false);
                }
            }
        }

        public void ClosePanel()
        {
            m_Fixed = false;
            PanelWidget widget = GetComponent<PanelWidget>();
            if (widget != null)
            {
                widget.Show(false);
                return;
            }
            gameObject.SetActive(false);
        }

        // Wire Button_MultiMirror Action here if it is not a toggling PanelButton.
        // Open: show settings and turn MultiMirror on.
        // Second press: close settings and turn MultiMirror off.
        public void ToggleMultiMirrorFromLaunch()
        {
            MultiMirrorSettingsPanel settings = FindOpenMultiMirrorSettings();
            if (settings != null)
            {
                settings.ClosePanel();
                if (PointerManager.m_Instance != null
                    && PointerManager.m_Instance.CurrentSymmetryMode
                    == PointerManager.SymmetryMode.MultiMirror
                    && SketchControlsScript.m_Instance != null)
                {
                    SketchControlsScript.m_Instance.IssueGlobalCommand(
                        SketchControlsScript.GlobalCommands.MultiMirror);
                }

                Debug.LogError(
                    "[AdvancedLaunchPanel.ToggleMultiMirrorFromLaunch] LAUNCH: closed settings");
                RefreshLaunchSymmetryTints();
                return;
            }

            if (SketchControlsScript.m_Instance == null)
            {
                return;
            }

            SketchControlsScript.m_Instance.OpenPanelOfType(
                BasePanel.PanelType.MultiMirrorSettings, TrTransform.identity);
            Debug.LogError(
                "[AdvancedLaunchPanel.ToggleMultiMirrorFromLaunch] LAUNCH: opened settings");
            RefreshLaunchSymmetryTints();
        }

        static MultiMirrorSettingsPanel FindOpenMultiMirrorSettings()
        {
            return FindShowingPanel(BasePanel.PanelType.MultiMirrorSettings)
                as MultiMirrorSettingsPanel;
        }

        public static TrTransform GetLaunchSpawnXf(BasePanel.PanelType type)
        {
            AdvancedLaunchPanel launch = Object.FindObjectOfType<AdvancedLaunchPanel>();
            Vector3 offsetMeters = new Vector3(0f, 0f, 1.0f);
            if (launch != null)
            {
                if (type == BasePanel.PanelType.MirrorControls)
                {
                    offsetMeters = launch.m_MirrorControlsSpawnOffset;
                }
                else if (type == BasePanel.PanelType.BrushCuration)
                {
                    offsetMeters = launch.m_BrushCurationSpawnOffset;
                }
                else if (type == BasePanel.PanelType.MultiMirrorSettings)
                {
                    offsetMeters = launch.m_MultiMirrorSettingsSpawnOffset;
                }
            }

            Transform head = Camera.main != null ? Camera.main.transform : null;
            if (head == null)
            {
                return TrTransform.identity;
            }

            float units = App.METERS_TO_UNITS;
            Vector3 worldPos =
                head.position
                + head.right * (offsetMeters.x * units)
                + head.up * (offsetMeters.y * units)
                + head.forward * (offsetMeters.z * units);
            Quaternion worldRot = Quaternion.LookRotation(
                (head.position - worldPos).sqrMagnitude > 1e-6f
                    ? (head.position - worldPos).normalized
                    : -head.forward,
                Vector3.up);
            return TrTransform.TR(worldPos, worldRot);
        }

        public static void LogReleasedLaunchPanel(BasePanel panel)
        {
            if (panel == null)
            {
                return;
            }

            BasePanel.PanelType type = panel.Type;
            if (type != BasePanel.PanelType.MirrorControls
                && type != BasePanel.PanelType.BrushCuration
                && type != BasePanel.PanelType.MultiMirrorSettings)
            {
                return;
            }

            Transform head = Camera.main != null ? Camera.main.transform : null;
            Vector3 headPos = head != null ? head.position : Vector3.zero;
            Vector3 headFwd = head != null ? head.forward : Vector3.forward;
            Vector3 panelPos = panel.transform.position;
            float units = App.METERS_TO_UNITS;
            Vector3 localMeters = Vector3.zero;
            if (head != null && units > 0.0001f)
            {
                Vector3 delta = panelPos - headPos;
                localMeters = new Vector3(
                    Vector3.Dot(delta, head.right) / units,
                    Vector3.Dot(delta, head.up) / units,
                    Vector3.Dot(delta, head.forward) / units);
            }

            Vector3 glassWorld = Vector3.zero;
            Vector3 glassScene = Vector3.zero;
            if (PointerManager.m_Instance != null
                && PointerManager.m_Instance.SymmetryWidget != null)
            {
                SymmetryWidget glass = PointerManager.m_Instance.SymmetryWidget;
                glassWorld = glass.transform.position;
                glassScene = App.Scene.AsScene[glass.transform].translation;
            }

            Debug.LogError(
                "[AdvancedLaunchPanel.Release] LAUNCH: type=" + type
                + " headW=" + headPos.ToString("F2")
                + " headFwd=" + headFwd.ToString("F2")
                + " panelW=" + panelPos.ToString("F2")
                + " glassW=" + glassWorld.ToString("F2")
                + " glassS=" + glassScene.ToString("F2")
                + " inspectorXYZ_m=" + localMeters.ToString("F2"));
        }

        public static bool IsLaunchPanelOpen(BasePanel.PanelType type)
        {
            bool showing = FindShowingPanel(type) != null;
            if (type == BasePanel.PanelType.MirrorControls)
            {
                if (!showing)
                {
                    MirrorControlsLaunchOpen = false;
                }

                return showing;
            }

            if (type == BasePanel.PanelType.BrushCuration)
            {
                if (!showing)
                {
                    BrushCurationLaunchOpen = false;
                }

                return showing;
            }

            return showing;
        }

        public static BasePanel FindShowingPanel(BasePanel.PanelType type)
        {
            BasePanel[] panels = FindObjectsOfType<BasePanel>();
            for (int i = 0; i < panels.Length; ++i)
            {
                BasePanel panel = panels[i];
                if (panel == null || panel.Type != type)
                {
                    continue;
                }

                // Init map clones are active/Used but not Showing. Those
                // must not count as open or the launch button closes then
                // opens again on one click.
                PanelWidget widget = panel.GetComponent<PanelWidget>();
                if (widget != null && widget.Showing)
                {
                    return panel;
                }
            }

            return null;
        }

        void RefreshLaunchSymmetryTints()
        {
            PointerManager.SymmetryMode mode = PointerManager.SymmetryMode.None;
            if (PointerManager.m_Instance != null)
            {
                mode = PointerManager.m_Instance.CurrentSymmetryMode;
            }

            bool settingsOpen = FindOpenMultiMirrorSettings() != null
                || mode == PointerManager.SymmetryMode.MultiMirror;
            bool mirrorControlsOpen =
                IsLaunchPanelOpen(BasePanel.PanelType.MirrorControls);
            bool curationOpen =
                IsLaunchPanelOpen(BasePanel.PanelType.BrushCuration);

            OptionButton[] options = GetComponentsInChildren<OptionButton>(true);
            for (int i = 0; i < options.Length; ++i)
            {
                if (options[i] == null)
                {
                    continue;
                }

                SketchControlsScript.GlobalCommands command = options[i].m_Command;
                if (command == SketchControlsScript.GlobalCommands.SummonMirror)
                {
                    continue;
                }

                bool on = false;
                Color tint = m_LaunchIdleTint;
                if (command == SketchControlsScript.GlobalCommands.SymmetryPlane)
                {
                    on = mode == PointerManager.SymmetryMode.SinglePlane;
                    tint = on ? m_MirrorActiveTint : m_LaunchIdleTint;
                }
                else if (command == SketchControlsScript.GlobalCommands.MultiMirror)
                {
                    on = mode == PointerManager.SymmetryMode.MultiMirror || settingsOpen;
                    tint = on ? m_MultiMirrorActiveTint : m_LaunchIdleTint;
                }
                else
                {
                    continue;
                }

                ApplyLaunchButtonState(options[i], on, tint);
            }

            MonoBehaviour[] behaviours = GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; ++i)
            {
                if (behaviours[i] == null)
                {
                    continue;
                }

                string typeName = behaviours[i].GetType().Name;
                if (typeName == "PanelButton")
                {
                    BasePanel.PanelType panelType;
                    if (!TryGetPanelButtonType(behaviours[i], out panelType))
                    {
                        continue;
                    }

                    bool on = IsLaunchPanelOpen(panelType);
                    if (panelType == BasePanel.PanelType.MultiMirrorSettings)
                    {
                        on = on || mode == PointerManager.SymmetryMode.MultiMirror;
                    }

                    Color tint = on ? m_LaunchPanelOpenTint : m_LaunchIdleTint;
                    if (panelType == BasePanel.PanelType.MultiMirrorSettings && on)
                    {
                        tint = m_MultiMirrorActiveTint;
                    }
                    else if (panelType == BasePanel.PanelType.MirrorControls && on)
                    {
                        tint = m_MirrorControlsActiveTint;
                    }
                    else if (panelType == BasePanel.PanelType.BrushCuration && on)
                    {
                        tint = m_BrushCurationActiveTint;
                    }

                    ApplyLaunchButtonState(behaviours[i], on, tint);
                }
                else if (typeName == "LongPressToolButton")
                {
                    BaseTool.ToolType tool;
                    if (!TryGetToolButtonType(behaviours[i], out tool))
                    {
                        continue;
                    }

                    bool on = SketchSurfacePanel.m_Instance != null
                        && SketchSurfacePanel.m_Instance.GetCurrentToolType() == tool;
                    ApplyLaunchButtonState(
                        behaviours[i], on, on ? m_LaunchPanelOpenTint : m_LaunchIdleTint);
                }
            }

            TintNamedLaunchButton("Button_Mirror",
                mode == PointerManager.SymmetryMode.SinglePlane,
                mode == PointerManager.SymmetryMode.SinglePlane
                    ? m_MirrorActiveTint
                    : m_LaunchIdleTint);
            TintNamedLaunchButton("Button_MultiMirror",
                mode == PointerManager.SymmetryMode.MultiMirror || settingsOpen,
                (mode == PointerManager.SymmetryMode.MultiMirror || settingsOpen)
                    ? m_MultiMirrorActiveTint
                    : m_LaunchIdleTint);
            TintNamedLaunchButton("Button_MirrorControls",
                mirrorControlsOpen,
                mirrorControlsOpen ? m_MirrorControlsActiveTint : m_LaunchIdleTint);
            TintNamedLaunchButton("Button_BrushCuration",
                curationOpen,
                curationOpen ? m_BrushCurationActiveTint : m_LaunchIdleTint);
        }

        void TintNamedLaunchButton(string name, bool on, Color tint)
        {
            Transform xf = FindChildByName(transform, name);
            if (xf != null)
            {
                ApplyLaunchButtonState(xf.GetComponent<MonoBehaviour>(), on, tint);
                TintLaunchRenderer(xf.gameObject, tint);
            }
        }

        static bool TryGetPanelButtonType(MonoBehaviour button, out BasePanel.PanelType panelType)
        {
            panelType = BasePanel.PanelType.Sketchbook;
            if (button == null)
            {
                return false;
            }

            FieldInfo[] fields = button.GetType().GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < fields.Length; ++i)
            {
                if (fields[i].FieldType != typeof(BasePanel.PanelType))
                {
                    continue;
                }

                object value = fields[i].GetValue(button);
                if (value == null)
                {
                    continue;
                }

                panelType = (BasePanel.PanelType)value;
                return true;
            }

            return false;
        }

        static bool TryGetToolButtonType(MonoBehaviour button, out BaseTool.ToolType tool)
        {
            tool = BaseTool.ToolType.FreePaintTool;
            if (button == null)
            {
                return false;
            }

            FieldInfo[] fields = button.GetType().GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < fields.Length; ++i)
            {
                if (fields[i].FieldType != typeof(BaseTool.ToolType))
                {
                    continue;
                }

                object value = fields[i].GetValue(button);
                if (value == null)
                {
                    continue;
                }

                tool = (BaseTool.ToolType)value;
                return true;
            }

            return false;
        }

        static void ApplyLaunchButtonState(MonoBehaviour button, bool on, Color tint)
        {
            if (button == null)
            {
                return;
            }

            // Do not call SetButtonActivated. RefreshAtlasedMaterial NREs at
            // CreatePanel and draws the black pip when the atlas is missing.
            TintLaunchRenderer(button.gameObject, tint);
        }

        static void TintLaunchRenderer(GameObject go, Color tint)
        {
            if (go == null)
            {
                return;
            }

            Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; ++i)
            {
                if (renderers[i] == null || renderers[i].material == null)
                {
                    continue;
                }

                if (renderers[i].GetComponent<TextMesh>() != null)
                {
                    continue;
                }

                string childName = renderers[i].gameObject.name.ToLowerInvariant();
                if (childName.IndexOf("desc") >= 0
                    || childName.IndexOf("label") >= 0
                    || childName.IndexOf("text") >= 0
                    || childName.IndexOf("letter") >= 0)
                {
                    continue;
                }

                Material mat = renderers[i].material;
                mat.SetColor("_Color", tint);
                if (mat.HasProperty("_SecondaryColor"))
                {
                    mat.SetColor("_SecondaryColor", tint);
                }

                if (mat.HasProperty("_EmissionColor"))
                {
                    mat.SetColor("_EmissionColor", tint * 0.35f);
                }
            }
        }

        static Transform FindChildByName(Transform root, string name)
        {
            if (root.name == name)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; ++i)
            {
                Transform found = FindChildByName(root.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
