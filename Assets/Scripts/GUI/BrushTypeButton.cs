/// Copyright 2020 The Tilt Brush Authors
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

using System;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

namespace TiltBrush
{

    public class BrushTypeButton : BaseButton
    {
        const string kBrushCellShaderName = "Custom/BrushCellButton";

        [SerializeField] private Texture2D m_PreviewBGTexture;
        [SerializeField] private GameObject m_AudioReactiveIcon;
        [SerializeField] private GameObject m_ExperimentalIcon;

        [NonSerialized] public BrushDescriptor m_Brush;
        [NonSerialized] public Vector3 m_OriginPosition;

        protected PreviewCubeScript m_PreviewCubeScript;
        private Renderer m_AudioReactiveIconRenderer;
        private Renderer m_ExperimentalIconRenderer;
        private Vector3 m_AudioReactiveIconBaseLocalPos;
        private Vector3 m_ExperimentalIconBaseLocalPos;
        private Texture2D m_BrushIconTexture;

        private BrushGrid m_ParentGrid;

        private GameObject m_SetBadgeRoot;
        private Renderer m_SetBadgePlateRenderer;
        private Renderer m_SetBadgeIconRenderer;
        private static BrushCurationPanel s_CachedCurationPanel;

        const string kBadgeRootName = "SetBadge";
        const string kBadgePlateName = "BadgePlate";
        const string kBadgeIconName = "BadgeIcon";
        const float kBadgeBasePlate = 0.275f;
        const float kBadgeBaseIcon = 0.225f;
        const float kBadgeZ = -0.05f;
        static readonly Vector2 kBadgeFixedCorner = new Vector2(
            0.38f + kBadgeBasePlate * 0.5f,
            0.38f + kBadgeBasePlate * 0.5f);

        [SerializeField]
        [Tooltip("Used only if BrushPaletteConfig has no SetBadgeScale. 1 = default, 1.5 = 150 percent.")]
        float m_SetBadgeScale = 1f;

        override protected void Awake()
        {
            base.Awake();
            m_AudioReactiveIconRenderer = m_AudioReactiveIcon.GetComponent<Renderer>();
            m_ExperimentalIconRenderer = m_ExperimentalIcon.GetComponent<Renderer>();
            m_OriginPosition = transform.localPosition;
            m_ParentGrid = GetComponentInParent<BrushGrid>();
            EnsureSetBadge();
        }

        protected override void OnSelectedLocaleChanged(Locale locale)
        {
            if (m_Brush != null)
            {
                if (Config.IsExperimental)
                {
                    SetDescriptionText(m_Brush.Description, m_Brush.m_DescriptionExtra);
                }
                else
                {
                    SetDescriptionText(m_Brush.Description);
                }
            }
        }

        override protected void OnDescriptionChanged()
        {
            m_PreviewCubeScript = m_Description.GetComponent<PreviewCubeScript>();
            base.OnDescriptionChanged();
        }

        override protected void OnRegisterComponent()
        {
            base.OnRegisterComponent();
            m_AudioReactiveIconBaseLocalPos = m_AudioReactiveIcon.transform.localPosition;
            m_ExperimentalIconBaseLocalPos = m_ExperimentalIcon.transform.localPosition;
        }

        override protected void ConfigureTextureAtlas()
        {
            if (!BrushPaletteConfig.GetBrushCellUseAtlas())
            {
                m_AtlasTexture = false;
            }

            if (m_AtlasTexture && SketchControlsScript.m_Instance.AtlasIconTextures)
            {
                RefreshAtlasedMaterial();
            }
            else
            {
                base.ConfigureTextureAtlas();
                ApplyBrushCellShader();
            }
        }

        void ApplyBrushCellShader()
        {
            if (m_AtlasTexture || m_ButtonRenderer == null)
            {
                return;
            }

            Shader shader = Shader.Find(kBrushCellShaderName);
            if (shader == null)
            {
                Debug.LogError("BrushTypeButton: shader not found " + kBrushCellShaderName);
                return;
            }

            Material mat = m_ButtonRenderer.material;
            if (mat.shader != shader)
            {
                mat.shader = shader;
            }
            ApplyBrushCellMaterialParams(mat);
        }

        void ApplyBrushCellMaterialParams(Material mat)
        {
            if (mat == null)
            {
                return;
            }
            if (mat.HasProperty("_PlateColor"))
            {
                mat.SetColor("_PlateColor", BrushPaletteConfig.GetBrushCellPlateColor());
            }
            if (mat.HasProperty("_StrokeGain"))
            {
                mat.SetFloat("_StrokeGain", BrushPaletteConfig.GetBrushCellStrokeGain());
            }
            if (mat.HasProperty("_PlateThreshold"))
            {
                mat.SetFloat("_PlateThreshold", BrushPaletteConfig.GetBrushCellPlateThreshold());
            }
        }

