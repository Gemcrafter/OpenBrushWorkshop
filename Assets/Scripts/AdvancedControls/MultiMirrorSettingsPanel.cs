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
    /// Floating MultiMirror settings. PanelType MultiMirrorSettings = 20305.
    /// Nested MultimirrorSettings is a copy of PopupWindow_MultimirrorOptions.
    /// Its PopUpWindow / MirrorOptionsPopUpWindow scripts stay disabled (Init NREs).
    /// Its UIComponentManager must still be ticked or the 126 icons never see the ray.
    public class MultiMirrorSettingsPanel : BasePanel
    {
        [Tooltip("Extra rotation applied the first time the user opens this panel, degrees.")]
        [SerializeField] Vector3 m_FaceEulerOffset = new Vector3(0f, 0f, 0f);

        [SerializeField] GameObject m_MultiMirrorSettings;

        [Header("Mode tints")]
        [Tooltip("Selected family / wallpaper icon. Dark green so light icon art still reads.")]
        [SerializeField] Color m_MultiMirrorActiveTint = new Color(0f, 40f / 255f, 0f, 1f);
        [Tooltip("Chrome and selected tab when single-plane Mirror is on and this panel is leftover.")]
        [SerializeField] Color m_SinglePlaneTint = new Color(5f / 255f, 144f / 255f, 250f / 255f, 1f);
        [Tooltip("Chrome when symmetry is off.")]
        [SerializeField] Color m_ModeOffTint = new Color(115f / 255f, 115f / 255f, 115f / 255f, 1f);
        [Tooltip("Unselected Point / Wallpaper / Options tabs.")]
        [SerializeField] Color m_TabInactiveTint = new Color(90f / 255f, 90f / 255f, 90f / 255f, 1f);

        bool m_PlacedOnce;
        bool m_SettingsInited;
        bool m_LoggedCreate;
        int m_LastRayLogFrame;
        UIComponentManager m_SettingsUiManager;
        string m_CurrentPage = "Point Symmetry Controls";
        PointerManager.SymmetryMode m_LastTintMode = PointerManager.SymmetryMode.None;
        bool m_LoggedFlavorNames;

        override protected void OnEnablePanel()
        {
            base.OnEnablePanel();
            DisablePopupScriptsOnThisPanel();

            bool appReady = IsAppReady();
            Debug.LogError(
                "[MultiMirrorSettingsPanel.OnEnablePanel] LAUNCH: appReady=" + appReady
                + " frame=" + Time.frameCount);

            if (!appReady)
            {
                if (!m_LoggedCreate)
                {
                    Debug.LogError(
                        "[MultiMirrorSettingsPanel.OnEnablePanel] LAUNCH: skip mode/face during CreatePanel");
                    m_LoggedCreate = true;
                }
                return;
            }

            InitMultiMirrorSettings();
            ApplyFaceOffsetIfNeeded();
            EnsureMultiMirrorOn();
            PlacePadColliderBehindButtons();
            CacheSettingsUiManager();
            RefreshModeTint(forceLog: true);
        }

        void DisablePopupScriptsOnThisPanel()
        {
            Transform found = transform.Find("Mesh/MultimirrorSettings");
            if (found != null)
            {
                DisablePopupScripts(found.gameObject);
            }
        }

        override public void OnUpdatePanel(Vector3 vToPanel, Vector3 vHitPoint)
        {
            base.OnUpdatePanel(vToPanel, vHitPoint);

            // Parent BasePanel.UpdatePanel only ticks THIS panel's UIComponentManager.
            // Flavor icons live under the nested popup manager. Tick it here, while
            // m_ReticleSelectionRay and m_InputValid are already cached for this frame.
            if (m_SettingsUiManager != null)
            {
                m_SettingsUiManager.UpdateUIComponents(
                    m_ReticleSelectionRay, m_InputValid, GetCollider());
                // Same cadence as BasePanel.UpdateState on the panel manager.
                // Without this the nested description meshes stay at the prefab
                // open pose and every icon/tab/slider label draws at once.
                m_SettingsUiManager.UpdateVisuals();
            }

            RefreshModeTint(forceLog: false);

            if (Time.frameCount - m_LastRayLogFrame < 30)
            {
                return;
            }

            m_LastRayLogFrame = Time.frameCount;
            string hitName = RaycastChildButtonName();
            Debug.LogError(
                "[MultiMirrorSettingsPanel.OnUpdatePanel] LAUNCH: hit=" + vHitPoint
                + " child=" + hitName
                + " mode=" + CurrentModeName()
                + " settingsMgr=" + SettingsManagerName());
        }

        override public void ForceUpdatePanelVisuals()
        {
            base.ForceUpdatePanelVisuals();
            if (m_SettingsUiManager != null)
            {
                m_SettingsUiManager.UpdateVisuals();
            }
        }

        override protected void OnUpdateActive()
        {
            base.OnUpdateActive();
            if (m_SettingsUiManager == null)
            {
                return;
            }

            if (!IsActive())
            {
                m_SettingsUiManager.ManagerLostFocus();
                m_SettingsUiManager.Deactivate();
            }
        }

        void LateUpdate()
        {
            if (!m_SettingsInited)
            {
                return;
            }

            RefreshModeTint(forceLog: false);
        }

        override protected void OnUpdateGazeBehavior(Color rPanelColor)
        {
            base.OnUpdateGazeBehavior(rPanelColor);
        }

        void EnsureMultiMirrorOn()
        {
            if (PointerManager.m_Instance == null)
            {
                return;
            }

            if (PointerManager.m_Instance.CurrentSymmetryMode
                == PointerManager.SymmetryMode.MultiMirror)
            {
                return;
            }

            if (SketchControlsScript.m_Instance == null)
            {
                return;
            }

            SketchControlsScript.m_Instance.IssueGlobalCommand(
                SketchControlsScript.GlobalCommands.MultiMirror);
            Debug.LogError(
                "[MultiMirrorSettingsPanel.EnsureMultiMirrorOn] LAUNCH: issued MultiMirror");
        }

        static string CurrentModeName()
        {
            if (PointerManager.m_Instance == null)
            {
                return "null";
            }

            return PointerManager.m_Instance.CurrentSymmetryMode.ToString();
        }

        static PointerManager.SymmetryMode CurrentMode()
        {
            if (PointerManager.m_Instance == null)
            {
                return PointerManager.SymmetryMode.None;
            }

            return PointerManager.m_Instance.CurrentSymmetryMode;
        }

        Color ColorForMode(PointerManager.SymmetryMode mode)
        {
            if (mode == PointerManager.SymmetryMode.MultiMirror)
            {
                return m_MultiMirrorActiveTint;
            }

            if (mode == PointerManager.SymmetryMode.SinglePlane)
            {
                return m_SinglePlaneTint;
            }

            return m_ModeOffTint;
        }

        void RefreshModeTint(bool forceLog)
        {
            PointerManager.SymmetryMode mode = CurrentMode();
            if (!forceLog && mode == m_LastTintMode)
            {
                TintTabButtons(mode);
                TintFlavorButtons(mode);
                return;
            }

            m_LastTintMode = mode;
            Color tint = ColorForMode(mode);
            TintTabButtons(mode);
            TintFlavorButtons(mode);
            Debug.LogError(
                "[MultiMirrorSettingsPanel.RefreshModeTint] LAUNCH: mode=" + mode
                + " page=" + m_CurrentPage
                + " family=" + CurrentFamilyKey()
                + " group=" + CurrentWallpaperKey()
                + " tint=" + tint);
        }

        void TintTabButtons(PointerManager.SymmetryMode mode)
        {
            if (m_MultiMirrorSettings == null)
            {
                return;
            }

            bool multiOn = mode == PointerManager.SymmetryMode.MultiMirror;
            Renderer[] renderers = m_MultiMirrorSettings.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; ++i)
            {
                if (renderers[i] == null)
                {
                    continue;
                }

                string name = renderers[i].gameObject.name;
                int kind = TabKindFromName(name);
                if (kind == 0)
                {
                    continue;
                }

                bool selected = false;
                if (kind == 1)
                {
                    selected = m_CurrentPage == "Point Symmetry Controls";
                }
                else if (kind == 2)
                {
                    selected = m_CurrentPage == "Wallpaper Symmetry Controls";
                }
                else if (kind == 3)
                {
                    selected = m_CurrentPage == "Wallpaper Options Controls"
                        || m_CurrentPage == "Wallpaper Secret Options Controls";
                }

                Color tint = m_TabInactiveTint;
                if (selected && multiOn)
                {
                    tint = m_MultiMirrorActiveTint;
                }
                else if (selected && mode == PointerManager.SymmetryMode.SinglePlane)
                {
                    tint = m_SinglePlaneTint;
                }

                Material mat = renderers[i].material;
                if (mat == null)
                {
                    continue;
                }

                mat.SetColor("_Color", tint);
                if (mat.HasProperty("_SecondaryColor"))
                {
                    mat.SetColor("_SecondaryColor", tint);
                }
            }
        }

        void TintFlavorButtons(PointerManager.SymmetryMode mode)
        {
            if (m_MultiMirrorSettings == null || PointerManager.m_Instance == null)
            {
                return;
            }

            bool multiOn = mode == PointerManager.SymmetryMode.MultiMirror;
            bool point = PointerManager.m_Instance.m_CustomSymmetryType
                == PointerManager.CustomSymmetryType.Point
                || PointerManager.m_Instance.m_CustomSymmetryType
                == PointerManager.CustomSymmetryType.Polyhedra;
            string want = point ? CurrentFamilyKey() : CurrentWallpaperKey();

            if (!m_LoggedFlavorNames)
            {
                m_LoggedFlavorNames = true;
                UIComponent[] all = m_MultiMirrorSettings.GetComponentsInChildren<UIComponent>(true);
                System.Text.StringBuilder names = new System.Text.StringBuilder();
                for (int i = 0; i < all.Length; ++i)
                {
                    if (all[i] == null)
                    {
                        continue;
                    }

                    names.Append(all[i].gameObject.name);
                    names.Append(',');
                }

                Debug.LogError(
                    "[MultiMirrorSettingsPanel.TintFlavorButtons] LAUNCH: want=" + want
                    + " names=" + names);
            }

            UIComponent[] components = m_MultiMirrorSettings.GetComponentsInChildren<UIComponent>(true);
            for (int i = 0; i < components.Length; ++i)
            {
                if (components[i] == null)
                {
                    continue;
                }

                GameObject go = components[i].gameObject;
                if (!go.activeInHierarchy)
                {
                    continue;
                }

                string name = go.name;
                if (!LooksLikeFlavorIcon(name))
                {
                    continue;
                }

                bool selected = multiOn && FlavorNameIsSelected(name, want, point);
                HideFlavorLetters(go);
                TintRenderer(go, selected ? m_MultiMirrorActiveTint : m_TabInactiveTint);
            }
        }

        static string CurrentFamilyKey()
        {
            if (PointerManager.m_Instance == null)
            {
                return "";
            }

            string family = PointerManager.m_Instance.m_PointSymmetryFamily.ToString();
            if (family == "Cn") return "C";
            if (family == "Cnv") return "Cv";
            if (family == "Cnh") return "Ch";
            if (family == "Sn") return "S";
            if (family == "Dn") return "D";
            if (family == "Dnd") return "Dd";
            if (family == "Dnh") return "Dh";
            return family;
        }

        static string CurrentWallpaperKey()
        {
            if (PointerManager.m_Instance == null)
            {
                return "";
            }

            return PointerManager.m_Instance.m_WallpaperSymmetryGroup.ToString();
        }

        static bool LooksLikeFlavorIcon(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            string lower = name.ToLowerInvariant();
            return lower.StartsWith("button mirrortype ")
                || lower.StartsWith("button wallpaper ");
        }

        static bool FlavorNameIsSelected(string name, string want, bool point)
        {
            if (FlavorNameMatches(name, want))
            {
                return true;
            }

            if (!point || PointerManager.m_Instance == null)
            {
                return false;
            }

            return FlavorNameMatches(
                name, PointerManager.m_Instance.m_PointSymmetryFamily.ToString());
        }

        static bool FlavorNameMatches(string name, string want)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(want))
            {
                return false;
            }

            string lowerName = name.ToLowerInvariant();
            string lowerWant = want.ToLowerInvariant();
            return lowerName == lowerWant
                || lowerName == "button_" + lowerWant
                || lowerName == "button mirrortype " + lowerWant
                || lowerName == "button wallpaper " + lowerWant
                || lowerName.EndsWith(" " + lowerWant)
                || lowerName.EndsWith("_" + lowerWant);
        }

        static void HideFlavorLetters(GameObject go)
        {
            if (go == null)
            {
                return;
            }

            TextMesh[] texts = go.GetComponentsInChildren<TextMesh>(true);
            for (int i = 0; i < texts.Length; ++i)
            {
                if (texts[i] != null)
                {
                    texts[i].gameObject.SetActive(false);
                }
            }

            Transform[] xforms = go.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < xforms.Length; ++i)
            {
                if (xforms[i] == null || xforms[i] == go.transform)
                {
                    continue;
                }

                string childName = xforms[i].name.ToLowerInvariant();
                if (childName.IndexOf("desc") >= 0
                    || childName.IndexOf("letter") >= 0
                    || childName.IndexOf("label") >= 0
                    || childName.IndexOf("text") >= 0)
                {
                    xforms[i].gameObject.SetActive(false);
                }
            }
        }

        static void TintRenderer(GameObject go, Color tint)
        {
            if (go == null)
            {
                return;
            }

            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer == null || renderer.material == null)
            {
                return;
            }

            renderer.material.SetColor("_Color", tint);
            if (renderer.material.HasProperty("_SecondaryColor"))
            {
                renderer.material.SetColor("_SecondaryColor", tint);
            }

            if (renderer.material.HasProperty("_EmissionColor"))
            {
                renderer.material.SetColor("_EmissionColor", tint * 0.25f);
            }
        }

        static int TabKindFromName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return 0;
            }

            if (name.IndexOf("Controls") >= 0)
            {
                return 0;
            }

            if (name.IndexOf("Order") >= 0 || name.IndexOf("Slider") >= 0)
            {
                return 0;
            }

            bool hasPoint = name.IndexOf("Point") >= 0;
            bool hasWall = name.IndexOf("Wallpaper") >= 0;
            bool hasOpt = name.IndexOf("Option") >= 0;
            if (hasPoint && !hasWall)
            {
                return 1;
            }

            if (hasWall && !hasOpt)
            {
                return 2;
            }

            if (hasOpt)
            {
                return 3;
            }

            return 0;
        }

        static bool IsAppReady()
        {
            if (PointerManager.m_Instance == null)
            {
                return false;
            }
            if (SketchControlsScript.m_Instance == null)
            {
                return false;
            }
            if (Time.frameCount < 2)
            {
                return false;
            }
            return true;
        }

        void InitMultiMirrorSettings()
        {
            if (m_SettingsInited)
            {
                return;
            }

            if (m_MultiMirrorSettings == null)
            {
                Transform found = transform.Find("Mesh/MultimirrorSettings");
                if (found != null)
                {
                    m_MultiMirrorSettings = found.gameObject;
                }
            }

            if (m_MultiMirrorSettings == null)
            {
                Debug.LogError(
                    "[MultiMirrorSettingsPanel.InitMultiMirrorSettings] LAUNCH: MultimirrorSettings missing");
                m_SettingsInited = true;
                return;
            }

            DisablePopupScripts(m_MultiMirrorSettings);
            HidePopUpDismissChrome(m_MultiMirrorSettings);
            HideInnerPopupMesh(m_MultiMirrorSettings);
            ShowOnlyNamedPage(m_MultiMirrorSettings, "Point Symmetry Controls");
            EnsureChildButtonColliders(m_MultiMirrorSettings);
            CacheSettingsUiManager();
            RefreshUiComponentManager(m_SettingsUiManager);
            RefreshUiComponentManager(m_UIComponentManager);
            m_SettingsInited = true;
            Debug.LogError(
                "[MultiMirrorSettingsPanel.InitMultiMirrorSettings] LAUNCH: pages set, popup scripts off");
        }

        void CacheSettingsUiManager()
        {
            if (m_MultiMirrorSettings == null)
            {
                m_SettingsUiManager = null;
                return;
            }

            m_SettingsUiManager = m_MultiMirrorSettings.GetComponent<UIComponentManager>();
            if (m_SettingsUiManager == null)
            {
                m_SettingsUiManager = m_MultiMirrorSettings.GetComponentInChildren<UIComponentManager>(true);
            }

            if (m_SettingsUiManager == null)
            {
                Debug.LogError(
                    "[MultiMirrorSettingsPanel.CacheSettingsUiManager] LAUNCH: no nested UIComponentManager");
                return;
            }

            if (m_SettingsUiManager == m_UIComponentManager)
            {
                Debug.LogError(
                    "[MultiMirrorSettingsPanel.CacheSettingsUiManager] LAUNCH: nested manager is the panel manager");
                return;
            }

            if (!m_SettingsUiManager.enabled)
            {
                m_SettingsUiManager.enabled = true;
            }

            Debug.LogError(
                "[MultiMirrorSettingsPanel.CacheSettingsUiManager] LAUNCH: nested="
                + m_SettingsUiManager.gameObject.name
                + " panel=" + (m_UIComponentManager != null ? m_UIComponentManager.gameObject.name : "null"));
        }

        string SettingsManagerName()
        {
            if (m_SettingsUiManager == null)
            {
                return "none";
            }

            return m_SettingsUiManager.gameObject.name;
        }

        static void DisablePopupScripts(GameObject root)
        {
            MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; ++i)
            {
                if (behaviours[i] == null)
                {
                    continue;
                }

                string typeName = behaviours[i].GetType().Name;
                if (typeName == "PopUpWindow"
                    || typeName == "OptionsPopUpWindow"
                    || typeName == "MirrorOptionsPopUpWindow")
                {
                    behaviours[i].enabled = false;
                    Debug.LogError(
                        "[MultiMirrorSettingsPanel.DisablePopupScripts] LAUNCH: disabled "
                        + typeName);
                }
            }
        }

        static void HideInnerPopupMesh(GameObject root)
        {
            Transform mesh = root.transform.Find("Mesh");
            if (mesh != null)
            {
                mesh.gameObject.SetActive(false);
            }
        }

        static void ShowOnlyNamedPage(GameObject root, string pageName)
        {
            string[] pages = new string[]
            {
                "Point Symmetry Controls",
                "Wallpaper Symmetry Controls",
                "Wallpaper Options Controls",
                "Wallpaper Secret Options Controls",
            };

            for (int i = 0; i < pages.Length; ++i)
            {
                Transform page = FindChildByName(root.transform, pages[i]);
                if (page == null)
                {
                    continue;
                }

                bool on = pages[i] == pageName;
                page.gameObject.SetActive(on);
                Debug.LogError(
                    "[MultiMirrorSettingsPanel.ShowOnlyNamedPage] LAUNCH: "
                    + pages[i] + " on=" + on);
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
                    || name == "Button_Done"
                    || name == "PopUpButton_Ok")
                {
                    xforms[i].gameObject.SetActive(false);
                }
            }
        }

        void ApplyFaceOffsetIfNeeded()
        {
            if (m_PlacedOnce)
            {
                return;
            }

            if (m_FaceEulerOffset.sqrMagnitude < 1e-6f)
            {
                m_PlacedOnce = true;
                return;
            }

            transform.rotation = transform.rotation * Quaternion.Euler(m_FaceEulerOffset);
            m_PlacedOnce = true;
            Debug.LogError(
                "[MultiMirrorSettingsPanel.ApplyFaceOffsetIfNeeded] LAUNCH: face offset="
                + m_FaceEulerOffset);
        }

        public void ShowPointPage()
        {
            EnsureSettingsRoot();
            EnsureMultiMirrorOn();
            m_CurrentPage = "Point Symmetry Controls";
            ShowOnlyNamedPage(m_MultiMirrorSettings, m_CurrentPage);
            AfterPageChanged();
        }

        public void ShowWallpaperPage()
        {
            EnsureSettingsRoot();
            EnsureMultiMirrorOn();
            m_CurrentPage = "Wallpaper Symmetry Controls";
            ShowOnlyNamedPage(m_MultiMirrorSettings, m_CurrentPage);
            AfterPageChanged();
        }

        public void ShowOptionsPage()
        {
            EnsureSettingsRoot();
            EnsureMultiMirrorOn();
            m_CurrentPage = "Wallpaper Options Controls";
            ShowOnlyNamedPage(m_MultiMirrorSettings, m_CurrentPage);
            AfterPageChanged();
        }

        void AfterPageChanged()
        {
            PlacePadColliderBehindButtons();
            RefreshUiComponentManager(m_SettingsUiManager);
            RefreshModeTint(forceLog: true);
        }

        void EnsureSettingsRoot()
        {
            if (m_MultiMirrorSettings != null)
            {
                return;
            }

            Transform found = transform.Find("Mesh/MultimirrorSettings");
            if (found != null)
            {
                m_MultiMirrorSettings = found.gameObject;
            }
        }

        void EnsureChildButtonColliders(GameObject root)
        {
            UIComponent[] components = root.GetComponentsInChildren<UIComponent>(true);
            Debug.LogError(
                "[MultiMirrorSettingsPanel.EnsureChildButtonColliders] LAUNCH: uiCount="
                + components.Length);
            for (int i = 0; i < components.Length; ++i)
            {
                if (components[i] == null)
                {
                    continue;
                }

                GameObject go = components[i].gameObject;
                Collider col = go.GetComponent<Collider>();
                if (col == null)
                {
                    BoxCollider box = go.AddComponent<BoxCollider>();
                    box.size = new Vector3(1f, 1f, 0.1f);
                    box.center = new Vector3(0f, 0f, 0.01f);
                    Debug.LogError(
                        "[MultiMirrorSettingsPanel.EnsureChildButtonColliders] LAUNCH: added collider "
                        + go.name);
                }
                else if (!col.enabled)
                {
                    col.enabled = true;
                    Debug.LogError(
                        "[MultiMirrorSettingsPanel.EnsureChildButtonColliders] LAUNCH: enabled collider "
                        + go.name);
                }
            }
        }

        void PlacePadColliderBehindButtons()
        {
            BoxCollider pad = FindPadBox();
            if (pad == null)
            {
                Debug.LogError(
                    "[MultiMirrorSettingsPanel.PlacePadColliderBehindButtons] LAUNCH: pad missing");
                return;
            }

            float minLocalZ = 1e6f;
            float maxLocalZ = -1e6f;
            int buttonBoxes = 0;
            Collider[] cols = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; ++i)
            {
                if (cols[i] == null || !cols[i].enabled)
                {
                    continue;
                }

                if (!cols[i].gameObject.activeInHierarchy)
                {
                    continue;
                }

                string n = cols[i].gameObject.name;
                if (n == "Collider" || n == "MeshCollider")
                {
                    continue;
                }

                if (cols[i].GetComponent<UIComponent>() == null)
                {
                    continue;
                }

                Vector3 local = transform.InverseTransformPoint(cols[i].bounds.center);
                if (local.z < minLocalZ)
                {
                    minLocalZ = local.z;
                }

                if (local.z > maxLocalZ)
                {
                    maxLocalZ = local.z;
                }

                buttonBoxes += 1;
            }

            if (buttonBoxes == 0)
            {
                Debug.LogError(
                    "[MultiMirrorSettingsPanel.PlacePadColliderBehindButtons] LAUNCH: no button boxes");
                return;
            }

            // Pad sits behind the nearest button (smaller local Z toward the mesh).
            float padLocalZ = minLocalZ - 0.06f;
            Vector3 padWorld = transform.TransformPoint(new Vector3(0f, 0f, padLocalZ));
            Vector3 padLocal = pad.transform.InverseTransformPoint(padWorld);
            Vector3 c = pad.center;
            Vector3 s = pad.size;
            pad.center = new Vector3(c.x, c.y, padLocal.z);
            pad.size = new Vector3(s.x, s.y, 0.08f);
            Debug.LogError(
                "[MultiMirrorSettingsPanel.PlacePadColliderBehindButtons] LAUNCH: buttons="
                + buttonBoxes
                + " zMin=" + minLocalZ
                + " zMax=" + maxLocalZ
                + " padZ=" + padLocalZ);
        }

        BoxCollider FindPadBox()
        {
            Transform padXf = transform.Find("Collider");
            if (padXf != null)
            {
                BoxCollider named = padXf.GetComponent<BoxCollider>();
                if (named != null)
                {
                    return named;
                }
            }

            if (m_Collider != null)
            {
                return m_Collider as BoxCollider;
            }

            return null;
        }

        static void RefreshUiComponentManager(UIComponentManager manager)
        {
            if (manager == null)
            {
                return;
            }

            manager.enabled = false;
            manager.enabled = true;
            Debug.LogError(
                "[MultiMirrorSettingsPanel.RefreshUiComponentManager] LAUNCH: bounced "
                + manager.gameObject.name);
        }

        string RaycastChildButtonName()
        {
            RaycastHit[] hits = Physics.RaycastAll(m_ReticleSelectionRay, 8f);
            string best = "none";
            float bestDist = 1e6f;
            for (int i = 0; i < hits.Length; ++i)
            {
                Collider col = hits[i].collider;
                if (col == null)
                {
                    continue;
                }

                if (!col.transform.IsChildOf(transform) && col.transform != transform)
                {
                    continue;
                }

                if (hits[i].distance < bestDist)
                {
                    bestDist = hits[i].distance;
                    best = col.gameObject.name;
                }
            }

            return best;
        }

        public static void CloseAllOpenInstances()
        {
            MultiMirrorSettingsPanel[] panels =
                FindObjectsOfType<MultiMirrorSettingsPanel>();
            int closed = 0;
            for (int i = 0; i < panels.Length; ++i)
            {
                MultiMirrorSettingsPanel panel = panels[i];
                if (panel == null)
                {
                    continue;
                }

                PanelWidget widget = panel.GetComponent<PanelWidget>();
                bool showing = panel.gameObject.activeInHierarchy;
                if (widget != null)
                {
                    showing = showing || widget.Showing;
                }

                if (!showing)
                {
                    continue;
                }

                panel.ClosePanel();
                closed++;
            }

            if (closed > 0)
            {
                Debug.LogError(
                    "[MultiMirrorSettingsPanel.CloseAllOpenInstances] LAUNCH: closed="
                    + closed);
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
    }

}
