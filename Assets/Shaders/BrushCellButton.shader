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

// Brush grid only. Do not assign this on other panel buttons.
// Copy of Custom/PanelButton with a plate key and stroke gain.
Shader "Custom/BrushCellButton" {
  Properties {
    _Color ("Main Color", Color) = (1,1,1,1)
    _MainTex ("Texture", 2D) = "white" {}
    _PlateColor ("Plate Color", Color) = (0,0,0,1)
    _StrokeGain ("Stroke Gain", Range(0.1, 1.5)) = 0.75
    _PlateThreshold ("Plate Threshold", Range(0.01, 0.4)) = 0.08
  }
  SubShader {
    Tags {"Queue"="AlphaTest+20"}

    Pass {
      Lighting Off

      CGPROGRAM
      #pragma vertex vert
      #pragma fragment frag
      #pragma multi_compile __ HDR_EMULATED HDR_SIMPLE

      #include "UnityCG.cginc"
      #include "Assets/Shaders/Include/Brush.cginc"
      #include "Assets/Shaders/Include/Hdr.cginc"

      sampler2D _MainTex;
      fixed4 _Color;
      fixed4 _PlateColor;
      float _StrokeGain;
      float _PlateThreshold;
      uniform float _Activated;
      uniform float _PanelMipmapBias;

      struct appdata_t {
        float4 vertex : POSITION;
        float2 texcoord : TEXCOORD0;

        UNITY_VERTEX_INPUT_INSTANCE_ID
      };

      struct v2f {
        float4 vertex : POSITION;
        float4 texcoord : TEXCOORD0;

        UNITY_VERTEX_OUTPUT_STEREO
      };

      v2f vert (appdata_t v)
      {
        v2f o;

        UNITY_SETUP_INSTANCE_ID(v);
        UNITY_INITIALIZE_OUTPUT(v2f, o);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

        if (_Activated) v.vertex.xyz += float3(0,0,-.2);
        o.vertex = UnityObjectToClipPos(v.vertex);
        o.texcoord = float4(v.texcoord.xy, 0, _PanelMipmapBias);
        return o;
      }

      fixed4 frag (v2f i) : COLOR
      {
        fixed4 c = tex2Dbias(_MainTex, i.texcoord);
        float lum = dot(c.rgb, float3(0.299, 0.587, 0.114));
        float thresh = max(_PlateThreshold, 0.0001);
        float strokeAmt = saturate(lum / thresh);
        c.rgb = lerp(_PlateColor.rgb, c.rgb * _StrokeGain, strokeAmt);

        if( _Activated > 0.5f )
        {
          if( (abs(i.texcoord.y - .5f) < .425f) && (abs(i.texcoord.x - .5f) < .425f) )
          {
            c.rgb = .5 - c.rgb;
          }
          else
          {
            c.rgb = 1;
          }
        }

        c.rgb *= _Color.rgb;

        return encodeHdr(saturate(c.rgb * c.a));
      }
      ENDCG
    }
  }
  FallBack "Diffuse"
}