        protected override void SetMaterialColor(Color rColor)
        {
            if (m_AtlasTexture)
            {
                base.SetMaterialColor(rColor);
                return;
            }

            Color photo = BrushPaletteConfig.GetBrushCellPhotoColor();
            photo.a = rColor.a;
            if (m_ButtonRenderer != null && m_ButtonRenderer.material != null)
            {
                m_ButtonRenderer.material.SetColor("_Color", photo);
                ApplyBrushCellMaterialParams(m_ButtonRenderer.material);
            }
        }

        public void SetButtonProperties(BrushDescriptor rBrush)
        {
            m_Brush = rBrush;

            Texture2D buttonTexture = rBrush.m_ButtonTexture;
            if (buttonTexture == null)
            {
                Debug.LogWarningFormat(
                    rBrush,
                    "Button Texture not set for {0}, {1}", rBrush.DurableName, rBrush.m_Guid);
                buttonTexture = BrushCatalog.m_Instance.DefaultBrush.m_ButtonTexture;
            }
            m_BrushIconTexture = buttonTexture;
            m_PreviewCubeScript.SetSampleQuadTexture(buttonTexture);
            SetButtonTexture(buttonTexture);
            ApplyBrushCellShader();

            if (Config.IsExperimental)
            {
                SetDescriptionText(rBrush.Description, rBrush.m_DescriptionExtra);
            }
            else
            {
                SetDescriptionText(rBrush.Description);
            }
            m_AudioReactiveIcon.SetActive(rBrush.m_AudioReactive &&
                VisualizerManager.m_Instance.VisualsRequested);
            m_ButtonHasPressedAudio = (rBrush.m_ButtonAudio == null);
            m_ExperimentalIcon.SetActive(App.Instance.IsBrushExperimental(rBrush));

            SetButtonAvailable(true);
            RefreshSetBadge();
        }

        override protected void OnDescriptionActivated()
        {
            if (m_AtlasTexture)
            {
                SetButtonTexture(m_PreviewBGTexture);
            }
            else
            {
                m_ButtonRenderer.material.mainTexture = m_PreviewBGTexture;
            }
        }

        override protected void OnDescriptionDeactivated()
        {
            SetButtonSelected(m_ButtonSelected);
        }

        override protected void OnButtonPressed()
        {
            if (m_ParentGrid != null && m_ParentGrid.HideModeActive)
            {
                HiddenBrushSet.Toggle(m_Brush.m_Guid);
                SetButtonAvailable(true);
                SetColor(Color.white);
                RefreshSetBadge();
            }
            else
            {
                BrushController.m_Instance.SetActiveBrush(m_Brush);
            }
        }

        override public void ResetState()
        {
            base.ResetState();
            PlaceIconsOnPreviewCube(0.0f);
        }

        override public void SetButtonSelected(bool bSelected)
        {
            base.SetButtonSelected(bSelected);
            if (bSelected)
            {
                m_AudioReactiveIconRenderer.material.SetFloat("_Activated", 1.0f);
                m_ExperimentalIconRenderer.material.SetFloat("_Activated", 1.0f);
            }
            else
            {
                m_AudioReactiveIconRenderer.material.SetFloat("_Activated", 0.0f);
                m_ExperimentalIconRenderer.material.SetFloat("_Activated", 0.0f);
            }
            if (m_DescriptionState == DescriptionState.Deactivated && m_Brush != null)
            {
                if (m_AtlasTexture)
                {
                    SetButtonTexture(m_BrushIconTexture);
                }
                else
                {
                    m_ButtonRenderer.material.mainTexture = m_CurrentButtonTexture;
                    ApplyBrushCellShader();
                }
            }
        }

        override public void UpdateVisuals()
        {
            base.UpdateVisuals();
            ApplySetBadgeLayout(0.0f);
            if (m_PreviewCubeScript != null)
            {
                if (m_DescriptionState != DescriptionState.Deactivated)
                {
                    m_PreviewCubeScript.SetSelected(m_ButtonSelected);
                }

                PlaceIconsOnPreviewCube(m_DescriptionActivateTimer);
            }
        }

