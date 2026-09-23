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

namespace TiltBrush
{
    public class RegisterVisualizerObject : MonoBehaviour
    {
        [SerializeField] public bool m_ShowInReactorModeOnly = false;

        // Update 2134 RegisterVisualizerObject.Awake
        // Purpose: Add diagnostic logging to determine why visualizer objects are not appearing in VisualizerManager.m_VisualizerObjects during Simulated BPM tests.
        void Awake()
        {
            if (VisualizerManager.m_Instance == null)
            {
                Debug.LogError("[RegisterVisualizerObject] VisualizerManager.m_Instance is NULL at Awake time on " + gameObject.name);
            }
            else
            {
                VisualizerManager.m_Instance.RegisterVisualizerObject(this.gameObject);
                Debug.Log("[RegisterVisualizerObject] Registered: " + gameObject.name);
            }
        }
    }
}