        void PlaceIconsOnPreviewCube(float offsetPercent)
        {
            float offset = offsetPercent * transform.localScale.z * 2.0f;
            offset += m_AudioReactiveIconBaseLocalPos.z * offsetPercent;

            Vector3 localPos = m_AudioReactiveIconBaseLocalPos;
            localPos.z -= offset;
            m_AudioReactiveIcon.transform.localPosition = localPos;

            localPos = m_ExperimentalIconBaseLocalPos;
            localPos.z -= offset;
            m_ExperimentalIcon.transform.localPosition = localPos;

            if (m_SetBadgeRoot != null)
            {
                ApplySetBadgeLayout(offset);
            }
        }

        override public void SetColor(Color rColor)
        {
            base.SetColor(rColor);
            Color audioTint = rColor;
            Color experimentalTint = rColor;
            if (m_ParentGrid != null)
            {
                audioTint = m_ParentGrid.AudioReactiveIconTint;
                experimentalTint = m_ParentGrid.ExperimentalIconTint;
            }
            ApplyMarkerTint(m_AudioReactiveIconRenderer, audioTint);
            ApplyMarkerTint(m_ExperimentalIconRenderer, experimentalTint);
            RefreshSetBadge();
        }

        static void ApplyMarkerTint(Renderer renderer, Color tint)
        {
            if (renderer == null || renderer.material == null)
            {
                return;
            }
            renderer.material.SetColor("_Color", tint);
            if (renderer.material.HasProperty("_Tint"))
            {
                renderer.material.SetColor("_Tint", tint);
            }
            if (renderer.material.HasProperty("_SecondaryColor"))
            {
                renderer.material.SetColor("_SecondaryColor", tint);
            }
        }

        void EnsureSetBadge()
        {
            Transform existing = transform.Find(kBadgeRootName);
            if (existing != null)
            {
                m_SetBadgeRoot = existing.gameObject;
                Transform plateXf = existing.Find(kBadgePlateName);
                Transform iconXf = existing.Find(kBadgeIconName);
                if (plateXf != null)
                {
                    m_SetBadgePlateRenderer = plateXf.GetComponent<Renderer>();
                }
                if (iconXf != null)
                {
                    m_SetBadgeIconRenderer = iconXf.GetComponent<Renderer>();
                }
                if (m_SetBadgePlateRenderer != null && m_SetBadgeIconRenderer != null)
                {
                    ApplySetBadgeLayout(0.0f);
                    return;
                }
            }

            Mesh quad = GetSharedQuadMesh();
            Material iconMat = null;
            if (m_ExperimentalIconRenderer != null && m_ExperimentalIconRenderer.sharedMaterial != null)
            {
                iconMat = new Material(m_ExperimentalIconRenderer.sharedMaterial);
            }

            m_SetBadgeRoot = new GameObject(kBadgeRootName);
            m_SetBadgeRoot.transform.SetParent(transform, false);
            m_SetBadgeRoot.transform.localRotation = Quaternion.identity;
            m_SetBadgeRoot.transform.localScale = Vector3.one;

            GameObject plate = new GameObject(kBadgePlateName);
            plate.transform.SetParent(m_SetBadgeRoot.transform, false);
            plate.transform.localPosition = Vector3.zero;
            plate.transform.localRotation = Quaternion.identity;
            MeshFilter plateFilter = plate.AddComponent<MeshFilter>();
            plateFilter.sharedMesh = quad;
            m_SetBadgePlateRenderer = plate.AddComponent<MeshRenderer>();
            m_SetBadgePlateRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_SetBadgePlateRenderer.receiveShadows = false;
            if (iconMat != null)
            {
                m_SetBadgePlateRenderer.material = new Material(iconMat);
            }
            m_SetBadgePlateRenderer.material.color = Color.black;
            if (m_SetBadgePlateRenderer.material.HasProperty("_Color"))
            {
                m_SetBadgePlateRenderer.material.SetColor("_Color", Color.black);
            }
            if (m_SetBadgePlateRenderer.material.HasProperty("_MainTex"))
            {
                m_SetBadgePlateRenderer.material.SetTexture("_MainTex", Texture2D.whiteTexture);
            }

            GameObject iconGo = new GameObject(kBadgeIconName);
            iconGo.transform.SetParent(m_SetBadgeRoot.transform, false);
            iconGo.transform.localPosition = new Vector3(0f, 0f, -0.01f);
            iconGo.transform.localRotation = Quaternion.identity;
            MeshFilter iconFilter = iconGo.AddComponent<MeshFilter>();
            iconFilter.sharedMesh = quad;
            m_SetBadgeIconRenderer = iconGo.AddComponent<MeshRenderer>();
            m_SetBadgeIconRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_SetBadgeIconRenderer.receiveShadows = false;
            if (iconMat != null)
            {
                m_SetBadgeIconRenderer.material = iconMat;
            }

            ApplySetBadgeLayout(0.0f);
            m_SetBadgeRoot.SetActive(false);
        }

        Mesh GetSharedQuadMesh()
        {
            if (m_ExperimentalIcon != null)
            {
                MeshFilter mf = m_ExperimentalIcon.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    return mf.sharedMesh;
                }
            }
            GameObject tmp = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Mesh mesh = tmp.GetComponent<MeshFilter>().sharedMesh;
            UnityEngine.Object.Destroy(tmp);
            return mesh;
        }

        float GetSetBadgeScale()
        {
            BrushPaletteConfig config = BrushPaletteConfig.LoadAsset();
            if (config != null && config.setBadgeScale > 0.01f)
            {
                return config.setBadgeScale;
            }
            return Mathf.Max(0.01f, m_SetBadgeScale);
        }

        void ApplySetBadgeLayout(float previewZOffset)
        {
            if (m_SetBadgeRoot == null)
            {
                return;
            }

            float scale = GetSetBadgeScale();
            float plate = kBadgeBasePlate * scale;
            float icon = kBadgeBaseIcon * scale;

            Vector3 pos = new Vector3(
                kBadgeFixedCorner.x - plate * 0.5f,
                kBadgeFixedCorner.y - plate * 0.5f,
                kBadgeZ - previewZOffset);
            m_SetBadgeRoot.transform.localPosition = pos;

            if (m_SetBadgePlateRenderer != null)
            {
                m_SetBadgePlateRenderer.transform.localScale = new Vector3(plate, plate, plate);
            }
            if (m_SetBadgeIconRenderer != null)
            {
                m_SetBadgeIconRenderer.transform.localScale = new Vector3(icon, icon, icon);
            }
        }

        void RefreshSetBadge()
        {
            if (m_SetBadgeRoot == null)
            {
                EnsureSetBadge();
            }
            if (m_SetBadgeRoot == null)
            {
                return;
            }

            string paletteId = null;
            if (m_Brush != null)
            {
                paletteId = HiddenBrushSet.GetPaletteIdForBrush(m_Brush.m_Guid);
            }

            ApplySetBadgeLayout(0.0f);

            bool show = !string.IsNullOrEmpty(paletteId) &&
                        HiddenBrushSet.IsWritablePalette(paletteId);
            m_SetBadgeRoot.SetActive(show);
            if (!show)
            {
                return;
            }

            if (m_SetBadgePlateRenderer != null && m_SetBadgePlateRenderer.material != null)
            {
                m_SetBadgePlateRenderer.material.SetColor("_Color", Color.black);
                if (m_SetBadgePlateRenderer.material.HasProperty("_Activated"))
                {
                    m_SetBadgePlateRenderer.material.SetFloat("_Activated", 1.0f);
                }
            }

            if (m_SetBadgeIconRenderer != null && m_SetBadgeIconRenderer.material != null)
            {
                Texture2D icon = FindPaletteButtonTexture(paletteId);
                if (icon != null)
                {
                    m_SetBadgeIconRenderer.material.mainTexture = icon;
                    if (m_SetBadgeIconRenderer.material.HasProperty("_MainTex"))
                    {
                        m_SetBadgeIconRenderer.material.SetTexture("_MainTex", icon);
                    }
                }

                Color tint = Color.white;
                string hex = HiddenBrushSet.GetPaletteColor(paletteId);
                HiddenBrushSet.TryParseHexColor(hex, out tint);
                m_SetBadgeIconRenderer.material.SetColor("_Color", tint);
                if (m_SetBadgeIconRenderer.material.HasProperty("_Tint"))
                {
                    m_SetBadgeIconRenderer.material.SetColor("_Tint", tint);
                }
                if (m_SetBadgeIconRenderer.material.HasProperty("_SecondaryColor"))
                {
                    m_SetBadgeIconRenderer.material.SetColor("_SecondaryColor", tint);
                }
                if (m_SetBadgeIconRenderer.material.HasProperty("_Activated"))
                {
                    m_SetBadgeIconRenderer.material.SetFloat("_Activated", 1.0f);
                }
                if (m_SetBadgeIconRenderer.material.HasProperty("_Saturation"))
                {
                    m_SetBadgeIconRenderer.material.SetFloat("_Saturation", 1.0f);
                }
            }
        }

        static Texture2D FindPaletteButtonTexture(string paletteId)
        {
            if (string.IsNullOrEmpty(paletteId))
            {
                return null;
            }
            Texture2D fromConfig = BrushPaletteConfig.GetSlotIcon(paletteId);
            if (fromConfig != null)
            {
                return fromConfig;
            }
            if (s_CachedCurationPanel == null)
            {
                s_CachedCurationPanel = UnityEngine.Object.FindObjectOfType<BrushCurationPanel>(true);
            }
            if (s_CachedCurationPanel == null)
            {
                return null;
            }

            ActionToggleButton[] toggles =
                s_CachedCurationPanel.GetComponentsInChildren<ActionToggleButton>(true);
            for (int i = 0; i < toggles.Length; ++i)
            {
                if (toggles[i] == null)
                {
                    continue;
                }
                if (!string.Equals(
                        toggles[i].gameObject.name,
                        paletteId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                return toggles[i].GetCurrentButtonTexture();
            }
            return null;
        }
    }
}


/*
using System;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

namespace TiltBrush
{

    public class BrushTypeButton : BaseButton
    {
        [SerializeField] private Texture2D m_PreviewBGTexture;
        [SerializeField] private GameObject m_AudioReactiveIcon;
        [SerializeField] private GameObject m_ExperimentalIcon;

        [NonSerialized] public BrushDescriptor m_Brush;
        [NonSerialized] public Vector3 m_OriginPosition;

        protected PreviewCubeScript m_PreviewCubeScript;
        private Renderer m_AudioReactiveIconRenderer;
        private Renderer m_ExperimentalIconRenderer;
        private Vector3 m_AudioReactiveIconBaseLocalPos;
        private Vector3 m_ExperimentalIconBaseLocalPos;
        private Texture2D m_BrushIconTexture;

        private BrushGrid m_ParentGrid;

        // Set-membership badge (upper right). Custom writable slots only.
        private GameObject m_SetBadgeRoot;
        private Renderer m_SetBadgePlateRenderer;
        private Renderer m_SetBadgeIconRenderer;
        private static BrushCurationPanel s_CachedCurationPanel;

        const string kBadgeRootName = "SetBadge";
        const string kBadgePlateName = "BadgePlate";
        const string kBadgeIconName = "BadgeIcon";
        // Closer to the cell corner than AudioReactive/Experimental (those sit at 0.25).
        // Base size at scale 1. Upper-right of the plate is the fixed corner.
        const float kBadgeBasePlate = 0.275f;
        const float kBadgeBaseIcon = 0.225f;
        const float kBadgeZ = -0.05f;
        static readonly Vector2 kBadgeFixedCorner = new Vector2(
            0.38f + kBadgeBasePlate * 0.5f,
            0.38f + kBadgeBasePlate * 0.5f);

        [SerializeField]
        [Tooltip("Used only if BrushGrid has no SetBadgeScale. 1 = default, 1.5 = 150 percent.")]
        float m_SetBadgeScale = 1f;

        override protected void Awake()
        {
            base.Awake();
            m_AudioReactiveIconRenderer = m_AudioReactiveIcon.GetComponent<Renderer>();
            m_ExperimentalIconRenderer = m_ExperimentalIcon.GetComponent<Renderer>();
            m_OriginPosition = transform.localPosition;
            m_ParentGrid = GetComponentInParent<BrushGrid>();
            EnsureSetBadge();
        }

        protected override void OnSelectedLocaleChanged(Locale locale)
        {
            if (m_Brush != null)
            {
                if (Config.IsExperimental)
                {
                    SetDescriptionText(m_Brush.Description, m_Brush.m_DescriptionExtra);
                }
                else
                {
                    SetDescriptionText(m_Brush.Description);
                }
            }
        }

        override protected void OnDescriptionChanged()
        {
            m_PreviewCubeScript = m_Description.GetComponent<PreviewCubeScript>();
            base.OnDescriptionChanged();
        }

        override protected void OnRegisterComponent()
        {
            base.OnRegisterComponent();
            m_AudioReactiveIconBaseLocalPos = m_AudioReactiveIcon.transform.localPosition;
            m_ExperimentalIconBaseLocalPos = m_ExperimentalIcon.transform.localPosition;
        }

        override protected void ConfigureTextureAtlas()
        {
            if (!BrushPaletteConfig.GetBrushCellUseAtlas())
            {
                m_AtlasTexture = false;
            }

            if (m_AtlasTexture && SketchControlsScript.m_Instance.AtlasIconTextures)
            {
                RefreshAtlasedMaterial();
            }
            else
            {
                base.ConfigureTextureAtlas();
            }
        }

        public void SetButtonProperties(BrushDescriptor rBrush)
        {
            m_Brush = rBrush;

            Texture2D buttonTexture = rBrush.m_ButtonTexture;
            if (buttonTexture == null)
            {
                Debug.LogWarningFormat(
                    rBrush,
                    "Button Texture not set for {0}, {1}", rBrush.DurableName, rBrush.m_Guid);
                buttonTexture = BrushCatalog.m_Instance.DefaultBrush.m_ButtonTexture;
            }
            m_BrushIconTexture = buttonTexture;
            m_PreviewCubeScript.SetSampleQuadTexture(buttonTexture);
            SetButtonTexture(buttonTexture);

            if (Config.IsExperimental)
            {
                SetDescriptionText(rBrush.Description, rBrush.m_DescriptionExtra);
            }
            else
            {
                SetDescriptionText(rBrush.Description);
            }
            m_AudioReactiveIcon.SetActive(rBrush.m_AudioReactive &&
                VisualizerManager.m_Instance.VisualsRequested);
            m_ButtonHasPressedAudio = (rBrush.m_ButtonAudio == null);
            m_ExperimentalIcon.SetActive(App.Instance.IsBrushExperimental(rBrush));

            SetButtonAvailable(true);
            RefreshSetBadge();
        }

        override protected void OnDescriptionActivated()
        {
            if (m_AtlasTexture)
            {
                SetButtonTexture(m_PreviewBGTexture);
            }
            else
            {
                m_ButtonRenderer.material.mainTexture = m_PreviewBGTexture;
            }
        }

        override protected void OnDescriptionDeactivated()
        {
            SetButtonSelected(m_ButtonSelected);
        }

        override protected void OnButtonPressed()
        {
            if (m_ParentGrid != null && m_ParentGrid.HideModeActive)
            {
                HiddenBrushSet.Toggle(m_Brush.m_Guid);
                SetButtonAvailable(true);
                SetColor(Color.white);
                RefreshSetBadge();
            }
            else
            {
                BrushController.m_Instance.SetActiveBrush(m_Brush);
            }
        }

        override public void ResetState()
        {
            base.ResetState();
            PlaceIconsOnPreviewCube(0.0f);
        }

        override public void SetButtonSelected(bool bSelected)
        {
            base.SetButtonSelected(bSelected);
            if (bSelected)
            {
                m_AudioReactiveIconRenderer.material.SetFloat("_Activated", 1.0f);
                m_ExperimentalIconRenderer.material.SetFloat("_Activated", 1.0f);
            }
            else
            {
                m_AudioReactiveIconRenderer.material.SetFloat("_Activated", 0.0f);
                m_ExperimentalIconRenderer.material.SetFloat("_Activated", 0.0f);
            }
            if (m_DescriptionState == DescriptionState.Deactivated && m_Brush != null)
            {
                if (m_AtlasTexture)
                {
                    SetButtonTexture(m_BrushIconTexture);
                }
                else
                {
                    m_ButtonRenderer.material.mainTexture = m_CurrentButtonTexture;
                }
            }
        }

        override public void UpdateVisuals()
        {
            base.UpdateVisuals();
            ApplySetBadgeLayout(0.0f);
            if (m_PreviewCubeScript != null)
            {
                if (m_DescriptionState != DescriptionState.Deactivated)
                {
                    m_PreviewCubeScript.SetSelected(m_ButtonSelected);
                }

                PlaceIconsOnPreviewCube(m_DescriptionActivateTimer);
            }
        }

        void PlaceIconsOnPreviewCube(float offsetPercent)
        {
            float offset = offsetPercent * transform.localScale.z * 2.0f;
            offset += m_AudioReactiveIconBaseLocalPos.z * offsetPercent;

            Vector3 localPos = m_AudioReactiveIconBaseLocalPos;
            localPos.z -= offset;
            m_AudioReactiveIcon.transform.localPosition = localPos;

            localPos = m_ExperimentalIconBaseLocalPos;
            localPos.z -= offset;
            m_ExperimentalIcon.transform.localPosition = localPos;

            if (m_SetBadgeRoot != null)
            {
                ApplySetBadgeLayout(offset);
            }
        }

        // Shelf membership is shown on the corner badge only, not the snapshot face.
        // AudioReactive / Experimental icon color comes from BrushGrid inspector tints
        // (not the panel wash color). That is the blue you saw on the grid.
        override public void SetColor(Color rColor)
        {
            base.SetColor(rColor);
            Color audioTint = rColor;
            Color experimentalTint = rColor;
            if (m_ParentGrid != null)
            {
                audioTint = m_ParentGrid.AudioReactiveIconTint;
                experimentalTint = m_ParentGrid.ExperimentalIconTint;
            }
            ApplyMarkerTint(m_AudioReactiveIconRenderer, audioTint);
            ApplyMarkerTint(m_ExperimentalIconRenderer, experimentalTint);
            RefreshSetBadge();
        }

        protected override void SetMaterialColor(Color rColor)
        {
            if (m_AtlasTexture)
            {
                base.SetMaterialColor(rColor);
                return;
            }

            Color photo = Color.white;
            photo.a = rColor.a;
            m_ButtonRenderer.material.SetColor("_Color", photo);
        }


        static void ApplyMarkerTint(Renderer renderer, Color tint)
        {
            if (renderer == null || renderer.material == null)
            {
                return;
            }
            renderer.material.SetColor("_Color", tint);
            if (renderer.material.HasProperty("_Tint"))
            {
                renderer.material.SetColor("_Tint", tint);
            }
            if (renderer.material.HasProperty("_SecondaryColor"))
            {
                renderer.material.SetColor("_SecondaryColor", tint);
            }
        }

        void EnsureSetBadge()
        {
            Transform existing = transform.Find(kBadgeRootName);
            if (existing != null)
            {
                m_SetBadgeRoot = existing.gameObject;
                Transform plateXf = existing.Find(kBadgePlateName);
                Transform iconXf = existing.Find(kBadgeIconName);
                if (plateXf != null)
                {
                    m_SetBadgePlateRenderer = plateXf.GetComponent<Renderer>();
                }
                if (iconXf != null)
                {
                    m_SetBadgeIconRenderer = iconXf.GetComponent<Renderer>();
                }
                if (m_SetBadgePlateRenderer != null && m_SetBadgeIconRenderer != null)
                {
                    ApplySetBadgeLayout(0.0f);
                    return;
                }
            }

            Mesh quad = GetSharedQuadMesh();
            Material iconMat = null;
            if (m_ExperimentalIconRenderer != null && m_ExperimentalIconRenderer.sharedMaterial != null)
            {
                iconMat = new Material(m_ExperimentalIconRenderer.sharedMaterial);
            }

            m_SetBadgeRoot = new GameObject(kBadgeRootName);
            m_SetBadgeRoot.transform.SetParent(transform, false);
            m_SetBadgeRoot.transform.localRotation = Quaternion.identity;
            m_SetBadgeRoot.transform.localScale = Vector3.one;

            GameObject plate = new GameObject(kBadgePlateName);
            plate.transform.SetParent(m_SetBadgeRoot.transform, false);
            plate.transform.localPosition = Vector3.zero;
            plate.transform.localRotation = Quaternion.identity;
            MeshFilter plateFilter = plate.AddComponent<MeshFilter>();
            plateFilter.sharedMesh = quad;
            m_SetBadgePlateRenderer = plate.AddComponent<MeshRenderer>();
            m_SetBadgePlateRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_SetBadgePlateRenderer.receiveShadows = false;
            if (iconMat != null)
            {
                m_SetBadgePlateRenderer.material = new Material(iconMat);
            }
            m_SetBadgePlateRenderer.material.color = Color.black;
            if (m_SetBadgePlateRenderer.material.HasProperty("_Color"))
            {
                m_SetBadgePlateRenderer.material.SetColor("_Color", Color.black);
            }
            if (m_SetBadgePlateRenderer.material.HasProperty("_MainTex"))
            {
                m_SetBadgePlateRenderer.material.SetTexture("_MainTex", Texture2D.whiteTexture);
            }

            GameObject iconGo = new GameObject(kBadgeIconName);
            iconGo.transform.SetParent(m_SetBadgeRoot.transform, false);
            iconGo.transform.localPosition = new Vector3(0f, 0f, -0.01f);
            iconGo.transform.localRotation = Quaternion.identity;
            MeshFilter iconFilter = iconGo.AddComponent<MeshFilter>();
            iconFilter.sharedMesh = quad;
            m_SetBadgeIconRenderer = iconGo.AddComponent<MeshRenderer>();
            m_SetBadgeIconRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_SetBadgeIconRenderer.receiveShadows = false;
            if (iconMat != null)
            {
                m_SetBadgeIconRenderer.material = iconMat;
            }

            ApplySetBadgeLayout(0.0f);
            m_SetBadgeRoot.SetActive(false);
        }

        Mesh GetSharedQuadMesh()
        {
            if (m_ExperimentalIcon != null)
            {
                MeshFilter mf = m_ExperimentalIcon.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    return mf.sharedMesh;
                }
            }
            GameObject tmp = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Mesh mesh = tmp.GetComponent<MeshFilter>().sharedMesh;
            UnityEngine.Object.Destroy(tmp);
            return mesh;
        }


        float GetSetBadgeScale()
        {
            BrushPaletteConfig config =
                Resources.Load<BrushPaletteConfig>("ScriptableObjects/BrushPaletteConfig");
            if (config != null && config.setBadgeScale > 0.01f)
            {
                return config.setBadgeScale;
            }
            return Mathf.Max(0.01f, m_SetBadgeScale);
        }


        void ApplySetBadgeLayout(float previewZOffset)
        {
            if (m_SetBadgeRoot == null)
            {
                return;
            }

            float scale = GetSetBadgeScale();
            float plate = kBadgeBasePlate * scale;
            float icon = kBadgeBaseIcon * scale;

            Vector3 pos = new Vector3(
                kBadgeFixedCorner.x - plate * 0.5f,
                kBadgeFixedCorner.y - plate * 0.5f,
                kBadgeZ - previewZOffset);
            m_SetBadgeRoot.transform.localPosition = pos;

            if (m_SetBadgePlateRenderer != null)
            {
                m_SetBadgePlateRenderer.transform.localScale = new Vector3(plate, plate, plate);
            }
            if (m_SetBadgeIconRenderer != null)
            {
                m_SetBadgeIconRenderer.transform.localScale = new Vector3(icon, icon, icon);
            }
        }

        void RefreshSetBadge()
        {
            if (m_SetBadgeRoot == null)
            {
                EnsureSetBadge();
            }
            if (m_SetBadgeRoot == null)
            {
                return;
            }

            string paletteId = null;
            if (m_Brush != null)
            {
                paletteId = HiddenBrushSet.GetPaletteIdForBrush(m_Brush.m_Guid);
            }

            ApplySetBadgeLayout(0.0f);

            bool show = !string.IsNullOrEmpty(paletteId) &&
                        HiddenBrushSet.IsWritablePalette(paletteId);
            m_SetBadgeRoot.SetActive(show);
            if (!show)
            {
                return;
            }

            if (m_SetBadgePlateRenderer != null && m_SetBadgePlateRenderer.material != null)
            {
                m_SetBadgePlateRenderer.material.SetColor("_Color", Color.black);
                if (m_SetBadgePlateRenderer.material.HasProperty("_Activated"))
                {
                    m_SetBadgePlateRenderer.material.SetFloat("_Activated", 1.0f);
                }
            }

            if (m_SetBadgeIconRenderer != null && m_SetBadgeIconRenderer.material != null)
            {
                Texture2D icon = FindPaletteButtonTexture(paletteId);
                if (icon != null)
                {
                    m_SetBadgeIconRenderer.material.mainTexture = icon;
                    if (m_SetBadgeIconRenderer.material.HasProperty("_MainTex"))
                    {
                        m_SetBadgeIconRenderer.material.SetTexture("_MainTex", icon);
                    }
                }

                Color tint = Color.white;
                string hex = HiddenBrushSet.GetPaletteColor(paletteId);
                HiddenBrushSet.TryParseHexColor(hex, out tint);
                // ExperimentalBrushIcon defaults _Tint to pale cyan. Replace with slot color
                // and drive _Color the same way so the glyph reads as neon red/orange/etc.
                m_SetBadgeIconRenderer.material.SetColor("_Color", tint);
                if (m_SetBadgeIconRenderer.material.HasProperty("_Tint"))
                {
                    m_SetBadgeIconRenderer.material.SetColor("_Tint", tint);
                }
                if (m_SetBadgeIconRenderer.material.HasProperty("_SecondaryColor"))
                {
                    m_SetBadgeIconRenderer.material.SetColor("_SecondaryColor", tint);
                }
                if (m_SetBadgeIconRenderer.material.HasProperty("_Activated"))
                {
                    m_SetBadgeIconRenderer.material.SetFloat("_Activated", 1.0f);
                }
                if (m_SetBadgeIconRenderer.material.HasProperty("_Saturation"))
                {
                    m_SetBadgeIconRenderer.material.SetFloat("_Saturation", 1.0f);
                }
            }
        }

        static Texture2D FindPaletteButtonTexture(string paletteId)
        {
            if (string.IsNullOrEmpty(paletteId))
            {
                return null;
            }
            if (s_CachedCurationPanel == null)
            {
                s_CachedCurationPanel = UnityEngine.Object.FindObjectOfType<BrushCurationPanel>(true);
            }
            if (s_CachedCurationPanel == null)
            {
                return null;
            }

            ActionToggleButton[] toggles =
                s_CachedCurationPanel.GetComponentsInChildren<ActionToggleButton>(true);
            for (int i = 0; i < toggles.Length; ++i)
            {
                if (toggles[i] == null)
                {
                    continue;
                }
                if (!string.Equals(
                        toggles[i].gameObject.name,
                        paletteId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                return toggles[i].GetCurrentButtonTexture();
            }
            return null;
        }

    } // Functions Complete

} // Tiltbrush

*/